// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! Discovery, inheritance, and mutation of `version.json` and `version.txt` files.

use std::fs;
use std::path::{Path, PathBuf};
use std::str;

use bitflags::bitflags;
use git2::{ObjectType, Repository, Tree};

use crate::{Error, GitContext, GitContextKind, Result, VersionOptions};

/// The legacy version filename.
pub const VERSION_TXT_FILE_NAME: &str = "version.txt";

/// The JSON version filename.
pub const VERSION_JSON_FILE_NAME: &str = "version.json";

/// The JSON schema shipped with this crate.
pub const VERSION_SCHEMA: &str = include_str!("version.schema.json");

bitflags! {
    /// Controls which version file is returned and whether inheritance is merged.
    #[derive(Clone, Copy, Debug, Default, Eq, PartialEq)]
    pub struct VersionFileRequirements: u8 {
        /// Return one file rather than a merged inheritance result.
        const NON_MERGED_RESULT = 0x1;
        /// Require the returned file to specify `version`.
        const VERSION_SPECIFIED = 0x2;
        /// Permit returning an inheriting file.
        const ACCEPT_INHERITING_FILE = 0x4;
    }
}

/// Absolute locations discovered while resolving version files.
#[derive(Clone, Debug, Default, Eq, PartialEq)]
pub struct VersionFileLocations {
    /// The first directory containing a non-inheriting version file.
    pub non_inheriting_version_directory: Option<PathBuf>,
    /// The first directory containing a file that specifies `version`.
    pub version_specifying_version_directory: Option<PathBuf>,
}

/// Reads and writes version files for a Git context.
pub struct VersionFile<'context> {
    context: &'context GitContext,
}

impl<'context> VersionFile<'context> {
    /// Creates a version-file accessor.
    #[must_use]
    pub const fn new(context: &'context GitContext) -> Self {
        Self { context }
    }

    /// Returns whether a version file is defined at the context's selected commit.
    pub fn is_version_defined(&self) -> Result<bool> {
        Ok(self
            .get_version(VersionFileRequirements::default())?
            .0
            .is_some())
    }

    /// Reads from the selected Git commit, or from disk when no commit is selected.
    pub fn get_version(
        &self,
        requirements: VersionFileRequirements,
    ) -> Result<(Option<VersionOptions>, VersionFileLocations)> {
        self.validate_requirements(requirements)?;
        match self.context.kind() {
            GitContextKind::Disabled => Ok((None, VersionFileLocations::default())),
            GitContextKind::Repository if self.context.git_commit_id().is_some() => {
                self.get_committed_version(requirements)
            }
            GitContextKind::Repository | GitContextKind::NoRepository => {
                self.get_working_copy_version(requirements)
            }
        }
    }

    /// Reads version files from the working copy, including uncommitted changes.
    pub fn get_working_copy_version(
        &self,
        requirements: VersionFileRequirements,
    ) -> Result<(Option<VersionOptions>, VersionFileLocations)> {
        self.validate_requirements(requirements)?;
        let root = self.context.working_tree_path();
        let start = self.context.absolute_project_directory();
        let relative = start.strip_prefix(root).map_err(|_| {
            Error::InvalidFormat(format!(
                "Project directory '{}' is not within '{}'.",
                start.display(),
                root.display()
            ))
        })?;
        let start = path_to_git(relative);
        self.read_search_chain(
            &start,
            requirements,
            |directory, filename| {
                let path = root.join(git_path_to_native(directory)).join(filename);
                match fs::read(path) {
                    Ok(content) => Ok(Some(content)),
                    Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
                    Err(error) => Err(error.into()),
                }
            },
            |directory| root.join(git_path_to_native(directory)),
        )
    }

