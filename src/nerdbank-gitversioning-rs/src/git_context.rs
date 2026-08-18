// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! Repository discovery and the Git operations used by version calculation.
//!
//! This module deliberately does not read version files or walk history. It owns the
//! selected commit and provides the small, testable repository surface those layers need.

use std::env;
use std::path::{Path, PathBuf};

use chrono::{DateTime, FixedOffset, TimeZone};
use git2::{BranchType, Commit, ErrorCode, Oid, Repository, Status};

use crate::{Error, Result};

const NO_REPOSITORY_MESSAGE: &str = "Not a git repo";
const DISABLED_COMMIT_ID: &str = "nerdbankdisabled";

/// Selects how repository operations are performed.
#[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
pub enum GitEngine {
    /// Opens the repository but rejects mutations.
    #[default]
    ReadOnly,

    /// Opens the repository and permits mutations.
    ReadWrite,

    /// Does not open the repository or calculate Git-derived values.
    Disabled,
}

/// Describes what kind of context was created.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum GitContextKind {
    /// A repository was found and opened.
    Repository,

    /// A repository was found, but Git was explicitly disabled.
    Disabled,

    /// No repository was found.
    NoRepository,
}

/// The author and committer timestamps for a commit.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct CommitDates {
    /// The time recorded for the commit's author.
    pub author: DateTime<FixedOffset>,

    /// The time recorded for the committer.
    pub committer: DateTime<FixedOffset>,
}

/// Represents a directory and selected commit within a Git repository.
pub struct GitContext {
    repository: Option<Repository>,
    kind: GitContextKind,
    engine: GitEngine,
    working_tree_path: PathBuf,
    git_directory: Option<PathBuf>,
    repo_relative_project_directory: PathBuf,
    selected_commit: Option<Oid>,
}

impl GitContext {
    /// Discovers a repository from `path` or one of its ancestors.
    ///
    /// Git's own discovery is used, so linked worktrees and textual `.git` files are
    /// supported. A path outside a repository produces a usable [`GitContextKind::NoRepository`]
    /// context. Bare repositories are rejected because version calculation requires a
    /// working tree.
    pub fn create(
        path: impl AsRef<Path>,
        committish: Option<&str>,
        engine: GitEngine,
    ) -> Result<Self> {
        let requested_path = absolute_path(path.as_ref())?;
        match Repository::discover(&requested_path) {
            Ok(repository) => Self::from_repository(repository, requested_path, committish, engine),
            Err(error) if error.code() == ErrorCode::NotFound => {
                Ok(Self::without_repository(requested_path, engine))
            }
            Err(error) => Err(error.into()),
        }
    }

    /// Creates a context using the effective engine selected by environment policy.
    pub fn create_with_environment(
        path: impl AsRef<Path>,
        committish: Option<&str>,
        default_engine: GitEngine,
    ) -> Result<Self> {
        Self::create(path, committish, effective_git_engine(default_engine))
    }

    /// Gets the kind of context that was created.
    pub fn kind(&self) -> GitContextKind {
        self.kind
    }

    /// Gets the requested Git engine.
    pub fn engine(&self) -> GitEngine {
        self.engine
    }

    /// Gets whether an actual repository backs this context.
    pub fn is_repository(&self) -> bool {
        self.git_directory.is_some()
    }

    /// Gets the absolute root of the working tree.
    pub fn working_tree_path(&self) -> &Path {
        &self.working_tree_path
    }

    /// Gets the repository metadata directory. In a linked worktree this is the
    /// worktree-specific Git directory, not necessarily `<working tree>/.git`.
    pub fn git_directory(&self) -> Option<&Path> {
        self.git_directory.as_deref()
    }

    /// Gets the directory selected for version calculation, relative to the working tree.
    pub fn repo_relative_project_directory(&self) -> &Path {
        &self.repo_relative_project_directory
    }

    /// Selects the directory used by higher version-file layers.
    pub fn set_repo_relative_project_directory(
        &mut self,
        directory: impl AsRef<Path>,
    ) -> Result<()> {
        let directory = directory.as_ref();
        if directory.is_absolute() {
            return Err(Error::InvalidFormat(format!(
                "Path '{}' must be relative to the working tree.",
                directory.display()
            )));
        }

        self.repo_relative_project_directory = directory.to_path_buf();
        Ok(())
    }

