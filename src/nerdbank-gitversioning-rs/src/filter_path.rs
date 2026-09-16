// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

use std::fmt;

use serde::{Deserialize, Deserializer, Serialize, Serializer};

use crate::{Error, Result};

/// A filter (include or exclude) representing a repository-relative path.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct FilterPath {
    repo_relative_path: String,
    is_exclude: bool,
    is_relative: bool,
    wildcard_segments: Option<Vec<String>>,
}

impl FilterPath {
    /// Creates a filter from a pathspec-like string and a relative path within the repository.
    ///
    /// `path_spec` may be relative (`../included.txt`), absolute (`:/included.txt`),
    /// excluded (`:!excluded.txt` or `:^excluded.txt`), or contain wildcards. `*` matches
    /// zero or more characters in one path segment, `?` matches one character in a segment,
    /// and a segment consisting of `**` matches zero or more path segments.
    ///
    /// `relative_to` is relative to the repository root; an empty string means the root.
    pub fn new(path_spec: &str, relative_to: &str) -> Result<Self> {
        if path_spec.is_empty() {
            return Err(Error::InvalidFormat(
                "A pathspec may not be empty.".to_owned(),
            ));
        }

        let (is_exclude, is_relative, path) = if let Some(magic) = path_spec.strip_prefix(':') {
            if let Some(path) = magic.strip_prefix(['^', '!']) {
                let (is_relative, path) = Self::normalize(path, relative_to)?;
                (true, is_relative, path)
            } else if let Some(path) = magic.strip_prefix(['/', '\\']) {
                (false, false, path.to_owned())
            } else {
                return Err(Error::InvalidFormat(format!(
                    "Unrecognized path spec '{path_spec}'"
                )));
            }
        } else {
            let (is_relative, path) = Self::normalize(path_spec, relative_to)?;
            (false, is_relative, path)
        };

        let repo_relative_path = path.replace('\\', "/").trim_end_matches('/').to_owned();
        let wildcard_segments = repo_relative_path.contains(['*', '?']).then(|| {
            repo_relative_path
                .split('/')
                .filter(|segment| !segment.is_empty())
                .map(str::to_owned)
                .collect()
        });

        Ok(Self {
            repo_relative_path,
            is_exclude,
            is_relative,
            wildcard_segments,
        })
    }

    /// Gets whether this is an exclude filter.
    pub fn is_exclude(&self) -> bool {
        self.is_exclude
    }

    /// Gets whether this is an include filter.
    pub fn is_include(&self) -> bool {
        !self.is_exclude
    }

    /// Gets the represented path relative to the repository root.
    ///
    /// Directory separators are forward slashes.
    pub fn repo_relative_path(&self) -> &str {
        &self.repo_relative_path
    }

    /// Gets whether this filter represents the repository root.
    pub fn is_root(&self) -> bool {
        self.repo_relative_path.is_empty()
    }

    /// Gets whether the original pathspec was parsed as relative.
    pub fn is_relative(&self) -> bool {
        self.is_relative
    }

    /// Gets whether this filter contains wildcard characters.
    pub fn has_wildcard(&self) -> bool {
        self.wildcard_segments.is_some()
    }

    /// Determines whether this excluding filter matches `repo_relative_path`.
    ///
    /// Set `ignore_case` to the repository's `core.ignorecase` value.
    pub fn excludes(&self, repo_relative_path: &str, ignore_case: bool) -> bool {
        self.is_exclude
            && if self.has_wildcard() {
                self.matches_wildcard(repo_relative_path, ignore_case, false)
            } else {
                Self::matches_path_or_child(
                    &self.repo_relative_path,
                    repo_relative_path,
                    ignore_case,
                )
            }
    }

    /// Determines whether this including filter matches `repo_relative_path`.
    ///
    /// Set `ignore_case` to the repository's `core.ignorecase` value.
    pub fn includes(&self, repo_relative_path: &str, ignore_case: bool) -> bool {
        self.is_include()
            && (self.is_root()
                || if self.has_wildcard() {
                    self.matches_wildcard(repo_relative_path, ignore_case, false)
                } else {
                    Self::matches_path_or_child(
                        &self.repo_relative_path,
                        repo_relative_path,
                        ignore_case,
                    )
                })
    }

    /// Determines whether this including filter may match children of
    /// `repo_relative_path`.
    pub fn includes_children(&self, repo_relative_path: &str, ignore_case: bool) -> bool {
        if !self.is_include() {
            return false;
        }
        if self.is_root() {
            return true;
        }
        if self.has_wildcard() {
            return self.matches_wildcard(repo_relative_path, ignore_case, true);
        }

        let prefix = format!("{repo_relative_path}/");
        Self::starts_with(&self.repo_relative_path, &prefix, ignore_case)
    }