    /// Writes `version.json`, or preserves an existing `version.txt` when it can represent
    /// all the supplied options.
    pub fn set_version(
        &self,
        project_directory: impl AsRef<Path>,
        version: &VersionOptions,
        include_schema_property: bool,
    ) -> Result<PathBuf> {
        let project_directory = project_directory.as_ref();
        if version.version.is_none() && !version.inherit {
            return Err(Error::InvalidFormat(
                "'version' must be set for a non-inheriting version file.".to_owned(),
            ));
        }
        fs::create_dir_all(project_directory)?;

        let version_txt_path = project_directory.join(VERSION_TXT_FILE_NAME);
        if version_txt_path.exists() {
            if is_version_only(version) {
                let semantic_version = version.version.as_ref().expect("version-only options");
                let mut content = semantic_version.version.to_string();
                content.push('\n');
                content.push_str(&semantic_version.prerelease);
                fs::write(&version_txt_path, content)?;
                return Ok(version_txt_path);
            }
            fs::remove_file(&version_txt_path)?;
        }

        let relative = project_directory
            .strip_prefix(self.context.working_tree_path())
            .map(path_to_git)
            .map_err(|_| {
                Error::InvalidFormat(format!(
                    "Project directory '{}' is not within '{}'.",
                    project_directory.display(),
                    self.context.working_tree_path().display()
                ))
            })?;
        let mut serialized = version.clone();
        serialized.schema =
            include_schema_property.then(|| VersionOptions::schema_url().to_owned());
        let json = serialized.to_json(&relative)?;
        let version_json_path = project_directory.join(VERSION_JSON_FILE_NAME);
        fs::write(&version_json_path, json)?;
        Ok(version_json_path)
    }

    fn get_committed_version(
        &self,
        requirements: VersionFileRequirements,
    ) -> Result<(Option<VersionOptions>, VersionFileLocations)> {
        let repository = self.context.repository().ok_or_else(|| {
            Error::InvalidOperation("No repository is available for commit lookup.".to_owned())
        })?;
        let commit_id = self.context.git_commit_id().ok_or_else(|| {
            Error::InvalidOperation("No commit is selected for commit lookup.".to_owned())
        })?;
        let commit = repository.find_commit(commit_id)?;
        let tree = commit.tree()?;
        let ignore_case = repository
            .config()?
            .get_bool("core.ignorecase")
            .unwrap_or(false);
        let start = path_to_git(self.context.repo_relative_project_directory());
        let root = self.context.working_tree_path();
        self.read_search_chain(
            &start,
            requirements,
            |directory, filename| {
                let path = join_git_path(directory, filename);
                read_blob(repository, &tree, &path, ignore_case)
            },
            |directory| root.join(git_path_to_native(directory)),
        )
    }

    fn read_search_chain<ReadFile, Location>(
        &self,
        starting_directory: &str,
        requirements: VersionFileRequirements,
        mut read_file: ReadFile,
        location: Location,
    ) -> Result<(Option<VersionOptions>, VersionFileLocations)>
    where
        ReadFile: FnMut(&str, &str) -> Result<Option<Vec<u8>>>,
        Location: Fn(&str) -> PathBuf,
    {
        let mut directory = normalize_git_directory(starting_directory);
        let mut locations = VersionFileLocations::default();
        let mut overlays: Vec<(VersionOptions, String)> = Vec::new();

        loop {
            if let Some(content) = read_file(&directory, VERSION_TXT_FILE_NAME)? {
                let mut options = read_version_txt(&content).map_err(|error| {
                    file_error(&join_git_path(&directory, VERSION_TXT_FILE_NAME), error)
                })?;
                apply_locations(&options, location(&directory), &mut locations);
                if !requirements.contains(VersionFileRequirements::NON_MERGED_RESULT) {
                    for (overlay, _) in overlays.iter().rev() {
                        options.merge_inheriting(overlay)?;
                    }
                }
                return Ok((
                    satisfies_requirements(&options, requirements).then_some(options),
                    locations,
                ));
            }

            if let Some(content) = read_file(&directory, VERSION_JSON_FILE_NAME)? {
                let json = str::from_utf8(&content).map_err(|error| {
                    Error::InvalidFormat(format!(
                        "Failure while reading '{}': {error}",
                        join_git_path(&directory, VERSION_JSON_FILE_NAME)
                    ))
                })?;
                let options = VersionOptions::from_json(json, &directory).map_err(|error| {
                    file_error(&join_git_path(&directory, VERSION_JSON_FILE_NAME), error)
                })?;
                apply_locations(&options, location(&directory), &mut locations);

                if satisfies_requirements(&options, requirements)
                    && (overlays.is_empty()
                        || options.inherit
                        || requirements.contains(VersionFileRequirements::NON_MERGED_RESULT))
                {
                    return Ok((Some(options), locations));
                }
                if !options.inherit {
                    if overlays.is_empty()
                        || requirements.contains(VersionFileRequirements::NON_MERGED_RESULT)
                    {
                        return Ok((
                            satisfies_requirements(&options, requirements).then_some(options),
                            locations,
                        ));
                    }
                    let mut merged = options;
                    for (overlay, _) in overlays.iter().rev() {
                        merged.merge_inheriting(overlay)?;
                    }
                    return Ok((
                        satisfies_requirements(&merged, requirements).then_some(merged),
                        locations,
                    ));
                }
                overlays.push((options, directory.clone()));
            }

            let Some(parent) = parent_git_directory(&directory) else {
                if let Some((_, inheriting_directory)) = overlays.first() {
                    return Err(Error::InvalidOperation(format!(
                        "'{}' inherits from an ancestor version.json file, but none exists.",
                        join_git_path(inheriting_directory, VERSION_JSON_FILE_NAME)
                    )));
                }
                return Ok((None, locations));
            };
            directory = parent;
        }
    }

