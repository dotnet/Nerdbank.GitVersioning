// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! Release branch preparation and the Git mutations it requires.

use std::fmt;
use std::path::Path;
use std::process::Command;

use git2::build::CheckoutBuilder;
use git2::{BranchType, Oid, Repository, Signature, Status, StatusOptions};
use serde::{Serialize, Serializer};

use crate::{
    GitContext, GitEngine, ReleaseOptions, ReleaseVersionIncrement, SemanticVersion, Version,
    VersionFile, VersionFileRequirements, VersionOptions,
};

/// Classifies failures that can occur while preparing a release.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ReleasePreparationError {
    /// The project directory is not in a Git repository.
    NoGitRepo,
    /// The index or working tree contains changes.
    UncommittedChanges,
    /// The release branch template is invalid.
    InvalidBranchNameSetting,
    /// The release tag template is invalid.
    InvalidTagNameSetting,
    /// No applicable version file was found.
    NoVersionFile,
    /// A requested version is older than the current version.
    VersionDecrement,
    /// The development version would not increment.
    NoVersionIncrement,
    /// The release branch already exists.
    BranchAlreadyExists,
    /// Git user name or email is not configured.
    UserNotConfigured,
    /// `HEAD` is detached.
    DetachedHead,
    /// The selected increment cannot be applied to the current version.
    InvalidVersionIncrementSetting,
    /// The Git executable required for signing failed.
    GitCommandFailed,
    /// An in-process libgit2 operation failed.
    GitOperationFailed,
    /// A version file could not be read or written.
    VersionFileError,
}

/// A release preparation failure with a stable classification and useful detail.
#[derive(Debug)]
pub struct ReleasePreparationException {
    /// The stable error classification.
    pub error: ReleasePreparationError,
    message: String,
}

impl ReleasePreparationException {
    fn new(error: ReleasePreparationError, message: impl Into<String>) -> Self {
        Self {
            error,
            message: message.into(),
        }
    }
}

impl fmt::Display for ReleasePreparationException {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(&self.message)
    }
}

impl std::error::Error for ReleasePreparationException {}

/// A result returned by release preparation.
pub type ReleasePreparationResult<T> = std::result::Result<T, ReleasePreparationException>;

/// Selects the rendered output format.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub enum ReleaseManagerOutputMode {
    /// Human-readable status lines.
    #[default]
    Text,
    /// A JSON [`ReleaseInfo`] object.
    Json,
}

/// Options controlling one release preparation operation.
#[derive(Clone, Debug)]
pub struct PrepareReleaseOptions {
    /// An optional prerelease tag for the release branch.
    pub release_unstable_tag: Option<String>,
    /// An explicit next development version.
    pub next_version: Option<Version>,
    /// An override for the configured development-version increment.
    pub version_increment: Option<ReleaseVersionIncrement>,
    /// The output representation to produce.
    pub output_mode: ReleaseManagerOutputMode,
    /// A commit message pattern in which `{0}` is replaced by the new version.
    pub commit_message: Option<String>,
    /// Calculate and report changes without modifying the repository.
    pub dry_run: bool,
    /// Merge the release branch into the development branch.
    pub merge_release_branch: bool,
}

impl Default for PrepareReleaseOptions {
    fn default() -> Self {
        Self {
            release_unstable_tag: None,
            next_version: None,
            version_increment: None,
            output_mode: ReleaseManagerOutputMode::Text,
            commit_message: None,
            dry_run: false,
            merge_release_branch: true,
        }
    }
}

/// Information about a branch created or updated during release preparation.
#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "PascalCase")]
pub struct ReleaseBranchInfo {
    /// The friendly branch name.
    pub name: String,
    /// The branch-tip object ID.
    pub commit: String,
    /// The version configured on the branch.
    #[serde(serialize_with = "serialize_semantic_version")]
    pub version: SemanticVersion,
}

/// Information about the branches affected by release preparation.
#[derive(Clone, Debug, Eq, PartialEq, Serialize)]
#[serde(rename_all = "PascalCase")]
pub struct ReleaseInfo {
    /// The branch from which the release was prepared.
    pub current_branch: ReleaseBranchInfo,
    /// The newly-created release branch, or `None` when advancing one.
    pub new_branch: Option<ReleaseBranchInfo>,
}

impl ReleaseInfo {
    /// Serializes this result using the same property names as the .NET implementation.
    pub fn to_json(&self) -> ReleasePreparationResult<String> {
        serde_json::to_string_pretty(self).map_err(|error| {
            ReleasePreparationException::new(
                ReleasePreparationError::VersionFileError,
                format!("Failed to serialize release information: {error}"),
            )
        })
    }
}

/// The complete result of a release preparation operation.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct PreparedRelease {
    /// The final (or simulated) branch information.
    pub info: ReleaseInfo,
    /// Text or JSON suitable for writing to standard output.
    pub output: String,
    /// Whether the repository was modified.
    pub changed: bool,
}