    /// Converts this filter to a pathspec.
    ///
    /// `repo_relative_base_directory` is the repository-relative directory from which
    /// relative pathspecs should be expressed. An empty string means the repository root.
    pub fn to_path_spec(&self, repo_relative_base_directory: &str) -> Result<String> {
        let (_, normalized_base) = Self::normalize(
            if repo_relative_base_directory.is_empty() {
                "."
            } else {
                repo_relative_base_directory
            },
            "",
        )?;
        let mut path_spec = if self.is_exclude {
            ":!".to_owned()
        } else {
            String::new()
        };

        if self.is_relative {
            let (dirs_ascended, relative_path) =
                Self::get_relative_path(&self.repo_relative_path, &normalized_base);
            if dirs_ascended == 0 && !self.is_exclude {
                path_spec.push_str("./");
            }
            path_spec.push_str(&relative_path);
        } else {
            path_spec.push('/');
            path_spec.push_str(&self.repo_relative_path);
        }

        Ok(path_spec)
    }

    fn normalize(path: &str, relative_to: &str) -> Result<(bool, String)> {
        if path.is_empty() {
            return Err(Error::InvalidFormat("A path may not be empty.".to_owned()));
        }
        if let Some(path) = path.strip_prefix(['/', '\\']) {
            return Ok((false, path.to_owned()));
        }

        let combined = if relative_to.is_empty() {
            path.to_owned()
        } else {
            format!("{relative_to}/{path}")
        };
        let mut parts = Vec::new();
        for segment in combined.split(['/', '\\']).filter(|part| !part.is_empty()) {
            match segment {
                "." => {}
                ".." => {
                    if parts.pop().is_none() {
                        return Err(Error::InvalidFormat(format!(
                            "Too many '..' in path '{combined}' - would escape the root of the repository."
                        )));
                    }
                }
                _ => parts.push(segment),
            }
        }

        Ok((true, parts.join("/")))
    }

    fn get_relative_path(path: &str, relative_to: &str) -> (usize, String) {
        let path_parts: Vec<_> = path.split('/').collect();
        let base_parts: Vec<_> = relative_to
            .split('/')
            .filter(|part| !part.is_empty())
            .collect();
        let common_parts = path_parts
            .iter()
            .zip(&base_parts)
            .take_while(|(left, right)| Self::strings_equal(left, right, true))
            .count();
        let dirs_to_ascend = base_parts.len() - common_parts;
        let mut result = "../".repeat(dirs_to_ascend);
        result.push_str(&path_parts[common_parts..].join("/"));
        (dirs_to_ascend, result)
    }

    fn matches_path_or_child(filter: &str, candidate: &str, ignore_case: bool) -> bool {
        Self::strings_equal(filter, candidate, ignore_case)
            || Self::starts_with(candidate, &format!("{filter}/"), ignore_case)
    }

    fn starts_with(value: &str, prefix: &str, ignore_case: bool) -> bool {
        if !ignore_case {
            return value.starts_with(prefix);
        }
        let mut value = value.chars();
        prefix.chars().all(|expected| {
            value
                .next()
                .is_some_and(|actual| Self::characters_equal(expected, actual, true))
        })
    }

    fn strings_equal(left: &str, right: &str, ignore_case: bool) -> bool {
        if ignore_case {
            left.chars().count() == right.chars().count()
                && left
                    .chars()
                    .zip(right.chars())
                    .all(|(left, right)| Self::characters_equal(left, right, true))
        } else {
            left == right
        }
    }

    fn matches_segment(pattern: &str, value: &str, ignore_case: bool) -> bool {
        let pattern: Vec<_> = pattern.chars().collect();
        let value: Vec<_> = value.chars().collect();
        let mut matches = vec![vec![false; value.len() + 1]; pattern.len() + 1];
        matches[pattern.len()][value.len()] = true;

        for pattern_index in (0..pattern.len()).rev() {
            if pattern[pattern_index] == '*' {
                matches[pattern_index][value.len()] = matches[pattern_index + 1][value.len()];
            }
            for value_index in (0..value.len()).rev() {
                matches[pattern_index][value_index] = if pattern[pattern_index] == '*' {
                    matches[pattern_index + 1][value_index]
                        || matches[pattern_index][value_index + 1]
                } else if pattern[pattern_index] == '?'
                    || Self::characters_equal(
                        pattern[pattern_index],
                        value[value_index],
                        ignore_case,
                    )
                {
                    matches[pattern_index + 1][value_index + 1]
                } else {
                    false
                };
            }
        }

        matches[0][0]
    }