    /// Gets the absolute directory selected for version calculation.
    pub fn absolute_project_directory(&self) -> PathBuf {
        self.working_tree_path
            .join(&self.repo_relative_project_directory)
    }

    /// Exposes the repository to the future version-file and history layers.
    pub fn repository(&self) -> Option<&Repository> {
        self.repository.as_ref()
    }

    /// Gets the selected commit ID.
    pub fn git_commit_id(&self) -> Option<Oid> {
        self.selected_commit
    }

    /// Gets whether the selected commit is the commit at `HEAD`.
    pub fn is_head(&self) -> Result<bool> {
        let Some(selected) = self.selected_commit else {
            return Ok(false);
        };
        let Some(repository) = &self.repository else {
            return Ok(false);
        };

        match repository.head().and_then(|head| head.peel_to_commit()) {
            Ok(head) => Ok(head.id() == selected),
            Err(error) if matches!(error.code(), ErrorCode::UnbornBranch | ErrorCode::NotFound) => {
                Ok(false)
            }
            Err(error) => Err(error.into()),
        }
    }

    /// Gets the selected commit's author and committer timestamps.
    pub fn git_commit_dates(&self) -> Result<Option<CommitDates>> {
        let Some(commit) = self.selected_commit()? else {
            return Ok(None);
        };

        Ok(Some(CommitDates {
            author: convert_time(commit.author().when())?,
            committer: convert_time(commit.committer().when())?,
        }))
    }

    /// Gets the canonical name of `HEAD`, such as `refs/heads/main`.
    ///
    /// A detached `HEAD` is reported as `HEAD`; an unborn repository has no name.
    pub fn head_canonical_name(&self) -> Result<Option<String>> {
        let Some(repository) = &self.repository else {
            return Ok(None);
        };

        match repository.head() {
            Ok(head) => Ok(Some(head.name()?.to_owned())),
            Err(error) if matches!(error.code(), ErrorCode::UnbornBranch | ErrorCode::NotFound) => {
                Ok(None)
            }
            Err(error) => Err(error.into()),
        }
    }

    /// Gets canonical tag names that resolve to the selected commit.
    ///
    /// Annotated tags are peeled before comparison.
    pub fn head_tags(&self) -> Result<Option<Vec<String>>> {
        let Some(selected) = self.selected_commit else {
            return Ok(None);
        };
        let Some(repository) = &self.repository else {
            return Ok(None);
        };
        let mut tags = Vec::new();
        for reference in repository.references_glob("refs/tags/*")? {
            let reference = reference?;
            if reference.peel_to_commit().map(|commit| commit.id()).ok() == Some(selected)
                && let Ok(name) = reference.name()
            {
                tags.push(name.to_owned());
            }
        }
        tags.sort();
        Ok(Some(tags))
    }

    /// Gets whether the repository is shallow.
    ///
    /// libgit2 checks the common repository directory, which also handles linked
    /// worktrees correctly.
    pub fn is_shallow(&self) -> bool {
        self.repository.as_ref().is_some_and(Repository::is_shallow)
    }

    /// Gets whether tracked, untracked, or staged working-tree changes exist.
    pub fn is_working_tree_dirty(&self) -> Result<bool> {
        let Some(repository) = &self.repository else {
            return Ok(false);
        };
        Ok(repository.statuses(None)?.iter().any(|entry| {
            let status = entry.status();
            status != Status::CURRENT && !status.contains(Status::IGNORED)
        }))
    }

    /// Determines whether Git would ignore `path`.
    pub fn is_ignored(&self, path: impl AsRef<Path>) -> Result<bool> {
        let Some(repository) = &self.repository else {
            return Ok(false);
        };
        let relative = self.repo_relative_path(path.as_ref())?;
        Ok(repository.status_should_ignore(&relative)?)
    }