/// Creates or advances release branches and updates their version files.
#[derive(Clone, Copy, Debug, Default)]
pub struct ReleaseManager;

impl ReleaseManager {
    /// Creates a release manager.
    #[must_use]
    pub const fn new() -> Self {
        Self
    }

    /// Prepares a release rooted at `project_directory`.
    pub fn prepare_release(
        &self,
        project_directory: impl AsRef<Path>,
        options: &PrepareReleaseOptions,
    ) -> ReleasePreparationResult<PreparedRelease> {
        let project_directory = project_directory.as_ref();
        let context = GitContext::create(project_directory, None, GitEngine::ReadWrite)
            .map_err(|error| version_file_error("Failed to discover repository", error))?;
        if !context.is_repository() {
            return Err(ReleasePreparationException::new(
                ReleasePreparationError::NoGitRepo,
                format!(
                    "No git repository found above directory '{}'.",
                    project_directory.display()
                ),
            ));
        }

        let repository = context.repository().expect("repository context");
        ensure_clean(repository, project_directory)?;
        let head = repository.head().map_err(git_operation_error)?;
        if !head.is_branch() {
            return Err(ReleasePreparationException::new(
                ReleasePreparationError::DetachedHead,
                "Detached head. Check out a branch first.",
            ));
        }
        let original_branch_name = head.shorthand().map_err(git_operation_error)?;
        let original_branch_name = original_branch_name.to_owned();
        let original_tip = head.peel_to_commit().map_err(git_operation_error)?.id();
        get_signature(repository)?;

        let (version_options, _) = VersionFile::new(&context)
            .get_version(VersionFileRequirements::default())
            .map_err(|error| version_file_error("Failed to load version file", error))?;
        let version_options = version_options.ok_or_else(|| {
            ReleasePreparationException::new(
                ReleasePreparationError::NoVersionFile,
                format!(
                    "Failed to load version file for directory '{}'.",
                    project_directory.display()
                ),
            )
        })?;
        let current_version = version_options.version.as_ref().ok_or_else(|| {
            ReleasePreparationException::new(
                ReleasePreparationError::NoVersionFile,
                "The applicable version file does not specify a version.",
            )
        })?;
        let release_branch_name = format_release_branch_name(&version_options)?;
        let release_version = current_version
            .set_first_prerelease_tag(options.release_unstable_tag.as_deref().unwrap_or(""))
            .map_err(|error| version_file_error("Invalid release prerelease tag", error))?;

        if original_branch_name.eq_ignore_ascii_case(&release_branch_name) {
            let simulated_info = ReleaseInfo {
                current_branch: branch_info(
                    &release_branch_name,
                    original_tip,
                    release_version.clone(),
                ),
                new_branch: None,
            };
            if options.dry_run {
                return finish(
                    simulated_info,
                    options.output_mode,
                    format!(
                        "What-if: {release_branch_name} branch would be advanced from {current_version} to {release_version}."
                    ),
                    false,
                );
            }

            update_version(
                project_directory,
                current_version,
                &release_version,
                options.commit_message.as_deref(),
            )?;
            let repository =
                Repository::discover(project_directory).map_err(git_operation_error)?;
            let final_tip = repository
                .head()
                .and_then(|head| head.peel_to_commit())
                .map_err(git_operation_error)?
                .id();
            return finish(
                ReleaseInfo {
                    current_branch: branch_info(
                        &release_branch_name,
                        final_tip,
                        release_version.clone(),
                    ),
                    new_branch: None,
                },
                options.output_mode,
                format!(
                    "{release_branch_name} branch advanced from {current_version} to {release_version}."
                ),
                true,
            );
        }

        let next_dev_version = get_next_dev_version(&version_options, options)?;
        if current_version.version == next_dev_version.version {
            return Err(ReleasePreparationException::new(
                ReleasePreparationError::NoVersionIncrement,
                format!(
                    "Version on '{original_branch_name}' is already set to next version {}.",
                    next_dev_version.version
                ),
            ));
        }
        if repository
            .find_branch(&release_branch_name, BranchType::Local)
            .is_ok()
        {
            return Err(ReleasePreparationException::new(
                ReleasePreparationError::BranchAlreadyExists,
                format!("Cannot create branch '{release_branch_name}' because it already exists."),
            ));
        }
        validate_branch_name(&release_branch_name)?;

        let simulated_info = ReleaseInfo {
            current_branch: branch_info(
                &original_branch_name,
                original_tip,
                next_dev_version.clone(),
            ),
            new_branch: Some(branch_info(
                &release_branch_name,
                original_tip,
                release_version.clone(),
            )),
        };
        if options.dry_run {
            return finish(
                simulated_info,
                options.output_mode,
                format!(
                    "What-if: {release_branch_name} branch would track v{release_version} stabilization and release.\nWhat-if: {original_branch_name} branch would track v{next_dev_version} development."
                ),
                false,
            );
        }

        let head_commit = repository
            .find_commit(original_tip)
            .map_err(git_operation_error)?;
        repository
            .branch(&release_branch_name, &head_commit, false)
            .map_err(git_operation_error)?;
        checkout_branch(repository, &release_branch_name)?;
        drop(head_commit);
        update_version(
            project_directory,
            current_version,
            &release_version,
            options.commit_message.as_deref(),
        )?;
        let repository = Repository::discover(project_directory).map_err(git_operation_error)?;
        checkout_branch(&repository, &original_branch_name)?;
        update_version(
            project_directory,
            current_version,
            &next_dev_version,
            options.commit_message.as_deref(),
        )?;

        let repository = Repository::discover(project_directory).map_err(git_operation_error)?;
        if options.merge_release_branch {
            merge_release_branch(&repository, &release_branch_name)?;
        }
        let final_original_tip = repository
            .head()
            .and_then(|head| head.peel_to_commit())
            .map_err(git_operation_error)?
            .id();
        let final_release_tip = repository
            .find_branch(&release_branch_name, BranchType::Local)
            .and_then(|branch| branch.get().peel_to_commit())
            .map_err(git_operation_error)?
            .id();
        finish(
            ReleaseInfo {
                current_branch: branch_info(
                    &original_branch_name,
                    final_original_tip,
                    next_dev_version.clone(),
                ),
                new_branch: Some(branch_info(
                    &release_branch_name,
                    final_release_tip,
                    release_version.clone(),
                )),
            },
            options.output_mode,
            format!(
                "{release_branch_name} branch now tracks v{release_version} stabilization and release.\n{original_branch_name} branch now tracks v{next_dev_version} development."
            ),
            true,
        )
    }
}