    fn characters_equal(left: char, right: char, ignore_case: bool) -> bool {
        left == right || (ignore_case && left.to_uppercase().eq(right.to_uppercase()))
    }

    fn matches_wildcard(
        &self,
        repo_relative_path: &str,
        ignore_case: bool,
        match_descendants: bool,
    ) -> bool {
        let candidate = repo_relative_path.replace('\\', "/");
        let candidate_segments: Vec<_> = candidate
            .split('/')
            .filter(|segment| !segment.is_empty())
            .collect();
        let filter_segments = self.wildcard_segments.as_ref().expect("wildcard segments");
        let mut matches =
            vec![vec![false; candidate_segments.len() + 1]; filter_segments.len() + 1];

        matches[filter_segments.len()].fill(true);
        for filter_index in (0..filter_segments.len()).rev() {
            let recursive = filter_segments[filter_index] == "**";
            matches[filter_index][candidate_segments.len()] = match_descendants
                || (recursive && matches[filter_index + 1][candidate_segments.len()]);
            for candidate_index in (0..candidate_segments.len()).rev() {
                matches[filter_index][candidate_index] = if recursive {
                    matches[filter_index + 1][candidate_index]
                        || matches[filter_index][candidate_index + 1]
                } else {
                    Self::matches_segment(
                        &filter_segments[filter_index],
                        candidate_segments[candidate_index],
                        ignore_case,
                    ) && matches[filter_index + 1][candidate_index + 1]
                };
            }
        }

        matches[0][0]
    }
}

impl fmt::Display for FilterPath {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(&self.repo_relative_path)
    }
}

impl Serialize for FilterPath {
    fn serialize<S>(&self, serializer: S) -> std::result::Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        self.to_path_spec("")
            .map_err(serde::ser::Error::custom)?
            .serialize(serializer)
    }
}