    fn validate_requirements(&self, requirements: VersionFileRequirements) -> Result<()> {
        if requirements.contains(VersionFileRequirements::ACCEPT_INHERITING_FILE)
            && !requirements.contains(VersionFileRequirements::NON_MERGED_RESULT)
        {
            return Err(Error::InvalidFormat(
                "ACCEPT_INHERITING_FILE requires NON_MERGED_RESULT.".to_owned(),
            ));
        }
        Ok(())
    }
}

fn satisfies_requirements(options: &VersionOptions, requirements: VersionFileRequirements) -> bool {
    (!requirements.contains(VersionFileRequirements::VERSION_SPECIFIED)
        || options.version.is_some())
        && (!options.inherit
            || requirements.contains(VersionFileRequirements::ACCEPT_INHERITING_FILE))
}

fn apply_locations(
    options: &VersionOptions,
    directory: PathBuf,
    locations: &mut VersionFileLocations,
) {
    if options.version.is_some() && locations.version_specifying_version_directory.is_none() {
        locations.version_specifying_version_directory = Some(directory.clone());
    }
    if !options.inherit && locations.non_inheriting_version_directory.is_none() {
        locations.non_inheriting_version_directory = Some(directory);
    }
}

fn read_version_txt(content: &[u8]) -> Result<VersionOptions> {
    let content = str::from_utf8(content)
        .map_err(|error| Error::InvalidFormat(format!("version.txt is not UTF-8: {error}")))?;
    let mut lines = content.lines();
    let version = lines.next().unwrap_or_default().trim_end_matches('\r');
    let prerelease = lines
        .next()
        .unwrap_or_default()
        .trim_end_matches('\r')
        .trim();
    let combined = if prerelease.is_empty() || prerelease.starts_with('-') {
        format!("{version}{prerelease}")
    } else {
        format!("{version}-{prerelease}")
    };
    let version = combined
        .parse()
        .map_err(|_| Error::InvalidFormat(format!("Unrecognized version format '{combined}'.")))?;
    Ok(VersionOptions {
        version: Some(version),
        ..VersionOptions::default()
    })
}

fn is_version_only(options: &VersionOptions) -> bool {
    if options.version.is_none() {
        return false;
    }
    let mut value = match serde_json::to_value(options) {
        Ok(value) => value,
        Err(_) => return false,
    };
    let Some(object) = value.as_object_mut() else {
        return false;
    };
    object.remove("$schema");
    object.len() == 1 && object.contains_key("version")
}

fn read_blob(
    repository: &Repository,
    root: &Tree<'_>,
    path: &str,
    ignore_case: bool,
) -> Result<Option<Vec<u8>>> {
    let mut tree = repository.find_tree(root.id())?;
    let segments: Vec<_> = path
        .split('/')
        .filter(|segment| !segment.is_empty())
        .collect();
    for (index, segment) in segments.iter().enumerate() {
        let entry = {
            let entry = tree.get_name(segment).or_else(|| {
                ignore_case.then(|| {
                    tree.iter().find(|entry| {
                        entry
                            .name()
                            .is_ok_and(|name| name.eq_ignore_ascii_case(segment))
                    })
                })?
            });
            entry.map(|entry| (entry.id(), entry.kind()))
        };
        let Some((id, kind)) = entry else {
            return Ok(None);
        };
        if index + 1 == segments.len() {
            if kind != Some(ObjectType::Blob) {
                return Ok(None);
            }
            return Ok(Some(repository.find_blob(id)?.content().to_vec()));
        }
        if kind != Some(ObjectType::Tree) {
            return Ok(None);
        }
        tree = repository.find_tree(id)?;
    }
    Ok(None)
}