/// Formats the configured release branch name using the numeric current version.
pub fn format_release_branch_name(
    version_options: &VersionOptions,
) -> ReleasePreparationResult<String> {
    let version = version_options.version.as_ref().ok_or_else(|| {
        ReleasePreparationException::new(
            ReleasePreparationError::NoVersionFile,
            "The version options do not specify a version.",
        )
    })?;
    format_version_template(
        version_options
            .release_or_default()
            .branch_name_or_default(),
        &version.version.to_string(),
        ReleasePreparationError::InvalidBranchNameSetting,
        "branchName",
    )
}

/// Formats a release tag template using a semantic version.
pub fn format_release_tag_name(
    release_options: &ReleaseOptions,
    version: &SemanticVersion,
) -> ReleasePreparationResult<String> {
    format_version_template(
        release_options.tag_name_or_default(),
        &version.to_string(),
        ReleasePreparationError::InvalidTagNameSetting,
        "tagName",
    )
}

fn format_version_template(
    template: &str,
    version: &str,
    error: ReleasePreparationError,
    setting: &str,
) -> ReleasePreparationResult<String> {
    if template.is_empty() || !template.contains("{version}") {
        return Err(ReleasePreparationException::new(
            error,
            format!(
                "Invalid '{setting}' setting '{template}'. Missing version placeholder '{{version}}'."
            ),
        ));
    }
    Ok(template.replace("{version}", version))
}

fn ensure_clean(repository: &Repository, path: &Path) -> ReleasePreparationResult<()> {
    let mut options = StatusOptions::new();
    options
        .include_untracked(true)
        .recurse_untracked_dirs(true)
        .include_ignored(false);
    let statuses = repository
        .statuses(Some(&mut options))
        .map_err(git_operation_error)?;
    let changed = statuses
        .iter()
        .filter(|entry| entry.status() != Status::CURRENT)
        .collect::<Vec<_>>();
    if !changed.is_empty() {
        let details = changed
            .iter()
            .map(|entry| {
                format!(
                    "- {} changed with FileStatus {}",
                    entry.path().unwrap_or_default(),
                    file_status_name(entry.status())
                )
            })
            .collect::<Vec<_>>()
            .join("\n");
        return Err(ReleasePreparationException::new(
            ReleasePreparationError::UncommittedChanges,
            format!(
                "No uncommitted changes are allowed, but {} are present in directory '{}':\n{details}",
                changed.len(),
                path.display(),
            ),
        ));
    }
    Ok(())
}

fn file_status_name(status: Status) -> String {
    let names = [
        (Status::INDEX_NEW, "NewInIndex"),
        (Status::INDEX_MODIFIED, "ModifiedInIndex"),
        (Status::INDEX_DELETED, "DeletedFromIndex"),
        (Status::INDEX_RENAMED, "RenamedInIndex"),
        (Status::INDEX_TYPECHANGE, "TypeChangeInIndex"),
        (Status::WT_NEW, "NewInWorkdir"),
        (Status::WT_MODIFIED, "ModifiedInWorkdir"),
        (Status::WT_DELETED, "DeletedFromWorkdir"),
        (Status::WT_RENAMED, "RenamedInWorkdir"),
        (Status::WT_TYPECHANGE, "TypeChangeInWorkdir"),
        (Status::CONFLICTED, "Conflicted"),
    ];
    names
        .into_iter()
        .filter_map(|(flag, name)| status.contains(flag).then_some(name))
        .collect::<Vec<_>>()
        .join(", ")
}