    /// Attempts to select a commit using any revparse-compatible committish.
    ///
    /// Tags (including annotated tags), local and remote branches, abbreviated IDs,
    /// and first-parent expressions such as `HEAD~2` are accepted.
    pub fn try_select_commit(&mut self, committish: &str) -> Result<bool> {
        match self.kind {
            GitContextKind::Disabled => return Ok(true),
            GitContextKind::NoRepository => return Err(no_repository_error()),
            GitContextKind::Repository => {}
        }

        let repository = self.repository.as_ref().expect("repository context");
        match resolve_commit(repository, committish) {
            Ok(commit) => {
                self.selected_commit = Some(commit);
                Ok(true)
            }
            Err(error)
                if matches!(
                    error.code(),
                    ErrorCode::NotFound
                        | ErrorCode::Ambiguous
                        | ErrorCode::InvalidSpec
                        | ErrorCode::Peel
                ) =>
            {
                Ok(false)
            }
            Err(error) => Err(error.into()),
        }
    }

    /// Creates a lightweight tag at the selected commit.
    pub fn apply_tag(&self, name: &str) -> Result<Oid> {
        self.ensure_writable()?;
        let repository = self.repository.as_ref().expect("repository context");
        let commit = self
            .selected_commit()?
            .ok_or_else(|| Error::InvalidOperation("No commit is selected.".to_owned()))?;
        Ok(repository.tag_lightweight(name, commit.as_object(), false)?)
    }

    /// Adds a path to the index and writes the updated index.
    pub fn stage(&self, path: impl AsRef<Path>) -> Result<()> {
        self.ensure_writable()?;
        let repository = self.repository.as_ref().expect("repository context");
        let relative = self.repo_relative_path(path.as_ref())?;
        let mut index = repository.index()?;
        index.add_path(&relative)?;
        index.write()?;
        Ok(())
    }

    /// Gets the shortest object ID of at least `min_length` characters that uniquely
    /// identifies the selected commit in this object database.
    pub fn short_unique_commit_id(&self, min_length: usize) -> Result<String> {
        match self.kind {
            GitContextKind::Disabled => return Ok(DISABLED_COMMIT_ID.to_owned()),
            GitContextKind::NoRepository => return Err(no_repository_error()),
            GitContextKind::Repository => {}
        }
        let selected = self
            .selected_commit
            .ok_or_else(|| Error::InvalidOperation("No commit is selected.".to_owned()))?;
        self.short_unique_id(selected, min_length)
    }

    /// Gets a shortest unique ID for an arbitrary commit.
    pub fn short_unique_id(&self, commit_id: Oid, min_length: usize) -> Result<String> {
        match self.kind {
            GitContextKind::Disabled => return Ok(DISABLED_COMMIT_ID.to_owned()),
            GitContextKind::NoRepository => return Err(no_repository_error()),
            GitContextKind::Repository => {}
        }
        if !(1..=40).contains(&min_length) {
            return Err(Error::InvalidFormat(
                "A short commit ID length must be between 1 and 40.".to_owned(),
            ));
        }

        let repository = self.repository.as_ref().expect("repository context");
        repository.find_commit(commit_id)?;
        let full_id = commit_id.to_string();
        let mut length = min_length;
        let odb = repository.odb()?;
        while length < full_id.len() {
            let prefix = &full_id[..length];
            let mut collision = false;
            odb.foreach(|candidate| {
                if *candidate != commit_id && candidate.to_string().starts_with(prefix) {
                    collision = true;
                }
                true
            })?;
            if !collision {
                break;
            }
            length += 1;
        }
        Ok(full_id[..length].to_owned())
    }

    /// Detects the repository's default branch using remote HEADs, local configuration,
    /// and conventional names, in that order.
    pub fn default_branch(&self) -> Result<String> {
        let Some(repository) = &self.repository else {
            return Ok("master".to_owned());
        };

        let remotes = repository.remotes()?;
        let mut remote_names = Vec::new();
        for remote in remotes.iter() {
            if let Some(name) = remote? {
                remote_names.push(name.to_owned());
            }
        }
        for preferred in ["upstream", "origin"] {
            if remote_names.iter().any(|name| name == preferred)
                && let Some(branch) = remote_default_branch(repository, preferred)?
            {
                return Ok(branch);
            }
        }
        for remote in &remote_names {
            if remote != "upstream"
                && remote != "origin"
                && let Some(branch) = remote_default_branch(repository, remote)?
            {
                return Ok(branch);
            }
        }

        let mut local_branches = Vec::new();
        for branch in repository.branches(Some(BranchType::Local))? {
            let (branch, _) = branch?;
            if let Some(name) = branch.name()? {
                local_branches.push(name.to_owned());
            }
        }
        if local_branches.len() == 1 {
            return Ok(local_branches.remove(0));
        }
        if let Ok(configured) = repository.config()?.get_string("init.defaultBranch")
            && local_branches.iter().any(|branch| branch == &configured)
        {
            return Ok(configured);
        }
        for conventional in ["master", "main", "develop"] {
            if local_branches.iter().any(|branch| branch == conventional) {
                return Ok(conventional.to_owned());
            }
        }
        Ok("master".to_owned())
    }

