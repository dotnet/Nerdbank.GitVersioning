// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! Git history traversal and the numeric version encoding derived from it.

use std::collections::{HashMap, HashSet, VecDeque};

use git2::{Commit, DiffOptions, Oid, Repository};

use crate::{
    Error, GitContext, Result, SemanticVersion, Version, VersionFile, VersionFileRequirements,
    VersionOptions, VersionPosition,
};

/// The largest build or revision value accepted by the CLR and Windows version resources.
pub const MAXIMUM_VERSION_COMPONENT: u32 = 0xfffe;

/// A version height and the commit that most recently contributed to it.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct VersionHeightCalculation {
    /// The number of relevant commits on the longest ancestry path.
    pub height: u32,
    /// The commit that most recently contributed to `height`.
    pub commit_id: Option<Oid>,
}

/// Calculates the longest single ancestry path, including the selected commit.
pub fn get_height(context: &GitContext) -> Result<u32> {
    let start = selected_commit_id(context)?;
    let mut tracker = HistoryTracker::new(context)?;
    Ok(commit_height(&mut tracker, start, |_| Ok(true))?.height)
}

/// Calculates version height at the selected commit.
///
/// When `base_version` is omitted, the selected commit's version is used. The walk stops
/// independently on each ancestry path as soon as a version change resets height.
pub fn get_version_height(
    context: &GitContext,
    base_version: Option<&Version>,
) -> Result<VersionHeightCalculation> {
    let Some(start) = context.git_commit_id() else {
        return Ok(VersionHeightCalculation {
            height: 0,
            commit_id: None,
        });
    };
    let mut tracker = HistoryTracker::new(context)?;
    let options = tracker.version_at(start)?;
    let Some(options) = options else {
        return Ok(VersionHeightCalculation {
            height: 0,
            commit_id: Some(start),
        });
    };
    let expected = match base_version {
        Some(version) => SemanticVersion::new(*version, "", "")?,
        None => options
            .version
            .clone()
            .unwrap_or_else(|| "0.0".parse().expect("0.0 is valid")),
    };
    let Some(position) = options.version_height_position() else {
        return Ok(VersionHeightCalculation {
            height: 0,
            commit_id: Some(start),
        });
    };

    commit_height(&mut tracker, start, |tracker| {
        commit_matches_version(tracker.current_commit, &expected, position, tracker)
    })
}

/// Applies working-copy reset rules before calculating committed version height.
pub fn calculate_version_height(
    context: &GitContext,
    committed_version: Option<&VersionOptions>,
    working_version: Option<&VersionOptions>,
) -> Result<VersionHeightCalculation> {
    if committed_version != working_version {
        let committed = committed_version
            .and_then(|options| options.version.as_ref())
            .map(|version| version.version)
            .unwrap_or_default();
        let working = working_version
            .and_then(|options| options.version.as_ref())
            .map(|version| version.version);
        if working != Some(committed) {
            return Ok(VersionHeightCalculation {
                height: 0,
                commit_id: context.git_commit_id(),
            });
        }
    }
    get_version_height(context, None)
}

/// Returns the first two object-ID bytes interpreted in network byte order.
#[must_use]
pub fn truncated_commit_id(commit_id: Oid) -> u16 {
    u16::from_be_bytes([commit_id.as_bytes()[0], commit_id.as_bytes()[1]])
}