impl<'de> Deserialize<'de> for FilterPath {
    fn deserialize<D>(deserializer: D) -> std::result::Result<Self, D::Error>
    where
        D: Deserializer<'de>,
    {
        let path_spec = String::deserialize(deserializer)?;
        Self::new(&path_spec, "").map_err(serde::de::Error::custom)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_to_repo_relative_path() {
        let cases = [
            ("./", "foo", "foo"),
            ("../relative-dir", "foo", "relative-dir"),
            ("relative-dir", "some/dir/../zany", "some/zany/relative-dir"),
            ("../../some/dir/here", "foo/multi/wow", "foo/some/dir/here"),
            (":^relativepath.txt", "foo", "foo/relativepath.txt"),
            (":!/absolutepath.txt", "foo", "absolutepath.txt"),
            ("/", "foo", ""),
            (":/", "foo", ""),
            (":/bar/absolutepath.txt", "foo", "bar/absolutepath.txt"),
            (":/loc/*/MyProduct.*", "foo", "loc/*/MyProduct.*"),
            ("../**/generated?.cs", "foo/bar", "foo/**/generated?.cs"),
            (r".\dir\file.txt", "foo", "foo/dir/file.txt"),
            (r":!\absolute.txt", "foo", "absolute.txt"),
        ];
        for (path_spec, relative_to, expected) in cases {
            assert_eq!(
                expected,
                FilterPath::new(path_spec, relative_to)
                    .unwrap()
                    .repo_relative_path()
            );
        }
    }

    #[test]
    fn rejects_invalid_pathspecs() {
        for (path_spec, relative_to) in [
            ("", ""),
            (":?", ""),
            ("../foo.txt", ""),
            (".././a/../../foo.txt", "foo"),
            (":!", ""),
        ] {
            assert!(FilterPath::new(path_spec, relative_to).is_err());
        }
    }

    #[test]
    fn includes_and_excludes_paths() {
        let exclude = FilterPath::new(":^../bar", "foo").unwrap();
        assert!(exclude.is_exclude());
        assert!(exclude.excludes("bar", false));
        assert!(exclude.excludes("bar/somefile.txt", false));
        assert!(!exclude.excludes("barista", false));
        assert!(!exclude.includes("bar", false));

        let include = FilterPath::new("../root.txt", "foo").unwrap();
        assert!(include.is_include());
        assert!(include.includes("root.txt", false));
        assert!(include.includes("root.txt/child", false));
        assert!(!include.excludes("root.txt", false));
        assert!(
            FilterPath::new("/", "foo")
                .unwrap()
                .includes("anything", false)
        );
    }

    #[test]
    fn comparisons_honor_case_setting() {
        let exclude = FilterPath::new(":!RelativePath.txt", "foo").unwrap();
        assert!(exclude.excludes("foo/relativepath.txt", true));
        assert!(!exclude.excludes("foo/relativepath.txt", false));

        let wildcard = FilterPath::new(":/loc/*/MyProduct.*", "").unwrap();
        assert!(wildcard.includes("LOC/en/myproduct.resx", true));
        assert!(!wildcard.includes("LOC/en/myproduct.resx", false));
    }

    #[test]
    fn wildcard_dp_matches_segments_and_descendants() {
        let matching = [
            (":/loc/*/MyProduct.*", "loc/en/MyProduct.resx"),
            (":/loc/*/MyProduct.*", "loc/en/MyProduct."),
            (":/loc/?/MyProduct.*", "loc/e/MyProduct.resx"),
            (":/loc/**/MyProduct.*", "loc/MyProduct.resx"),
            (":/loc/**/MyProduct.*", "loc/en/subdir/MyProduct.resx"),
            (":/**/MyProduct.*", "MyProduct.resx"),
            (":/eng/*", "eng/product/src/file.cs"),
        ];
        for (path_spec, candidate) in matching {
            assert!(
                FilterPath::new(path_spec, "")
                    .unwrap()
                    .includes(candidate, false)
            );
        }

        let not_matching = [
            (":/loc/*/MyProduct.*", "loc/en/subdir/MyProduct.resx"),
            (":/loc/?/MyProduct.*", "loc/en/MyProduct.resx"),
            (":/loc/*/MyProduct.*", "loc/en/OtherProduct.resx"),
        ];
        for (path_spec, candidate) in not_matching {
            assert!(
                !FilterPath::new(path_spec, "")
                    .unwrap()
                    .includes(candidate, false)
            );
        }

        let exclude = FilterPath::new(":^/loc/**/generated?.cs", "").unwrap();
        assert!(exclude.excludes("loc/generated1.cs", false));
        assert!(exclude.excludes("loc/en/subdir/generatedA.cs", false));
        assert!(!exclude.excludes("loc/en/generated.cs", false));
    }

    #[test]
    fn wildcard_filter_may_include_children() {
        let filter = FilterPath::new(":/loc/*/MyProduct.*", "").unwrap();
        for candidate in ["loc", "loc/en", "loc/en/MyProduct.resources"] {
            assert!(filter.includes_children(candidate, false));
        }
        for candidate in ["localization", "docs"] {
            assert!(!filter.includes_children(candidate, false));
        }
        assert!(
            !FilterPath::new(":!/loc/*", "")
                .unwrap()
                .includes_children("loc", false)
        );
    }

    #[test]
    fn converts_to_pathspec() {
        let cases = [
            (":/abc/def", "", "/abc/def"),
            (":/abc/def", ".", "/abc/def"),
            ("abc", ".", "./abc"),
            (".", ".", "./"),
            ("./", "", "./"),
            ("abc/def", "./foo", "./abc/def"),
            (
                "../Directory.Build.props",
                "./foo",
                "../Directory.Build.props",
            ),
            (
                ":!/Directory.Build.props",
                "./foo",
                ":!/Directory.Build.props",
            ),
            (":!relative.txt", "./foo", ":!relative.txt"),
            (":/loc/*/MyProduct.*", "./foo", "/loc/*/MyProduct.*"),
            ("../**/generated?.cs", "./foo", "../**/generated?.cs"),
        ];
        for (path_spec, relative_to, expected) in cases {
            assert_eq!(
                expected,
                FilterPath::new(path_spec, relative_to)
                    .unwrap()
                    .to_path_spec(relative_to)
                    .unwrap()
            );
        }

        for relative_to in ["foo", "FOO"] {
            assert_eq!(
                "./bar",
                FilterPath::new("foo/bar", ".")
                    .unwrap()
                    .to_path_spec(relative_to)
                    .unwrap()
            );
        }
    }

    #[test]
    fn serde_uses_a_pathspec_string_and_round_trips() {
        for filter in [
            FilterPath::new(":/abc/*", "elsewhere").unwrap(),
            FilterPath::new(":!foo/?.txt", "").unwrap(),
            FilterPath::new("./foo", "").unwrap(),
        ] {
            let json = serde_json::to_string(&filter).unwrap();
            assert!(json.starts_with('"'));
            assert_eq!(filter, serde_json::from_str(&json).unwrap());
        }
    }
}