fn get_next_dev_version(
    version_options: &VersionOptions,
    options: &PrepareReleaseOptions,
) -> ReleasePreparationResult<SemanticVersion> {
    let current = version_options.version.as_ref().ok_or_else(|| {
        ReleasePreparationException::new(
            ReleasePreparationError::NoVersionFile,
            "The version options do not specify a version.",
        )
    })?;
    let incremented = if let Some(version) = options.next_version {
        SemanticVersion::new(
            version,
            current.prerelease.clone(),
            current.build_metadata.clone(),
        )
        .map_err(|error| version_file_error("Invalid next version", error))?
    } else {
        let increment = options.version_increment.unwrap_or_else(|| {
            version_options
                .release_or_default()
                .version_increment_or_default()
        });
        if increment == ReleaseVersionIncrement::Build && current.version.build.is_none() {
            return Err(ReleasePreparationException::new(
                ReleasePreparationError::InvalidVersionIncrementSetting,
                format!(
                    "Cannot apply version increment 'build' to version '{current}' because it only has major and minor segments."
                ),
            ));
        }
        current.increment(increment).map_err(|error| {
            ReleasePreparationException::new(
                ReleasePreparationError::InvalidVersionIncrementSetting,
                error.to_string(),
            )
        })?
    };
    incremented
        .set_first_prerelease_tag(
            version_options
                .release_or_default()
                .first_unstable_tag_or_default(),
        )
        .map_err(|error| version_file_error("Invalid first unstable tag", error))
}

fn update_version(
    project_directory: &Path,
    old_version: &SemanticVersion,
    new_version: &SemanticVersion,
    commit_message: Option<&str>,
) -> ReleasePreparationResult<()> {
    if is_version_decrement(old_version, new_version) {
        return Err(ReleasePreparationException::new(
            ReleasePreparationError::VersionDecrement,
            format!(
                "Cannot change version from {old_version} to {new_version} because {new_version} is older than {old_version}."
            ),
        ));
    }

    let context = GitContext::create(project_directory, None, GitEngine::ReadWrite)
        .map_err(|error| version_file_error("Failed to open repository", error))?;
    let requirements = VersionFileRequirements::NON_MERGED_RESULT
        | VersionFileRequirements::VERSION_SPECIFIED
        | VersionFileRequirements::ACCEPT_INHERITING_FILE;
    let (version_options, locations) = VersionFile::new(&context)
        .get_version(requirements)
        .map_err(|error| version_file_error("Failed to load writable version file", error))?;
    let mut version_options = version_options.ok_or_else(|| {
        ReleasePreparationException::new(
            ReleasePreparationError::NoVersionFile,
            "No writable version file specifies a version.",
        )
    })?;
    if version_options.version.as_ref() == Some(new_version) {
        return Ok(());
    }

    if version_options.version_height_offset != Some(-1)
        && version_options.version_height_offset.is_some()
        && let (Some(local_version), Some(position)) = (
            version_options.version.as_ref(),
            version_options.version_height_position(),
        )
        && SemanticVersion::will_version_change_reset_version_height(
            local_version,
            new_version,
            position,
        )
        .map_err(|error| version_file_error("Failed to compare version heights", error))?
    {
        version_options.version_height_offset = None;
        version_options.version_height_offset_applies_to = None;
    }
    version_options.version = Some(new_version.clone());
    let directory = locations
        .version_specifying_version_directory
        .ok_or_else(|| {
            ReleasePreparationException::new(
                ReleasePreparationError::NoVersionFile,
                "The version-specifying directory could not be determined.",
            )
        })?;
    let path = VersionFile::new(&context)
        .set_version(directory, &version_options, true)
        .map_err(|error| version_file_error("Failed to write version file", error))?;
    context
        .stage(path)
        .map_err(|error| version_file_error("Failed to stage version file", error))?;
    let repository = context.repository().expect("repository context");
    let mut index = repository.index().map_err(git_operation_error)?;
    let tree_id = index.write_tree().map_err(git_operation_error)?;
    let parent = repository
        .head()
        .and_then(|head| head.peel_to_commit())
        .map_err(git_operation_error)?;
    if parent.tree_id() == tree_id {
        return Ok(());
    }

    let message = format_commit_message(
        commit_message.unwrap_or("Set version to '{0}'"),
        new_version,
    );
    if should_sign_commits(repository)? {
        run_git(
            repository,
            &["commit", "--gpg-sign", "--no-verify", "--message", &message],
        )?;
    } else {
        let signature = get_signature(repository)?;
        let tree = repository.find_tree(tree_id).map_err(git_operation_error)?;
        repository
            .commit(
                Some("HEAD"),
                &signature,
                &signature,
                &message,
                &tree,
                &[&parent],
            )
            .map_err(git_operation_error)?;
    }
    Ok(())
}

fn is_version_decrement(old: &SemanticVersion, new: &SemanticVersion) -> bool {
    new.version < old.version
        || (new.version == old.version && old.prerelease.is_empty() && !new.prerelease.is_empty())
}