/// Encodes version height and commit identity in a four-component numeric version.
pub fn encode_version(
    commit_id: Option<Oid>,
    version_options: Option<&VersionOptions>,
    version_height: u32,
) -> Result<Version> {
    let base = version_options
        .and_then(|options| options.version.as_ref())
        .map(|version| version.version)
        .unwrap_or_default();
    let mut build = base.build;
    let mut revision = base.revision;
    let height_position = version_options
        .map(VersionOptions::version_height_position)
        .unwrap_or(Some(VersionPosition::Build));
    let commit_position = version_options
        .map(VersionOptions::git_commit_id_position)
        .unwrap_or(Some(VersionPosition::Revision));

    if let Some(position) = height_position {
        let adjusted = if version_height == 0 {
            0
        } else {
            i64::from(version_height)
                + i64::from(
                    version_options.map_or(0, VersionOptions::effective_version_height_offset),
                )
        };
        if !(0..=i64::from(MAXIMUM_VERSION_COMPONENT)).contains(&adjusted) {
            return Err(Error::InvalidOperation(format!(
                "Git height {adjusted} is outside the allowed range 0..={MAXIMUM_VERSION_COMPONENT}."
            )));
        }
        match position {
            VersionPosition::Build => build = Some(adjusted as u32),
            VersionPosition::Revision => revision = Some(adjusted as u32),
            _ => {}
        }
    }

    if commit_position == Some(VersionPosition::Revision) {
        revision = Some(
            commit_id
                .map(|id| u32::from(truncated_commit_id(id)).min(MAXIMUM_VERSION_COMPONENT))
                .unwrap_or(0),
        );
    }

    Ok(Version {
        major: base.major,
        minor: base.minor,
        build,
        revision,
    })
}

/// Selects committed or working options and encodes a completed height calculation.
///
/// When path filtering left the selected commit irrelevant, the ID of the most recent
/// relevant commit is encoded while the full selected commit ID remains available from
/// [`GitContext`].
pub fn get_id_as_version(
    context: &GitContext,
    committed_version: Option<&VersionOptions>,
    working_version: Option<&VersionOptions>,
    version_height: VersionHeightCalculation,
) -> Result<Version> {
    let options = if committed_version != working_version {
        working_version
    } else {
        committed_version
    };
    encode_version(
        version_height.commit_id.or_else(|| context.git_commit_id()),
        options,
        version_height.height,
    )
}

/// Finds all commits reachable from named references that could have produced `version`.
pub fn get_commits_from_version(context: &GitContext, version: &Version) -> Result<Vec<Oid>> {
    let repository = context
        .repository()
        .ok_or_else(|| Error::InvalidOperation("No repository is available.".to_owned()))?;
    let mut tracker = HistoryTracker::new(context)?;
    let mut matches = Vec::new();
    for commit_id in commits_reachable_from_refs(repository)? {
        let Some(options) = tracker.version_at(commit_id)? else {
            continue;
        };
        if !options
            .version
            .as_ref()
            .is_some_and(|candidate| candidate.is_matching_version(version))
            || commit_id_mismatch(version, &options, commit_id)?
            || version_height_mismatch(version, &options, commit_id, &mut tracker)?
        {
            continue;
        }
        matches.push(commit_id);
    }
    Ok(matches)
}

/// Finds the unique reachable commit that could have produced `version`.
///
/// An ambiguous version is an error rather than an arbitrary match.
pub fn get_commit_from_version(context: &GitContext, version: &Version) -> Result<Option<Oid>> {
    let matches = get_commits_from_version(context, version)?;
    match matches.as_slice() {
        [] => Ok(None),
        [commit] => Ok(Some(*commit)),
        _ => Err(Error::InvalidOperation(format!(
            "Version '{version}' matches more than one commit."
        ))),
    }
}

struct HistoryTracker<'context> {
    context: &'context GitContext,
    repository: &'context Repository,
    versions: HashMap<Oid, Option<VersionOptions>>,
    heights: HashMap<Oid, VersionHeightCalculation>,
    current_commit: Oid,
    ignore_case: Option<bool>,
}

impl<'context> HistoryTracker<'context> {
    fn new(context: &'context GitContext) -> Result<Self> {
        let repository = context
            .repository()
            .ok_or_else(|| Error::InvalidOperation("No repository is available.".to_owned()))?;
        Ok(Self {
            context,
            repository,
            versions: HashMap::new(),
            heights: HashMap::new(),
            current_commit: Oid::ZERO_SHA1,
            ignore_case: None,
        })
    }

    fn ignore_case(&mut self) -> Result<bool> {
        if let Some(ignore_case) = self.ignore_case {
            return Ok(ignore_case);
        }
        let ignore_case = self
            .repository
            .config()?
            .get_bool("core.ignorecase")
            .unwrap_or(false);
        self.ignore_case = Some(ignore_case);
        Ok(ignore_case)
    }