fn normalize_git_directory(path: &str) -> String {
    let mut path = path.replace('\\', "/").trim_matches('/').to_owned();
    while path.ends_with("/.") {
        path.truncate(path.len() - 2);
    }
    if path == "." {
        path.clear();
    }
    path
}

fn path_to_git(path: &Path) -> String {
    path.components()
        .filter_map(|component| {
            let value = component.as_os_str().to_string_lossy();
            (value != ".").then(|| value.into_owned())
        })
        .collect::<Vec<_>>()
        .join("/")
}

fn git_path_to_native(path: &str) -> PathBuf {
    path.split('/')
        .filter(|segment| !segment.is_empty())
        .collect()
}

fn join_git_path(directory: &str, filename: &str) -> String {
    if directory.is_empty() {
        filename.to_owned()
    } else {
        format!("{directory}/{filename}")
    }
}

fn parent_git_directory(directory: &str) -> Option<String> {
    if directory.is_empty() {
        None
    } else {
        Some(
            directory
                .rsplit_once('/')
                .map_or("", |(parent, _)| parent)
                .to_owned(),
        )
    }
}

fn file_error(path: &str, error: Error) -> Error {
    Error::InvalidFormat(format!("Failure while reading '{path}': {error}"))
}

#[cfg(test)]
mod tests {
    use std::sync::atomic::{AtomicU64, Ordering};

    use git2::{IndexAddOption, Signature};

    use super::*;
    use crate::{GitEngine, SemanticVersion, VersionPrecision};

    static NEXT_REPOSITORY: AtomicU64 = AtomicU64::new(0);

    struct TestRepository {
        path: PathBuf,
    }

    impl TestRepository {
        fn new() -> Self {
            let id = NEXT_REPOSITORY.fetch_add(1, Ordering::Relaxed);
            let path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
                .join("target")
                .join("test-repositories")
                .join(format!("version-file-{}-{id}", std::process::id()));
            let _ = fs::remove_dir_all(&path);
            fs::create_dir_all(&path).unwrap();
            Repository::init(&path).unwrap();
            Self { path }
        }

        fn commit(&self, message: &str) -> git2::Oid {
            let repository = Repository::open(&self.path).unwrap();
            let mut index = repository.index().unwrap();
            index.add_all(["*"], IndexAddOption::DEFAULT, None).unwrap();
            index.write().unwrap();
            let tree_id = index.write_tree().unwrap();
            let tree = repository.find_tree(tree_id).unwrap();
            let signature = Signature::now("Test", "test@example.com").unwrap();
            let parent = repository
                .head()
                .ok()
                .and_then(|head| head.target())
                .map(|id| repository.find_commit(id).unwrap());
            repository
                .commit(
                    Some("HEAD"),
                    &signature,
                    &signature,
                    message,
                    &tree,
                    &parent.iter().collect::<Vec<_>>(),
                )
                .unwrap()
        }
    }