    fn from_repository(
        repository: Repository,
        requested_path: PathBuf,
        committish: Option<&str>,
        engine: GitEngine,
    ) -> Result<Self> {
        let working_tree_path = repository.workdir().ok_or_else(|| {
            Error::InvalidOperation("Bare repositories are not supported.".to_owned())
        })?;
        let working_tree_path = trim_trailing_separator(working_tree_path);
        let git_directory = trim_trailing_separator(repository.path());
        let relative = requested_path
            .strip_prefix(&working_tree_path)
            .unwrap_or(Path::new(""))
            .to_path_buf();

        if engine == GitEngine::Disabled {
            return Ok(Self {
                repository: None,
                kind: GitContextKind::Disabled,
                engine,
                working_tree_path,
                git_directory: Some(git_directory),
                repo_relative_project_directory: relative,
                selected_commit: None,
            });
        }

        let selected_commit = match committish {
            Some(spec) => Some(resolve_commit(&repository, spec).map_err(|_| {
                Error::InvalidFormat(format!("No matching commit found for '{spec}'."))
            })?),
            None => match repository.head().and_then(|head| head.peel_to_commit()) {
                Ok(commit) => Some(commit.id()),
                Err(error)
                    if matches!(error.code(), ErrorCode::UnbornBranch | ErrorCode::NotFound) =>
                {
                    None
                }
                Err(error) => return Err(error.into()),
            },
        };

        Ok(Self {
            repository: Some(repository),
            kind: GitContextKind::Repository,
            engine,
            working_tree_path,
            git_directory: Some(git_directory),
            repo_relative_project_directory: relative,
            selected_commit,
        })
    }

    fn without_repository(path: PathBuf, engine: GitEngine) -> Self {
        let root = path
            .ancestors()
            .last()
            .map(Path::to_path_buf)
            .unwrap_or_else(|| path.clone());
        let relative = path
            .strip_prefix(&root)
            .unwrap_or(Path::new(""))
            .to_path_buf();
        Self {
            repository: None,
            kind: GitContextKind::NoRepository,
            engine,
            working_tree_path: root,
            git_directory: None,
            repo_relative_project_directory: relative,
            selected_commit: None,
        }
    }

    fn selected_commit(&self) -> Result<Option<Commit<'_>>> {
        match (&self.repository, self.selected_commit) {
            (Some(repository), Some(commit)) => Ok(Some(repository.find_commit(commit)?)),
            _ => Ok(None),
        }
    }

    fn repo_relative_path(&self, path: &Path) -> Result<PathBuf> {
        if path.is_absolute() {
            path.strip_prefix(&self.working_tree_path)
                .map(Path::to_path_buf)
                .map_err(|_| {
                    Error::InvalidFormat(format!(
                        "Path '{}' is not within repository '{}'.",
                        path.display(),
                        self.working_tree_path.display()
                    ))
                })
        } else {
            Ok(path.to_path_buf())
        }
    }

    fn ensure_writable(&self) -> Result<()> {
        match self.kind {
            GitContextKind::Disabled => Err(Error::InvalidOperation(
                "Git operations are disabled.".to_owned(),
            )),
            GitContextKind::NoRepository => Err(no_repository_error()),
            GitContextKind::Repository if self.engine != GitEngine::ReadWrite => Err(
                Error::InvalidOperation("The Git context is read-only.".to_owned()),
            ),
            GitContextKind::Repository => Ok(()),
        }
    }
}