    fn version_at(&mut self, commit_id: Oid) -> Result<Option<VersionOptions>> {
        if let Some(version) = self.versions.get(&commit_id) {
            return Ok(version.clone());
        }
        let ignore_case = self.ignore_case()?;
        let commit = self.repository.find_commit(commit_id)?;
        version_at_commit(
            self.context,
            self.repository,
            &mut self.versions,
            &commit,
            ignore_case,
        )
    }
}

fn version_at_commit(
    context: &GitContext,
    repository: &Repository,
    versions: &mut HashMap<Oid, Option<VersionOptions>>,
    commit: &Commit<'_>,
    ignore_case: bool,
) -> Result<Option<VersionOptions>> {
    let commit_id = commit.id();
    if let Some(version) = versions.get(&commit_id) {
        return Ok(version.clone());
    }
    let version = VersionFile::new(context)
        .get_version_from_commit(
            repository,
            commit,
            ignore_case,
            VersionFileRequirements::default(),
        )
        .map_err(|error| {
            Error::InvalidOperation(format!(
                "Unable to get version from commit {commit_id}: {error}"
            ))
        })?
        .0;
    versions.insert(commit_id, version.clone());
    Ok(version)
}

fn selected_commit_id(context: &GitContext) -> Result<Oid> {
    context
        .git_commit_id()
        .ok_or_else(|| Error::InvalidOperation("No commit is selected.".to_owned()))
}