    impl Drop for TestRepository {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.path);
        }
    }

    fn version(value: &str) -> VersionOptions {
        VersionOptions {
            version: Some(value.parse::<SemanticVersion>().unwrap()),
            ..VersionOptions::default()
        }
    }

    #[test]
    fn working_copy_prefers_version_txt() {
        let test = TestRepository::new();
        fs::write(
            test.path.join(VERSION_JSON_FILE_NAME),
            r#"{"version":"1.0"}"#,
        )
        .unwrap();
        fs::write(test.path.join(VERSION_TXT_FILE_NAME), "2.3\nbeta").unwrap();
        let context = GitContext::create(&test.path, None, GitEngine::ReadOnly).unwrap();
        let (actual, _) = VersionFile::new(&context)
            .get_working_copy_version(VersionFileRequirements::default())
            .unwrap();
        assert_eq!("2.3-beta", actual.unwrap().version.unwrap().to_string());
    }

    #[test]
    fn inheritance_merges_and_resolves_relative_filters() {
        let test = TestRepository::new();
        fs::write(
            test.path.join(VERSION_JSON_FILE_NAME),
            r#"{"version":"1.2","pathFilters":["./root.txt"],"versionHeightOffset":3}"#,
        )
        .unwrap();
        fs::create_dir(test.path.join("child")).unwrap();
        fs::write(
            test.path.join("child").join(VERSION_JSON_FILE_NAME),
            r#"{"inherit":true,"prerelease":"beta","pathFilters":["./child.txt"]}"#,
        )
        .unwrap();
        let context =
            GitContext::create(test.path.join("child"), None, GitEngine::ReadOnly).unwrap();
        let (actual, locations) = VersionFile::new(&context)
            .get_working_copy_version(VersionFileRequirements::default())
            .unwrap();
        let actual = actual.unwrap();
        assert_eq!("1.2-beta", actual.version.unwrap().to_string());
        assert_eq!(Some(3), actual.version_height_offset);
        assert_eq!(
            "child/child.txt",
            actual.path_filters.unwrap()[0].repo_relative_path()
        );
        assert_eq!(
            Some(test.path.clone()),
            locations.non_inheriting_version_directory
        );
    }

    #[test]
    fn multilevel_inheritance_matches_managed_merge_semantics() {
        let test = TestRepository::new();
        fs::write(
            test.path.join(VERSION_JSON_FILE_NAME),
            r#"{"version":"14.2","assemblyVersion":{"precision":"major"}}"#,
        )
        .unwrap();
        fs::create_dir_all(test.path.join("foo/bar")).unwrap();
        fs::write(
            test.path.join("foo/version.json"),
            r#"{"inherit":true,"assemblyVersion":{"precision":"minor"},"prerelease":"beta"}"#,
        )
        .unwrap();
        fs::write(
            test.path.join("foo/bar/version.json"),
            r#"{"inherit":true,"versionHeightOffset":1}"#,
        )
        .unwrap();

        let context =
            GitContext::create(test.path.join("foo/bar"), None, GitEngine::ReadOnly).unwrap();
        let (actual, locations) = VersionFile::new(&context)
            .get_working_copy_version(VersionFileRequirements::default())
            .unwrap();
        let actual = actual.unwrap();
        assert_eq!("14.2-beta", actual.version.as_ref().unwrap().to_string());
        assert_eq!(
            VersionPrecision::Minor,
            actual.assembly_version_or_default().precision_or_default()
        );
        assert_eq!(Some(1), actual.version_height_offset);
        assert!(!actual.inherit);
        assert_eq!(None, actual.prerelease);
        assert_eq!(
            Some(test.path.clone()),
            locations.non_inheriting_version_directory
        );
        assert_eq!(
            Some(test.path.clone()),
            locations.version_specifying_version_directory
        );
    }

    #[test]
    fn requirements_can_return_inheriting_file() {
        let test = TestRepository::new();
        fs::write(
            test.path.join(VERSION_JSON_FILE_NAME),
            r#"{"version":"1.0"}"#,
        )
        .unwrap();
        fs::create_dir(test.path.join("child")).unwrap();
        fs::write(
            test.path.join("child").join(VERSION_JSON_FILE_NAME),
            r#"{"inherit":true}"#,
        )
        .unwrap();
        let context =
            GitContext::create(test.path.join("child"), None, GitEngine::ReadOnly).unwrap();
        let requirements = VersionFileRequirements::NON_MERGED_RESULT
            | VersionFileRequirements::ACCEPT_INHERITING_FILE;
        let (actual, _) = VersionFile::new(&context)
            .get_working_copy_version(requirements)
            .unwrap();
        assert!(actual.unwrap().inherit);
    }

    #[test]
    fn historical_lookup_and_ignore_case() {
        let test = TestRepository::new();
        fs::create_dir(test.path.join("MyProject")).unwrap();
        fs::write(
            test.path.join("MyProject").join(VERSION_JSON_FILE_NAME),
            r#"{"version":"1.2.3"}"#,
        )
        .unwrap();
        test.commit("version");
        let repository = Repository::open(&test.path).unwrap();
        repository
            .config()
            .unwrap()
            .set_bool("core.ignorecase", true)
            .unwrap();
        drop(repository);

        let mut context =
            GitContext::create(&test.path, Some("HEAD"), GitEngine::ReadOnly).unwrap();
        context
            .set_repo_relative_project_directory("myproject")
            .unwrap();
        let (actual, _) = VersionFile::new(&context)
            .get_version(VersionFileRequirements::default())
            .unwrap();
        assert_eq!("1.2.3", actual.unwrap().version.unwrap().to_string());
    }

    #[test]
    fn historical_lookup_uses_selected_commit() {
        let test = TestRepository::new();
        fs::write(
            test.path.join(VERSION_JSON_FILE_NAME),
            r#"{"version":"1.0"}"#,
        )
        .unwrap();
        let first = test.commit("first");
        fs::write(
            test.path.join(VERSION_JSON_FILE_NAME),
            r#"{"version":"2.0"}"#,
        )
        .unwrap();
        test.commit("second");

        let context =
            GitContext::create(&test.path, Some(&first.to_string()), GitEngine::ReadOnly).unwrap();
        let (actual, _) = VersionFile::new(&context)
            .get_version(VersionFileRequirements::default())
            .unwrap();
        assert_eq!("1.0", actual.unwrap().version.unwrap().to_string());
    }

    #[test]
    fn set_version_serializes_schema_and_contextual_filters() {
        let test = TestRepository::new();
        let context = GitContext::create(&test.path, None, GitEngine::ReadOnly).unwrap();
        let child = test.path.join("child");
        let mut options = version("3.4");
        options.path_filters = Some(vec![crate::FilterPath::new("./source", "").unwrap()]);
        let path = VersionFile::new(&context)
            .set_version(&child, &options, true)
            .unwrap();
        let content = fs::read_to_string(path).unwrap();
        assert!(content.contains(VersionOptions::schema_url()));
        assert!(content.contains("../source"));
    }

    #[test]
    fn managed_json_compatibility_forms_round_trip_to_canonical_output() {
        let options = VersionOptions::from_json(
            r#"{"version":"2.3","assemblyVersion":{"version":"2.2","precision":"revision"},"buildNumberOffset":-1,"publicReleaseRefSpec":["refs/heads/master"]}"#,
            "",
        )
        .unwrap();
        let serialized: serde_json::Value =
            serde_json::from_str(&options.to_json("").unwrap()).unwrap();
        assert_eq!(serialized["version"], "2.3");
        assert_eq!(serialized["assemblyVersion"]["version"], "2.2");
        assert_eq!(serialized["assemblyVersion"]["precision"], "revision");
        assert_eq!(serialized["versionHeightOffset"], -1);
        assert!(serialized.get("buildNumberOffset").is_none());
        assert_eq!(
            serialized["publicReleaseRefSpec"],
            serde_json::json!(["refs/heads/master"])
        );
    }

    #[test]
    fn existing_version_txt_is_preserved_or_upgraded() {
        let test = TestRepository::new();
        fs::write(test.path.join(VERSION_TXT_FILE_NAME), "1.0").unwrap();
        let context = GitContext::create(&test.path, None, GitEngine::ReadOnly).unwrap();
        let accessor = VersionFile::new(&context);
        let txt = accessor
            .set_version(&test.path, &version("2.0-beta"), false)
            .unwrap();
        assert_eq!(VERSION_TXT_FILE_NAME, txt.file_name().unwrap());
        let mut complex = version("3.0");
        complex.version_height_offset = Some(5);
        let json = accessor.set_version(&test.path, &complex, false).unwrap();
        assert_eq!(VERSION_JSON_FILE_NAME, json.file_name().unwrap());
        assert!(!test.path.join(VERSION_TXT_FILE_NAME).exists());
    }

    #[test]
    fn missing_parent_and_bad_prerelease_are_errors() {
        let test = TestRepository::new();
        fs::write(
            test.path.join(VERSION_JSON_FILE_NAME),
            r#"{"inherit":true,"prerelease":"beta"}"#,
        )
        .unwrap();
        let context = GitContext::create(&test.path, None, GitEngine::ReadOnly).unwrap();
        let error = VersionFile::new(&context)
            .get_working_copy_version(VersionFileRequirements::default())
            .unwrap_err();
        assert!(error.to_string().contains("none exists"));

        fs::write(
            test.path.join(VERSION_JSON_FILE_NAME),
            r#"{"version":"1.0"}"#,
        )
        .unwrap();
        fs::create_dir(test.path.join("child")).unwrap();
        fs::write(
            test.path.join("child").join(VERSION_JSON_FILE_NAME),
            r#"{"version":"1.0-alpha","inherit":true,"prerelease":"beta"}"#,
        )
        .unwrap();
        let context =
            GitContext::create(test.path.join("child"), None, GitEngine::ReadOnly).unwrap();
        let error = VersionFile::new(&context)
            .get_working_copy_version(VersionFileRequirements::default())
            .unwrap_err();
        assert!(error.to_string().contains("already includes a prerelease"));
    }

    #[test]
    fn schema_asset_is_valid_json() {
        let schema: serde_json::Value = serde_json::from_str(VERSION_SCHEMA).unwrap();
        assert_eq!(
            Some("Nerdbank.GitVersioning version.json schema"),
            schema["title"].as_str()
        );
    }
}