fn format_commit_message(pattern: &str, version: &SemanticVersion) -> String {
    pattern
        .replace("{{", "\u{0}")
        .replace("}}", "\u{1}")
        .replace("{0}", &version.to_string())
        .replace('\u{0}', "{")
        .replace('\u{1}', "}")
}

fn checkout_branch(repository: &Repository, name: &str) -> ReleasePreparationResult<()> {
    repository
        .set_head(&format!("refs/heads/{name}"))
        .map_err(git_operation_error)?;
    repository
        .checkout_head(Some(CheckoutBuilder::new().safe()))
        .map_err(git_operation_error)?;
    Ok(())
}

fn merge_release_branch(
    repository: &Repository,
    release_branch_name: &str,
) -> ReleasePreparationResult<()> {
    if should_sign_commits(repository)? {
        return run_git(
            repository,
            &[
                "merge",
                "--gpg-sign",
                "--no-edit",
                "--no-verify",
                "--strategy-option=ours",
                &format!("refs/heads/{release_branch_name}"),
            ],
        );
    }

    let signature = get_signature(repository)?;
    let development = repository
        .head()
        .and_then(|head| head.peel_to_commit())
        .map_err(git_operation_error)?;
    let release = repository
        .find_branch(release_branch_name, BranchType::Local)
        .and_then(|branch| branch.get().peel_to_commit())
        .map_err(git_operation_error)?;
    if repository
        .graph_descendant_of(development.id(), release.id())
        .map_err(git_operation_error)?
    {
        return Ok(());
    }
    let tree = development.tree().map_err(git_operation_error)?;
    repository
        .commit(
            Some("HEAD"),
            &signature,
            &signature,
            &format!("Merge branch '{release_branch_name}'"),
            &tree,
            &[&development, &release],
        )
        .map_err(git_operation_error)?;
    Ok(())
}

fn should_sign_commits(repository: &Repository) -> ReleasePreparationResult<bool> {
    let config = repository.config().map_err(git_operation_error)?;
    match config.get_bool("commit.gpgSign") {
        Ok(value) => Ok(value),
        Err(error) if error.code() == git2::ErrorCode::NotFound => Ok(false),
        Err(error) => Err(git_operation_error(error)),
    }
}

fn run_git(repository: &Repository, arguments: &[&str]) -> ReleasePreparationResult<()> {
    let working_directory = repository.workdir().ok_or_else(|| {
        ReleasePreparationException::new(
            ReleasePreparationError::GitCommandFailed,
            "Signed commits cannot be created in a bare repository.",
        )
    })?;
    let output = Command::new("git")
        .args(arguments)
        .current_dir(working_directory)
        .output()
        .map_err(|error| {
            ReleasePreparationException::new(
                ReleasePreparationError::GitCommandFailed,
                format!("Commit signing is enabled, but Git could not be started: {error}"),
            )
        })?;
    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        let stdout = String::from_utf8_lossy(&output.stdout);
        let detail = if stderr.trim().is_empty() {
            stdout.trim()
        } else {
            stderr.trim()
        };
        return Err(ReleasePreparationException::new(
            ReleasePreparationError::GitCommandFailed,
            format!(
                "Git {} failed while creating a signed commit: {detail}",
                arguments.first().copied().unwrap_or_default()
            ),
        ));
    }
    Ok(())
}

fn get_signature(repository: &Repository) -> ReleasePreparationResult<Signature<'static>> {
    let config = repository.config().map_err(git_operation_error)?;
    let name = config.get_string("user.name").map_err(|_| {
        ReleasePreparationException::new(
            ReleasePreparationError::UserNotConfigured,
            "Cannot create commits in this repo because git user name and email are not configured.",
        )
    })?;
    let email = config.get_string("user.email").map_err(|_| {
        ReleasePreparationException::new(
            ReleasePreparationError::UserNotConfigured,
            "Cannot create commits in this repo because git user name and email are not configured.",
        )
    })?;
    if name.trim().is_empty() || email.trim().is_empty() {
        return Err(ReleasePreparationException::new(
            ReleasePreparationError::UserNotConfigured,
            "Cannot create commits in this repo because git user name and email are not configured.",
        ));
    }
    Signature::now(&name, &email).map_err(git_operation_error)
}

fn validate_branch_name(name: &str) -> ReleasePreparationResult<()> {
    if !git2::Reference::is_valid_name(&format!("refs/heads/{name}")) {
        return Err(ReleasePreparationException::new(
            ReleasePreparationError::InvalidBranchNameSetting,
            format!("The formatted release branch name '{name}' is not a valid Git reference."),
        ));
    }
    Ok(())
}

fn branch_info(name: &str, commit: Oid, version: SemanticVersion) -> ReleaseBranchInfo {
    ReleaseBranchInfo {
        name: name.to_owned(),
        commit: commit.to_string(),
        version,
    }
}