/// Gets the effective Git engine after applying `NBGV_GitEngine`, Dependabot, and
/// GitHub Copilot policy.
///
/// `NBGV_GitEngine` has precedence and accepts the case-sensitive compatibility
/// values `LibGit2`, `Managed`, and `Disabled`. Unknown values are ignored.
/// Otherwise `DEPENDABOT=true` (case-insensitive) or the exact GitHub actor
/// `copilot-swe-agent[bot]` disables Git.
pub fn effective_git_engine(default_engine: GitEngine) -> GitEngine {
    effective_git_engine_from(
        default_engine,
        env::var("NBGV_GitEngine").ok().as_deref(),
        env::var("DEPENDABOT").ok().as_deref(),
        env::var("GITHUB_ACTOR").ok().as_deref(),
    )
}

fn effective_git_engine_from(
    default_engine: GitEngine,
    configured: Option<&str>,
    dependabot: Option<&str>,
    github_actor: Option<&str>,
) -> GitEngine {
    match configured {
        Some("LibGit2") => return GitEngine::ReadWrite,
        Some("Managed") => return GitEngine::ReadOnly,
        Some("Disabled") => return GitEngine::Disabled,
        _ => {}
    }
    if dependabot.is_some_and(|value| value.eq_ignore_ascii_case("true"))
        || github_actor == Some("copilot-swe-agent[bot]")
    {
        GitEngine::Disabled
    } else {
        default_engine
    }
}

fn resolve_commit(
    repository: &Repository,
    committish: &str,
) -> std::result::Result<Oid, git2::Error> {
    if let Ok(oid) = Oid::from_str(committish)
        && let Ok(commit) = repository.find_commit(oid)
    {
        return Ok(commit.id());
    }
    repository
        .revparse_single(committish)?
        .peel_to_commit()
        .map(|commit| commit.id())
}

fn remote_default_branch(repository: &Repository, remote: &str) -> Result<Option<String>> {
    let reference_name = format!("refs/remotes/{remote}/HEAD");
    let prefix = format!("refs/remotes/{remote}/");
    match repository.find_reference(&reference_name) {
        Ok(reference) => Ok(reference
            .symbolic_target()?
            .and_then(|target| target.strip_prefix(&prefix))
            .map(str::to_owned)),
        Err(error) if error.code() == ErrorCode::NotFound => Ok(None),
        Err(error) => Err(error.into()),
    }
}

fn convert_time(time: git2::Time) -> Result<DateTime<FixedOffset>> {
    let offset = FixedOffset::east_opt(time.offset_minutes() * 60).ok_or_else(|| {
        Error::InvalidFormat(format!(
            "Commit has an invalid timezone offset: {} minutes.",
            time.offset_minutes()
        ))
    })?;
    offset
        .timestamp_opt(time.seconds(), 0)
        .single()
        .ok_or_else(|| Error::InvalidFormat("Commit has an invalid timestamp.".to_owned()))
}

fn absolute_path(path: &Path) -> std::io::Result<PathBuf> {
    if path.is_absolute() {
        Ok(path.to_path_buf())
    } else {
        Ok(env::current_dir()?.join(path))
    }
}

fn trim_trailing_separator(path: &Path) -> PathBuf {
    path.components().collect()
}

fn no_repository_error() -> Error {
    Error::InvalidOperation(NO_REPOSITORY_MESSAGE.to_owned())
}

#[cfg(test)]
mod tests {
    use std::fs;
    use std::sync::atomic::{AtomicU64, Ordering};

    use git2::{ObjectType, Signature};

    use super::*;

    static NEXT_DIRECTORY: AtomicU64 = AtomicU64::new(0);

    struct TestRepository {
        path: PathBuf,
        head: Oid,
    }

    impl TestRepository {
        fn create() -> Self {
            let path = env::temp_dir().join(format!(
                "nbgv-rust-git-context-{}-{}",
                std::process::id(),
                NEXT_DIRECTORY.fetch_add(1, Ordering::Relaxed)
            ));
            let repository = Repository::init(&path).unwrap();
            let signature =
                Signature::new("Test", "test@example.com", &git2::Time::new(10, 60)).unwrap();
            fs::write(path.join("tracked.txt"), "one").unwrap();
            let mut index = repository.index().unwrap();
            index.add_path(Path::new("tracked.txt")).unwrap();
            index.write().unwrap();
            let tree_id = index.write_tree().unwrap();
            let tree = repository.find_tree(tree_id).unwrap();
            let head = repository
                .commit(Some("HEAD"), &signature, &signature, "initial", &tree, &[])
                .unwrap();
            drop(tree);
            drop(index);
            drop(repository);
            Self { path, head }
        }
    }