fn commit_height<F>(
    tracker: &mut HistoryTracker<'_>,
    start: Oid,
    mut continue_stepping: F,
) -> Result<VersionHeightCalculation>
where
    F: FnMut(&mut HistoryTracker<'_>) -> Result<bool>,
{
    tracker.current_commit = start;
    if !continue_stepping(tracker)? {
        return Ok(VersionHeightCalculation {
            height: 0,
            commit_id: Some(start),
        });
    }

    let mut stack = vec![start];
    while let Some(&commit_id) = stack.last() {
        if tracker.heights.contains_key(&commit_id) {
            stack.pop();
            continue;
        }
        let ignore_case = tracker.ignore_case()?;
        let commit = tracker.repository.find_commit(commit_id)?;
        let parent_ids: Vec<_> = commit.parent_ids().collect();
        let mut missing_parent = false;
        for parent_id in &parent_ids {
            if tracker.heights.contains_key(parent_id) {
                continue;
            }
            tracker.current_commit = *parent_id;
            if continue_stepping(tracker)? {
                stack.push(*parent_id);
                missing_parent = true;
            }
        }
        if missing_parent {
            continue;
        }

        let mut best = VersionHeightCalculation {
            height: 0,
            commit_id: None,
        };
        for parent_id in parent_ids {
            if let Some(parent) = tracker.heights.get(&parent_id)
                && (parent.height > best.height
                    || (parent.height == best.height
                        && best.commit_id.is_none()
                        && parent.commit_id.is_some()))
            {
                best = *parent;
            }
        }
        let options = version_at_commit(
            tracker.context,
            tracker.repository,
            &mut tracker.versions,
            &commit,
            ignore_case,
        )?;
        let relevant = match options
            .as_ref()
            .and_then(|options| options.path_filters.as_ref())
        {
            Some(filters) => is_relevant_commit(tracker.repository, &commit, filters, ignore_case)?,
            None => true,
        };
        let height = best.height + u32::from(relevant);
        tracker.heights.insert(
            commit_id,
            VersionHeightCalculation {
                height,
                commit_id: if relevant {
                    Some(commit_id)
                } else {
                    best.commit_id
                },
            },
        );
        stack.pop();
    }
    Ok(*tracker
        .heights
        .get(&start)
        .expect("the starting commit was evaluated"))
}

fn commit_matches_version(
    commit_id: Oid,
    expected: &SemanticVersion,
    precision: VersionPosition,
    tracker: &mut HistoryTracker<'_>,
) -> Result<bool> {
    let Some(options) = tracker.version_at(commit_id)? else {
        return Ok(false);
    };
    let Some(actual) = options.version.as_ref() else {
        return Ok(false);
    };
    Ok(options.version_height_position() == Some(precision)
        && !SemanticVersion::will_version_change_reset_version_height(actual, expected, precision)?)
}

fn is_relevant_commit(
    repository: &Repository,
    commit: &Commit<'_>,
    filters: &[crate::FilterPath],
    ignore_case: bool,
) -> Result<bool> {
    let tree = commit.tree()?;
    if commit.parent_count() == 0 {
        return diff_is_relevant(repository, None, &tree, filters, ignore_case);
    }
    for parent in commit.parents() {
        let parent_tree = parent.tree()?;
        if diff_is_relevant(repository, Some(&parent_tree), &tree, filters, ignore_case)? {
            return Ok(true);
        }
    }
    Ok(false)
}

fn diff_is_relevant(
    repository: &Repository,
    old_tree: Option<&git2::Tree<'_>>,
    new_tree: &git2::Tree<'_>,
    filters: &[crate::FilterPath],
    ignore_case: bool,
) -> Result<bool> {
    let mut options = DiffOptions::new();
    options.context_lines(0);
    let diff = repository.diff_tree_to_tree(old_tree, Some(new_tree), Some(&mut options))?;
    let has_includes = filters.iter().any(crate::FilterPath::is_include);
    for delta in diff.deltas() {
        for path in [delta.old_file().path(), delta.new_file().path()]
            .into_iter()
            .flatten()
        {
            let path = path.to_string_lossy().replace('\\', "/");
            if (!has_includes
                || filters
                    .iter()
                    .any(|filter| filter.includes(&path, ignore_case)))
                && !filters
                    .iter()
                    .any(|filter| filter.excludes(&path, ignore_case))
            {
                return Ok(true);
            }
        }
    }
    Ok(false)
}

fn commits_reachable_from_refs(repository: &Repository) -> Result<Vec<Oid>> {
    let mut queue = VecDeque::new();
    if let Ok(head) = repository.head().and_then(|head| head.peel_to_commit()) {
        queue.push_back(head.id());
    }
    for reference in repository.references()? {
        let reference = reference?;
        if let Ok(commit) = reference.peel_to_commit() {
            queue.push_back(commit.id());
        }
    }
    let mut visited = HashSet::new();
    let mut commits = Vec::new();
    while let Some(commit_id) = queue.pop_front() {
        if !visited.insert(commit_id) {
            continue;
        }
        let commit = repository.find_commit(commit_id)?;
        commits.push(commit_id);
        queue.extend(commit.parent_ids());
    }
    Ok(commits)
}

fn version_height_mismatch(
    version: &Version,
    options: &VersionOptions,
    commit_id: Oid,
    tracker: &mut HistoryTracker<'_>,
) -> Result<bool> {
    let Some(position) = options.version_height_position() else {
        return Ok(false);
    };
    if position > VersionPosition::Revision {
        return Ok(false);
    }
    let expected = read_version_position(*version, position)?;
    let comparison_precision = match position {
        VersionPosition::Major => return Ok(false),
        VersionPosition::Minor => VersionPosition::Major,
        VersionPosition::Build => VersionPosition::Minor,
        VersionPosition::Revision => VersionPosition::Build,
        _ => unreachable!(),
    };
    let calculation = commit_height(tracker, commit_id, |tracker| {
        commit_matches_numeric_version(
            tracker.current_commit,
            version,
            comparison_precision,
            tracker,
        )
    })?;
    let actual =
        i64::from(calculation.height) + i64::from(options.effective_version_height_offset());
    Ok(expected != actual)
}

fn commit_matches_numeric_version(
    commit_id: Oid,
    expected: &Version,
    precision: VersionPosition,
    tracker: &mut HistoryTracker<'_>,
) -> Result<bool> {
    let Some(options) = tracker.version_at(commit_id)? else {
        return Ok(false);
    };
    let Some(actual) = options.version.as_ref() else {
        return Ok(false);
    };
    for position in [
        VersionPosition::Major,
        VersionPosition::Minor,
        VersionPosition::Build,
        VersionPosition::Revision,
    ]
    .into_iter()
    .take(precision as usize + 1)
    {
        if actual.read_version_position(position)? != read_version_position(*expected, position)? {
            return Ok(false);
        }
    }
    Ok(true)
}

fn commit_id_mismatch(version: &Version, options: &VersionOptions, commit_id: Oid) -> Result<bool> {
    let Some(position) = options.git_commit_id_position() else {
        return Ok(false);
    };
    if position > VersionPosition::Revision {
        return Ok(false);
    }
    let expected = read_version_position(*version, position)?;
    if expected < 0 {
        return Ok(false);
    }
    let expected = u16::try_from(expected).map_err(|_| {
        Error::InvalidFormat(format!("Commit ID component {expected} is not 16-bit."))
    })?;
    let bytes = commit_id.as_bytes();
    let big = u16::from_be_bytes([bytes[0], bytes[1]]);
    let little = u16::from_le_bytes([bytes[0], bytes[1]]);
    let mask = if u32::from(expected) == MAXIMUM_VERSION_COMPONENT {
        0xfffe
    } else {
        0xffff
    };
    Ok((big & mask) != expected && (little & mask) != expected)
}

fn read_version_position(version: Version, position: VersionPosition) -> Result<i64> {
    match position {
        VersionPosition::Major => Ok(i64::from(version.major)),
        VersionPosition::Minor => Ok(i64::from(version.minor)),
        VersionPosition::Build => Ok(version.build_or_unspecified()),
        VersionPosition::Revision => Ok(version.revision_or_unspecified()),
        _ => Err(Error::InvalidOperation(
            "The position must be numeric.".to_owned(),
        )),
    }
}

#[cfg(test)]
mod tests {
    use std::fs;
    use std::path::PathBuf;
    use std::sync::atomic::{AtomicU64, Ordering};

    use git2::{IndexAddOption, Signature};

    use super::*;
    use crate::GitEngine;

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
                .join(format!("history-{}-{id}", std::process::id()));
            let _ = fs::remove_dir_all(&path);
            fs::create_dir_all(&path).unwrap();
            Repository::init(&path).unwrap();
            Self { path }
        }

        fn commit(&self, message: &str, parents: &[Oid]) -> Oid {
            let repository = Repository::open(&self.path).unwrap();
            let mut index = repository.index().unwrap();
            index.add_all(["*"], IndexAddOption::DEFAULT, None).unwrap();
            index.write().unwrap();
            drop(index);
            drop(repository);
            self.commit_index(message, parents)
        }

        fn commit_index(&self, message: &str, parents: &[Oid]) -> Oid {
            let repository = Repository::open(&self.path).unwrap();
            let mut index = repository.index().unwrap();
            let tree_id = index.write_tree().unwrap();
            let tree = repository.find_tree(tree_id).unwrap();
            let signature = Signature::now("Test", "test@example.com").unwrap();
            let parents: Vec<_> = parents
                .iter()
                .map(|id| repository.find_commit(*id).unwrap())
                .collect();
            let commit = repository
                .commit(
                    None,
                    &signature,
                    &signature,
                    message,
                    &tree,
                    &parents.iter().collect::<Vec<_>>(),
                )
                .unwrap();
            repository
                .reference("refs/heads/master", commit, true, "test")
                .unwrap();
            repository.set_head("refs/heads/master").unwrap();
            commit
        }

        fn commit_head(&self, message: &str) -> Oid {
            let repository = Repository::open(&self.path).unwrap();
            let parents: Vec<_> = repository
                .head()
                .ok()
                .and_then(|head| head.target())
                .into_iter()
                .collect();
            drop(repository);
            self.commit(message, &parents)
        }

        fn context(&self, commit: Oid, project: &str) -> GitContext {
            let mut context =
                GitContext::create(&self.path, Some(&commit.to_string()), GitEngine::ReadOnly)
                    .unwrap();
            context
                .set_repo_relative_project_directory(project)
                .unwrap();
            context
        }
    }

    impl Drop for TestRepository {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.path);
        }
    }

    #[test]
    fn height_uses_longest_merge_path_and_stops_each_path() {
        let test = TestRepository::new();
        let root = test.commit("root", &[]);
        let short = test.commit("short", &[root]);
        let long1 = test.commit("long1", &[root]);
        let long2 = test.commit("long2", &[long1]);
        let long3 = test.commit("long3", &[long2]);
        let merge = test.commit("merge", &[short, long3]);
        let context = test.context(merge, "");
        assert_eq!(5, get_height(&context).unwrap());

        let mut tracker = HistoryTracker::new(&context).unwrap();
        let stopped = commit_height(&mut tracker, merge, |tracker| {
            Ok(tracker.current_commit != long1 && tracker.current_commit != short)
        })
        .unwrap();
        assert_eq!(3, stopped.height);
    }

    #[test]
    fn version_changes_reset_height_and_working_copy_can_reset_to_zero() {
        let test = TestRepository::new();
        fs::write(test.path.join("version.json"), r#"{"version":"1.0"}"#).unwrap();
        let first = test.commit_head("1.0");
        let second = test.commit_head("more");
        fs::write(test.path.join("version.json"), r#"{"version":"1.1"}"#).unwrap();
        let third = test.commit_head("1.1");
        assert_eq!(
            2,
            get_version_height(&test.context(second, ""), None)
                .unwrap()
                .height
        );
        let context = test.context(third, "");
        assert_eq!(1, get_version_height(&context, None).unwrap().height);

        let committed = VersionOptions::from_version(Version::new(1, 1), None).unwrap();
        let working = VersionOptions::from_version(Version::new(1, 2), None).unwrap();
        assert_eq!(
            0,
            calculate_version_height(&context, Some(&committed), Some(&working))
                .unwrap()
                .height
        );
        assert_ne!(first, third);
    }

    #[test]
    fn filters_count_add_modify_delete_and_ignore_other_paths() {
        let test = TestRepository::new();
        fs::create_dir_all(test.path.join("project")).unwrap();
        fs::write(
            test.path.join("project/version.json"),
            r#"{"version":"1.0","pathFilters":["./**",":!/project/excluded/**"]}"#,
        )
        .unwrap();
        let one = test.commit_head("version");
        fs::write(test.path.join("outside.txt"), "outside").unwrap();
        let two = test.commit_head("outside");
        fs::write(test.path.join("project/included.txt"), "included").unwrap();
        let three = test.commit_head("included");
        fs::create_dir_all(test.path.join("project/excluded")).unwrap();
        fs::write(test.path.join("project/excluded/no.txt"), "excluded").unwrap();
        let four = test.commit_head("excluded");
        fs::remove_dir_all(test.path.join("project/excluded")).unwrap();
        let five = test.commit_head("delete excluded directory");
        fs::remove_file(test.path.join("project/included.txt")).unwrap();
        let six = test.commit_head("delete included");

        assert_eq!(
            1,
            get_version_height(&test.context(one, "project"), None)
                .unwrap()
                .height
        );
        assert_eq!(
            1,
            get_version_height(&test.context(two, "project"), None)
                .unwrap()
                .height
        );
        assert_eq!(
            2,
            get_version_height(&test.context(three, "project"), None)
                .unwrap()
                .height
        );
        assert_eq!(
            2,
            get_version_height(&test.context(four, "project"), None)
                .unwrap()
                .height
        );
        assert_eq!(
            2,
            get_version_height(&test.context(five, "project"), None)
                .unwrap()
                .height
        );
        assert_eq!(
            3,
            get_version_height(&test.context(six, "project"), None)
                .unwrap()
                .height
        );
    }

    #[test]
    fn only_exclude_filter_counts_deleting_an_included_file() {
        let test = TestRepository::new();
        fs::write(
            test.path.join("version.json"),
            r#"{"version":"1.2","pathFilters":[":!README.md"]}"#,
        )
        .unwrap();
        test.commit_head("version");
        fs::write(test.path.join("included.txt"), "included").unwrap();
        let added = test.commit_head("add included file");
        fs::remove_file(test.path.join("included.txt")).unwrap();
        let deleted = test.commit_head("delete included file");

        assert_eq!(
            2,
            get_version_height(&test.context(added, ""), None)
                .unwrap()
                .height
        );
        assert_eq!(
            3,
            get_version_height(&test.context(deleted, ""), None)
                .unwrap()
                .height
        );
    }

    #[test]
    fn included_file_mode_change_increases_height() {
        let test = TestRepository::new();
        fs::write(
            test.path.join("version.json"),
            r#"{"version":"1.2","pathFilters":["included.txt"]}"#,
        )
        .unwrap();
        fs::write(test.path.join("included.txt"), "included").unwrap();
        let added = test.commit_head("add included file");

        let repository = Repository::open(&test.path).unwrap();
        let mut index = repository.index().unwrap();
        let mut entry = index
            .get_path(std::path::Path::new("included.txt"), 0)
            .unwrap();
        entry.mode = 0o100755;
        index.add(&entry).unwrap();
        index.write().unwrap();
        drop(index);
        drop(repository);
        let mode_changed = test.commit_index("make included file executable", &[added]);

        assert_eq!(
            1,
            get_version_height(&test.context(added, ""), None)
                .unwrap()
                .height
        );
        assert_eq!(
            2,
            get_version_height(&test.context(mode_changed, ""), None)
                .unwrap()
                .height
        );
    }

    #[test]
    fn include_filter_honors_ignore_case() {
        let test = TestRepository::new();
        let repository = Repository::open(&test.path).unwrap();
        repository
            .config()
            .unwrap()
            .set_bool("core.ignorecase", true)
            .unwrap();
        drop(repository);
        fs::write(
            test.path.join("version.json"),
            r#"{"version":"1.2","pathFilters":["SRC/INCLUDED.TXT"]}"#,
        )
        .unwrap();
        fs::create_dir_all(test.path.join("src")).unwrap();
        fs::write(test.path.join("src/included.txt"), "included").unwrap();
        let commit = test.commit_head("add included file with different casing");

        assert_eq!(
            1,
            get_version_height(&test.context(commit, ""), None)
                .unwrap()
                .height
        );
    }

    #[test]
    fn replacing_file_with_included_directory_increases_height() {
        let test = TestRepository::new();
        fs::write(
            test.path.join("version.json"),
            r#"{"version":"1.2","pathFilters":["target/included.txt"]}"#,
        )
        .unwrap();
        fs::write(test.path.join("target"), "not included").unwrap();
        let file = test.commit_head("add file outside filter");
        fs::remove_file(test.path.join("target")).unwrap();
        fs::create_dir(test.path.join("target")).unwrap();
        fs::write(test.path.join("target/included.txt"), "included").unwrap();
        let directory = test.commit_head("replace file with included directory");

        assert_eq!(
            0,
            get_version_height(&test.context(file, ""), None)
                .unwrap()
                .height
        );
        assert_eq!(
            1,
            get_version_height(&test.context(directory, ""), None)
                .unwrap()
                .height
        );
    }

    #[test]
    fn replacing_included_directory_with_file_increases_height() {
        let test = TestRepository::new();
        fs::write(
            test.path.join("version.json"),
            r#"{"version":"1.2","pathFilters":["target/included.txt"]}"#,
        )
        .unwrap();
        fs::create_dir(test.path.join("target")).unwrap();
        fs::write(test.path.join("target/included.txt"), "included").unwrap();
        let directory = test.commit_head("add included directory");
        fs::remove_dir_all(test.path.join("target")).unwrap();
        fs::write(test.path.join("target"), "not included").unwrap();
        let file = test.commit_head("replace included directory with file");

        assert_eq!(
            1,
            get_version_height(&test.context(directory, ""), None)
                .unwrap()
                .height
        );
        assert_eq!(
            2,
            get_version_height(&test.context(file, ""), None)
                .unwrap()
                .height
        );
    }

    #[test]
    fn wildcard_include_filter_counts_matching_path() {
        let test = TestRepository::new();
        fs::write(
            test.path.join("version.json"),
            r#"{"version":"1.2","pathFilters":["dir/*.txt"]}"#,
        )
        .unwrap();
        fs::create_dir(test.path.join("dir")).unwrap();
        fs::write(test.path.join("dir/value.txt"), "included").unwrap();
        let commit = test.commit_head("add wildcard-matched file");

        assert_eq!(
            1,
            get_version_height(&test.context(commit, ""), None)
                .unwrap()
                .height
        );
    }

    #[test]
    fn encoding_uses_big_endian_cap_offset_and_defaults() {
        let id = Oid::from_str("ffff000000000000000000000000000000000000").unwrap();
        let options: VersionOptions =
            serde_json::from_str(r#"{"version":"1.2","versionHeightOffset":5}"#).unwrap();
        assert_eq!(0xffff, truncated_commit_id(id));
        assert_eq!(
            Version::new_with_revision(1, 2, 8, 0xfffe),
            encode_version(Some(id), Some(&options), 3).unwrap()
        );
        assert_eq!(
            Version::new_with_revision(0, 0, 0, 0),
            encode_version(None, None, 0).unwrap()
        );
        assert!(encode_version(Some(id), Some(&options), 0xffff).is_err());
        assert!(
            !commit_id_mismatch(
                &Version::new_with_revision(1, 2, 3, 0xfffe),
                &VersionOptions::from_version(Version::new(1, 2), None).unwrap(),
                id,
            )
            .unwrap()
        );
    }

    #[test]
    fn reverse_lookup_accepts_both_endians_and_subdirectories() {
        let test = TestRepository::new();
        fs::create_dir_all(test.path.join("sub")).unwrap();
        fs::write(test.path.join("version.json"), r#"{"version":"1.0"}"#).unwrap();
        fs::write(test.path.join("sub/version.json"), r#"{"version":"2.0"}"#).unwrap();
        let commit = test.commit_head("versions");
        let root_context = test.context(commit, "");
        let sub_context = test.context(commit, "sub");
        let root_height = get_version_height(&root_context, None).unwrap();
        let encoded = encode_version(
            Some(commit),
            VersionFile::new(&root_context)
                .get_version(VersionFileRequirements::default())
                .unwrap()
                .0
                .as_ref(),
            root_height.height,
        )
        .unwrap();
        assert_eq!(
            Some(commit),
            get_commit_from_version(&root_context, &encoded).unwrap()
        );
        assert_eq!(
            None,
            get_commit_from_version(&sub_context, &encoded).unwrap()
        );

        let swapped = Version::new_with_revision(
            encoded.major,
            encoded.minor,
            encoded.build.unwrap(),
            u32::from((encoded.revision.unwrap() as u16).swap_bytes()),
        );
        assert_eq!(
            Some(commit),
            get_commit_from_version(&root_context, &swapped).unwrap()
        );
    }

    #[test]
    fn reverse_lookup_rejects_versions_missing_the_height_component() {
        for (version_json, version) in [
            (r#"{"version":"1.2"}"#, Version::new(1, 2)),
            (r#"{"version":"1.2.3"}"#, Version::new_with_build(1, 2, 3)),
        ] {
            let test = TestRepository::new();
            fs::write(test.path.join("version.json"), version_json).unwrap();
            let commit = test.commit_head("version");
            let context = test.context(commit, "");
            assert_eq!(None, get_commit_from_version(&context, &version).unwrap());
        }
    }

    #[test]
    fn ambiguity_is_reported_but_all_matches_are_available() {
        let test = TestRepository::new();
        fs::write(test.path.join("version.json"), r#"{"version":"1.2.3.4"}"#).unwrap();
        let first = test.commit_head("first");
        let second = test.commit_head("second");
        let context = test.context(second, "");
        let version = Version::new_with_revision(1, 2, 3, 4);
        let matches = get_commits_from_version(&context, &version).unwrap();
        assert!(matches.contains(&first));
        assert!(matches.contains(&second));
        assert!(get_commit_from_version(&context, &version).is_err());
    }

    #[test]
    fn missing_shallow_parent_is_an_error() {
        let test = TestRepository::new();
        fs::write(test.path.join("version.json"), r#"{"version":"1.0"}"#).unwrap();
        let parent = test.commit_head("parent");
        let child = test.commit_head("child");
        let repository = Repository::open(&test.path).unwrap();
        fs::write(repository.path().join("shallow"), format!("{parent}\n")).unwrap();
        let parent_path = repository
            .path()
            .join("objects")
            .join(&parent.to_string()[..2])
            .join(&parent.to_string()[2..]);
        drop(repository);
        if parent_path.exists() {
            fs::remove_file(parent_path).unwrap();
        }
        let error = get_version_height(&test.context(child, ""), None).unwrap_err();
        assert!(
            error.to_string().contains("object")
                || error.to_string().contains("commit")
                || error.to_string().contains("not found")
        );
    }
}