fn serialize_semantic_version<S>(
    version: &SemanticVersion,
    serializer: S,
) -> std::result::Result<S::Ok, S::Error>
where
    S: Serializer,
{
    serializer.serialize_str(&version.to_string())
}

fn finish(
    info: ReleaseInfo,
    mode: ReleaseManagerOutputMode,
    text: String,
    changed: bool,
) -> ReleasePreparationResult<PreparedRelease> {
    let output = match mode {
        ReleaseManagerOutputMode::Text => text,
        ReleaseManagerOutputMode::Json => info.to_json()?,
    };
    Ok(PreparedRelease {
        info,
        output,
        changed,
    })
}

fn git_operation_error(error: git2::Error) -> ReleasePreparationException {
    ReleasePreparationException::new(
        ReleasePreparationError::GitOperationFailed,
        format!("Git operation failed: {error}"),
    )
}

fn version_file_error(
    operation: &str,
    error: impl std::error::Error,
) -> ReleasePreparationException {
    ReleasePreparationException::new(
        ReleasePreparationError::VersionFileError,
        format!("{operation}: {error}"),
    )
}

#[cfg(test)]
mod tests {
    use std::fs;
    use std::path::PathBuf;
    use std::sync::atomic::{AtomicU64, Ordering};
    use std::time::{SystemTime, UNIX_EPOCH};

    use git2::{ObjectType, Repository, Signature};

    use super::*;

    static NEXT_DIRECTORY: AtomicU64 = AtomicU64::new(0);

    struct TestRepository {
        path: PathBuf,
    }

    impl TestRepository {
        fn new(version_json: Option<&str>, configure_user: bool) -> Self {
            let nonce = SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .expect("clock after epoch")
                .as_nanos();
            let sequence = NEXT_DIRECTORY.fetch_add(1, Ordering::Relaxed);
            let path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
                .join("target")
                .join("release-manager-tests")
                .join(format!("{}-{nonce}-{sequence}", std::process::id()));
            fs::create_dir_all(&path).unwrap();
            let repository = Repository::init(&path).unwrap();
            if configure_user {
                let mut config = repository.config().unwrap();
                config.set_str("user.name", "Release Test").unwrap();
                config.set_str("user.email", "release@example.com").unwrap();
            }
            if let Some(json) = version_json {
                fs::write(path.join("version.json"), json).unwrap();
                let mut index = repository.index().unwrap();
                index.add_path(Path::new("version.json")).unwrap();
                index.write().unwrap();
                let tree_id = index.write_tree().unwrap();
                let tree = repository.find_tree(tree_id).unwrap();
                let signature = Signature::now("Release Test", "release@example.com").unwrap();
                repository
                    .commit(
                        Some("HEAD"),
                        &signature,
                        &signature,
                        "Initial version",
                        &tree,
                        &[],
                    )
                    .unwrap();
            } else {
                fs::write(path.join("README"), "test").unwrap();
                let mut index = repository.index().unwrap();
                index.add_path(Path::new("README")).unwrap();
                index.write().unwrap();
                let tree_id = index.write_tree().unwrap();
                let tree = repository.find_tree(tree_id).unwrap();
                let signature = Signature::now("Release Test", "release@example.com").unwrap();
                repository
                    .commit(Some("HEAD"), &signature, &signature, "Initial", &tree, &[])
                    .unwrap();
            }
            drop(repository);
            Self { path }
        }

        fn repository(&self) -> Repository {
            Repository::open(&self.path).unwrap()
        }

        fn branch_version(&self, branch: &str) -> VersionOptions {
            let repository = self.repository();
            let commit = repository
                .find_branch(branch, BranchType::Local)
                .unwrap()
                .get()
                .peel_to_commit()
                .unwrap();
            let tree = commit.tree().unwrap();
            let blob = tree
                .get_path(Path::new("version.json"))
                .unwrap()
                .to_object(&repository)
                .unwrap()
                .peel(ObjectType::Blob)
                .unwrap();
            VersionOptions::from_json(
                std::str::from_utf8(blob.as_blob().unwrap().content()).unwrap(),
                "",
            )
            .unwrap()
        }

        fn current_branch(&self) -> String {
            self.repository()
                .head()
                .unwrap()
                .shorthand()
                .unwrap()
                .to_owned()
        }

        fn checkout_new_branch(&self, name: &str) {
            let repository = self.repository();
            let commit = repository.head().unwrap().peel_to_commit().unwrap();
            repository.branch(name, &commit, false).unwrap();
            checkout_branch(&repository, name).unwrap();
        }
    }