    impl Drop for TestRepository {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.path);
        }
    }

    #[test]
    fn discovers_from_subdirectory_and_reads_metadata() {
        let test = TestRepository::create();
        let subdirectory = test.path.join("a").join("b");
        fs::create_dir_all(&subdirectory).unwrap();

        let context = GitContext::create(&subdirectory, None, GitEngine::ReadOnly).unwrap();

        assert_eq!(context.kind(), GitContextKind::Repository);
        assert_eq!(context.working_tree_path(), test.path);
        assert_eq!(context.repo_relative_project_directory(), Path::new("a/b"));
        assert_eq!(context.git_commit_id(), Some(test.head));
        assert!(context.is_head().unwrap());
        assert_eq!(
            context.git_commit_dates().unwrap().unwrap().author.offset(),
            &FixedOffset::east_opt(3600).unwrap()
        );
        assert!(!context.is_shallow());
    }

    #[test]
    fn discovers_linked_worktree_git_file() {
        let test = TestRepository::create();
        let linked_path = test.path.with_extension("linked");
        let repository = Repository::open(&test.path).unwrap();
        let worktree = repository.worktree("linked", &linked_path, None).unwrap();
        drop(worktree);
        drop(repository);

        let context = GitContext::create(&linked_path, None, GitEngine::ReadOnly).unwrap();

        assert_eq!(context.working_tree_path(), linked_path);
        assert!(linked_path.join(".git").is_file());
        assert!(context.git_directory().unwrap().is_dir());
        assert_eq!(context.git_commit_id(), Some(test.head));
        fs::remove_dir_all(linked_path).unwrap();
    }

    #[test]
    fn detects_shallow_repository() {
        let test = TestRepository::create();
        fs::write(
            test.path.join(".git").join("shallow"),
            format!("{}\n", test.head),
        )
        .unwrap();

        let context = GitContext::create(&test.path, None, GitEngine::ReadOnly).unwrap();

        assert!(context.is_shallow());
    }

    #[test]
    fn selects_committishes_and_reports_tags() {
        let test = TestRepository::create();
        let repository = Repository::open(&test.path).unwrap();
        let head = repository.find_commit(test.head).unwrap();
        repository
            .tag_lightweight("v1", head.as_object(), false)
            .unwrap();
        drop(head);
        drop(repository);
        let mut context = GitContext::create(&test.path, None, GitEngine::ReadOnly).unwrap();

        assert!(context.try_select_commit("v1").unwrap());
        assert!(
            context
                .try_select_commit(&test.head.to_string()[..12])
                .unwrap()
        );
        assert!(!context.try_select_commit("HEAD~999").unwrap());
        assert_eq!(context.head_tags().unwrap().unwrap(), vec!["refs/tags/v1"]);
        assert_eq!(
            context.short_unique_commit_id(8).unwrap(),
            test.head.to_string()[..8]
        );
    }

    #[test]
    fn selects_local_remote_and_annotated_tag_refs() {
        let test = TestRepository::create();
        let repository = Repository::open(&test.path).unwrap();
        let head = repository.find_commit(test.head).unwrap();
        repository.branch("feature", &head, false).unwrap();
        repository
            .reference(
                "refs/remotes/origin/feature",
                test.head,
                true,
                "compatibility fixture",
            )
            .unwrap();
        let signature =
            Signature::new("Test", "test@example.com", &git2::Time::new(20, -60)).unwrap();
        repository
            .tag("annotated", head.as_object(), &signature, "release", false)
            .unwrap();
        drop(head);
        drop(repository);

        let mut context = GitContext::create(&test.path, None, GitEngine::ReadOnly).unwrap();
        for committish in ["feature", "origin/feature", "annotated"] {
            assert!(
                context.try_select_commit(committish).unwrap(),
                "{committish}"
            );
            assert_eq!(Some(test.head), context.git_commit_id());
        }
        assert_eq!(
            context.head_tags().unwrap().unwrap(),
            vec!["refs/tags/annotated"]
        );
    }

    #[test]
    fn lengthens_an_ambiguous_short_id() {
        let test = TestRepository::create();
        let repository = Repository::open(&test.path).unwrap();
        let odb = repository.odb().unwrap();
        let prefix = &test.head.to_string()[..1];
        for value in 0..1000 {
            let candidate = odb
                .write(ObjectType::Blob, format!("collision-{value}").as_bytes())
                .unwrap();
            if candidate != test.head && candidate.to_string().starts_with(prefix) {
                break;
            }
        }
        drop(odb);
        drop(repository);
        let context = GitContext::create(&test.path, None, GitEngine::ReadOnly).unwrap();

        assert!(context.short_unique_commit_id(1).unwrap().len() > 1);
    }

    #[test]
    fn supports_ignore_status_stage_and_tag_mutations() {
        let test = TestRepository::create();
        fs::write(test.path.join(".gitignore"), "*.tmp\n").unwrap();
        fs::write(test.path.join("ignored.tmp"), "ignored").unwrap();
        fs::write(test.path.join("new.txt"), "new").unwrap();
        let context = GitContext::create(&test.path, None, GitEngine::ReadWrite).unwrap();

        assert!(context.is_ignored(test.path.join("ignored.tmp")).unwrap());
        assert!(context.is_working_tree_dirty().unwrap());
        context.stage("new.txt").unwrap();
        assert!(
            context
                .repository()
                .unwrap()
                .index()
                .unwrap()
                .get_path(Path::new("new.txt"), 0)
                .is_some()
        );
        context.apply_tag("created").unwrap();
        assert!(
            context
                .repository()
                .unwrap()
                .find_reference("refs/tags/created")
                .is_ok()
        );
    }

    #[test]
    fn detects_default_branch_in_compatibility_order() {
        let test = TestRepository::create();
        let repository = Repository::open(&test.path).unwrap();
        repository
            .remote("origin", "https://example.com/repo")
            .unwrap();
        repository
            .reference("refs/remotes/origin/main", test.head, true, "test")
            .unwrap();
        repository
            .reference_symbolic(
                "refs/remotes/origin/HEAD",
                "refs/remotes/origin/main",
                true,
                "test",
            )
            .unwrap();
        drop(repository);

        let context = GitContext::create(&test.path, None, GitEngine::ReadOnly).unwrap();
        assert_eq!(context.default_branch().unwrap(), "main");
    }

    #[test]
    fn models_disabled_and_no_repository_contexts() {
        let test = TestRepository::create();
        let mut disabled =
            GitContext::create(&test.path, Some("does-not-matter"), GitEngine::Disabled).unwrap();
        assert_eq!(disabled.kind(), GitContextKind::Disabled);
        assert!(disabled.is_repository());
        assert!(disabled.try_select_commit("anything").unwrap());
        assert_eq!(
            disabled.short_unique_commit_id(7).unwrap(),
            DISABLED_COMMIT_ID
        );

        let outside = test.path.parent().unwrap().join(format!(
            "nbgv-no-repo-{}",
            NEXT_DIRECTORY.fetch_add(1, Ordering::Relaxed)
        ));
        fs::create_dir_all(&outside).unwrap();
        let no_repository = GitContext::create(&outside, None, GitEngine::ReadOnly).unwrap();
        assert_eq!(no_repository.kind(), GitContextKind::NoRepository);
        assert!(!no_repository.is_repository());
        fs::remove_dir_all(outside).unwrap();
    }

    #[test]
    fn applies_effective_engine_policy() {
        assert_eq!(
            effective_git_engine_from(GitEngine::ReadOnly, None, Some("TRUE"), None),
            GitEngine::Disabled
        );
        assert_eq!(
            effective_git_engine_from(
                GitEngine::ReadWrite,
                None,
                None,
                Some("copilot-swe-agent[bot]")
            ),
            GitEngine::Disabled
        );
        assert_eq!(
            effective_git_engine_from(
                GitEngine::ReadOnly,
                Some("LibGit2"),
                Some("true"),
                Some("copilot-swe-agent[bot]")
            ),
            GitEngine::ReadWrite
        );
        assert_eq!(
            effective_git_engine_from(
                GitEngine::ReadWrite,
                Some("invalid"),
                Some("false"),
                Some("COPILOT-SWE-AGENT[BOT]")
            ),
            GitEngine::ReadWrite
        );
    }
}