    impl Drop for TestRepository {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.path);
        }
    }

    fn json(version: &str) -> String {
        format!(r#"{{"version":"{version}"}}"#)
    }

    #[test]
    fn dry_run_reports_without_mutating() {
        let fixture = TestRepository::new(Some(&json("1.2-beta")), true);
        let before = fixture
            .repository()
            .head()
            .unwrap()
            .peel_to_commit()
            .unwrap()
            .id();
        let result = ReleaseManager::new()
            .prepare_release(
                &fixture.path,
                &PrepareReleaseOptions {
                    dry_run: true,
                    release_unstable_tag: Some("rc".to_owned()),
                    ..PrepareReleaseOptions::default()
                },
            )
            .unwrap();

        assert!(!result.changed);
        assert_eq!(result.info.current_branch.version.to_string(), "1.3-alpha");
        assert_eq!(
            result.info.new_branch.as_ref().unwrap().version.to_string(),
            "1.2-rc"
        );
        assert!(result.output.starts_with("What-if:"));
        let repository = fixture.repository();
        assert_eq!(
            repository.head().unwrap().peel_to_commit().unwrap().id(),
            before
        );
        assert!(repository.find_branch("v1.2", BranchType::Local).is_err());
    }

    #[test]
    fn prepares_and_merges_release_with_json_result() {
        let fixture = TestRepository::new(Some(&json("1.0-beta")), true);
        let original_branch = fixture.current_branch();
        let result = ReleaseManager::new()
            .prepare_release(
                &fixture.path,
                &PrepareReleaseOptions {
                    output_mode: ReleaseManagerOutputMode::Json,
                    ..PrepareReleaseOptions::default()
                },
            )
            .unwrap();

        assert!(result.changed);
        assert_eq!(fixture.current_branch(), original_branch);
        assert_eq!(
            fixture
                .branch_version(&original_branch)
                .version
                .unwrap()
                .to_string(),
            "1.1-alpha"
        );
        assert_eq!(
            fixture.branch_version("v1.0").version.unwrap().to_string(),
            "1.0"
        );
        let repository = fixture.repository();
        assert_eq!(
            repository
                .head()
                .unwrap()
                .peel_to_commit()
                .unwrap()
                .parent_count(),
            2
        );
        let value: serde_json::Value = serde_json::from_str(&result.output).unwrap();
        assert_eq!(value["CurrentBranch"]["Name"], original_branch);
        assert_eq!(value["NewBranch"]["Name"], "v1.0");
        assert_eq!(value["NewBranch"]["Version"], "1.0");
    }

    #[test]
    fn no_merge_leaves_divergent_single_parent_commits() {
        let fixture = TestRepository::new(Some(&json("1.0-beta")), true);
        ReleaseManager::new()
            .prepare_release(
                &fixture.path,
                &PrepareReleaseOptions {
                    merge_release_branch: false,
                    ..PrepareReleaseOptions::default()
                },
            )
            .unwrap();
        let repository = fixture.repository();
        assert_eq!(
            repository
                .head()
                .unwrap()
                .peel_to_commit()
                .unwrap()
                .parent_count(),
            1
        );
        assert_eq!(
            repository
                .find_branch("v1.0", BranchType::Local)
                .unwrap()
                .get()
                .peel_to_commit()
                .unwrap()
                .parent_count(),
            1
        );
    }

    #[test]
    fn advances_an_existing_release_branch() {
        let fixture = TestRepository::new(Some(&json("1.2-beta")), true);
        fixture.checkout_new_branch("v1.2");
        let result = ReleaseManager::new()
            .prepare_release(&fixture.path, &PrepareReleaseOptions::default())
            .unwrap();
        assert!(result.info.new_branch.is_none());
        assert_eq!(
            fixture.branch_version("v1.2").version.unwrap().to_string(),
            "1.2"
        );
    }

    #[test]
    fn custom_commit_message_is_formatted() {
        let fixture = TestRepository::new(Some(&json("1.0-beta")), true);
        ReleaseManager::new()
            .prepare_release(
                &fixture.path,
                &PrepareReleaseOptions {
                    commit_message: Some("{0} Custom {{message}}".to_owned()),
                    merge_release_branch: false,
                    ..PrepareReleaseOptions::default()
                },
            )
            .unwrap();
        let repository = fixture.repository();
        let message = repository
            .find_branch("v1.0", BranchType::Local)
            .unwrap()
            .get()
            .peel_to_commit()
            .unwrap()
            .summary()
            .unwrap()
            .unwrap()
            .to_owned();
        assert_eq!(message, "1.0 Custom {message}");
    }

    #[test]
    fn detects_validation_failures() {
        let outside = Path::new(env!("CARGO_MANIFEST_DIR"))
            .ancestors()
            .last()
            .unwrap()
            .join(format!("nbgv-release-no-repository-{}", std::process::id()));
        fs::create_dir_all(&outside).unwrap();
        let error = ReleaseManager::new()
            .prepare_release(&outside, &PrepareReleaseOptions::default())
            .unwrap_err();
        assert_eq!(error.error, ReleasePreparationError::NoGitRepo);
        fs::remove_dir_all(&outside).unwrap();

        let no_version = TestRepository::new(None, true);
        assert_eq!(
            ReleaseManager::new()
                .prepare_release(&no_version.path, &PrepareReleaseOptions::default())
                .unwrap_err()
                .error,
            ReleasePreparationError::NoVersionFile
        );

        let no_user = TestRepository::new(Some(&json("1.0-beta")), false);
        let repository = no_user.repository();
        let mut config = repository.config().unwrap();
        config.set_str("user.name", "").unwrap();
        config.set_str("user.email", "").unwrap();
        drop(config);
        drop(repository);
        assert_eq!(
            ReleaseManager::new()
                .prepare_release(&no_user.path, &PrepareReleaseOptions::default())
                .unwrap_err()
                .error,
            ReleasePreparationError::UserNotConfigured
        );
    }

    #[test]
    fn rejects_dirty_detached_existing_and_invalid_increment() {
        let dirty = TestRepository::new(Some(&json("1.0-beta")), true);
        fs::write(dirty.path.join("dirty"), "dirty").unwrap();
        assert_eq!(
            ReleaseManager::new()
                .prepare_release(&dirty.path, &PrepareReleaseOptions::default())
                .unwrap_err()
                .error,
            ReleasePreparationError::UncommittedChanges
        );

        let detached = TestRepository::new(Some(&json("1.0-beta")), true);
        let repository = detached.repository();
        let id = repository.head().unwrap().peel_to_commit().unwrap().id();
        repository.set_head_detached(id).unwrap();
        repository.checkout_head(None).unwrap();
        drop(repository);
        assert_eq!(
            ReleaseManager::new()
                .prepare_release(&detached.path, &PrepareReleaseOptions::default())
                .unwrap_err()
                .error,
            ReleasePreparationError::DetachedHead
        );

        let existing = TestRepository::new(Some(&json("1.0-beta")), true);
        let repository = existing.repository();
        let commit = repository.head().unwrap().peel_to_commit().unwrap();
        repository.branch("v1.0", &commit, false).unwrap();
        drop(commit);
        drop(repository);
        assert_eq!(
            ReleaseManager::new()
                .prepare_release(&existing.path, &PrepareReleaseOptions::default())
                .unwrap_err()
                .error,
            ReleasePreparationError::BranchAlreadyExists
        );

        let invalid_increment = TestRepository::new(
            Some(r#"{"version":"1.2","release":{"versionIncrement":"build"}}"#),
            true,
        );
        assert_eq!(
            ReleaseManager::new()
                .prepare_release(&invalid_increment.path, &PrepareReleaseOptions::default())
                .unwrap_err()
                .error,
            ReleasePreparationError::InvalidVersionIncrementSetting
        );
    }

    #[test]
    fn rejects_version_decrement_and_no_increment() {
        let decrement = TestRepository::new(Some(&json("1.2")), true);
        assert_eq!(
            ReleaseManager::new()
                .prepare_release(
                    &decrement.path,
                    &PrepareReleaseOptions {
                        release_unstable_tag: Some("rc".to_owned()),
                        ..PrepareReleaseOptions::default()
                    }
                )
                .unwrap_err()
                .error,
            ReleasePreparationError::VersionDecrement
        );

        let no_increment = TestRepository::new(Some(&json("1.2")), true);
        assert_eq!(
            ReleaseManager::new()
                .prepare_release(
                    &no_increment.path,
                    &PrepareReleaseOptions {
                        next_version: Some(Version::new(1, 2)),
                        ..PrepareReleaseOptions::default()
                    }
                )
                .unwrap_err()
                .error,
            ReleasePreparationError::NoVersionIncrement
        );
    }

    #[test]
    fn formats_branch_and_tag_templates() {
        let options = VersionOptions::from_json(
            r#"{"version":"1.2-beta","release":{"branchName":"release/v{version}"}}"#,
            "",
        )
        .unwrap();
        assert_eq!(
            format_release_branch_name(&options).unwrap(),
            "release/v1.2"
        );
        let release = ReleaseOptions {
            tag_name: Some("product-{version}".to_owned()),
            ..ReleaseOptions::default()
        };
        assert_eq!(
            format_release_tag_name(&release, &"1.2-rc".parse().unwrap()).unwrap(),
            "product-1.2-rc"
        );
        assert_eq!(
            format_release_tag_name(
                &ReleaseOptions {
                    tag_name: Some("invalid".to_owned()),
                    ..ReleaseOptions::default()
                },
                &"1.2".parse().unwrap()
            )
            .unwrap_err()
            .error,
            ReleasePreparationError::InvalidTagNameSetting
        );
    }

    #[test]
    fn signing_failure_is_explicit() {
        let fixture = TestRepository::new(Some(&json("1.0-beta")), true);
        let repository = fixture.repository();
        let mut config = repository.config().unwrap();
        config.set_bool("commit.gpgSign", true).unwrap();
        config.set_str("gpg.format", "openpgp").unwrap();
        config
            .set_str("user.signingKey", "definitely-not-a-real-key")
            .unwrap();
        drop(config);
        drop(repository);
        assert_eq!(
            ReleaseManager::new()
                .prepare_release(&fixture.path, &PrepareReleaseOptions::default())
                .unwrap_err()
                .error,
            ReleasePreparationError::GitCommandFailed
        );
    }
}
