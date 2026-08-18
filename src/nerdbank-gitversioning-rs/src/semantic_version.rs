// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

use std::fmt;
use std::str::FromStr;
use std::sync::LazyLock;

use regex::Regex;

use crate::{Error, ReleaseVersionIncrement, Result, Version};

static SEMANTIC_VERSION_PATTERN: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(
        r"(?ix)^v?(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)(?:\.(?<patch>0|[1-9][0-9]*)(?:\.(?<revision>0|[1-9][0-9]*))?)?(?<prerelease>-(?:[0-9a-z-]+|\{height\})(?:\.(?:[0-9a-z-]+|\{height\}))*)?(?<build_metadata>\+(?:[0-9a-z-]+|\{height\})(?:\.(?:[0-9a-z-]+|\{height\}))*)?$",
    )
    .expect("the semantic version pattern is valid")
});

static PRERELEASE_PATTERN: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r"(?i)^-(?:[0-9a-z-]+|\{height\})(?:\.(?:[0-9a-z-]+|\{height\}))*$")
        .expect("the prerelease pattern is valid")
});

static BUILD_METADATA_PATTERN: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r"(?i)^\+(?:[0-9a-z-]+|\{height\})(?:\.(?:[0-9a-z-]+|\{height\}))*$")
        .expect("the build metadata pattern is valid")
});

/// Identifies positions in a semantic version.
#[derive(Clone, Copy, Debug, Eq, Hash, Ord, PartialEq, PartialOrd)]
#[repr(u8)]
pub enum VersionPosition {
    /// The major numeric component.
    Major,
    /// The minor numeric component.
    Minor,
    /// The build numeric component.
    Build,
    /// The revision numeric component.
    Revision,
    /// The prerelease portion.
    Prerelease,
    /// The build metadata portion.
    BuildMetadata,
}

/// Describes a numeric version with optional prerelease and build metadata.
///
/// Unlike crates that accept only SemVer 2, this type intentionally supports
/// the two- to four-part version grammar used by Nerdbank.GitVersioning, along
/// with the `{height}` macro in prerelease and build metadata identifiers.
#[derive(Clone, Debug, Eq, Hash, PartialEq)]
pub struct SemanticVersion {
    /// The numeric portion of the version.
    pub version: Version,
    /// The prerelease portion, including its leading hyphen, or an empty string.
    pub prerelease: String,
    /// The build metadata, including its leading plus, or an empty string.
    pub build_metadata: String,
}

impl SemanticVersion {
    /// Initializes a semantic version, validating the prerelease and build
    /// metadata strings.
    pub fn new(
        version: Version,
        prerelease: impl Into<String>,
        build_metadata: impl Into<String>,
    ) -> Result<Self> {
        let prerelease = prerelease.into();
        let build_metadata = build_metadata.into();
        if !prerelease.is_empty() && !PRERELEASE_PATTERN.is_match(&prerelease) {
            return Err(Error::InvalidFormat(format!(
                "The prerelease '{prerelease}' is invalid."
            )));
        }
        if !build_metadata.is_empty() && !BUILD_METADATA_PATTERN.is_match(&build_metadata) {
            return Err(Error::InvalidFormat(format!(
                "The build metadata '{build_metadata}' is invalid."
            )));
        }

        Ok(Self {
            version,
            prerelease,
            build_metadata,
        })
    }

    /// Parses a semantic version, returning `None` when the input is invalid.
    pub fn try_parse(value: &str) -> Option<Self> {
        value.parse().ok()
    }

    /// Gets the position where version height should appear.
    pub fn version_height_position(&self) -> Option<VersionPosition> {
        if self.prerelease.contains("{height}") {
            Some(VersionPosition::Prerelease)
        } else if self.version.build.is_none() {
            Some(VersionPosition::Build)
        } else if self.version.revision.is_none() {
            Some(VersionPosition::Revision)
        } else {
            None
        }
    }

    /// Gets the position where the first 16 bits of a Git commit ID should
    /// appear, if any.
    pub fn git_commit_id_position(&self) -> Option<VersionPosition> {
        (self.version_height_position() == Some(VersionPosition::Build))
            .then_some(VersionPosition::Revision)
    }

    /// Reads one of the four numeric version positions, using `-1` for an
    /// unspecified build or revision.
    pub fn read_version_position(&self, position: VersionPosition) -> Result<i64> {
        Self::read_position(self.version, position)
    }

    /// Tests whether changing between two semantic versions resets version
    /// height at the specified position.
    pub fn will_version_change_reset_version_height(
        first: &Self,
        second: &Self,
        version_height_position: VersionPosition,
    ) -> Result<bool> {
        if first == second {
            return Ok(false);
        }
        if version_height_position == VersionPosition::Prerelease {
            return Ok(first != second);
        }
        if version_height_position > VersionPosition::Revision {
            return Err(Error::InvalidOperation(
                "Version height must occupy a numeric or prerelease position.".to_owned(),
            ));
        }

        for position in [
            VersionPosition::Major,
            VersionPosition::Minor,
            VersionPosition::Build,
            VersionPosition::Revision,
        ]
        .into_iter()
        .take(version_height_position as usize + 1)
        {
            if first.read_version_position(position)? != second.read_version_position(position)? {
                return Ok(true);
            }
        }

        Ok(false)
    }

    /// Checks whether a numeric version may have been produced by this
    /// semantic version specification.
    pub fn is_matching_version(&self, version: &Version) -> bool {
        let last_position = match self.version_height_position() {
            Some(position) if position <= VersionPosition::Revision => position as usize,
            _ => 4,
        };
        let positions = [
            VersionPosition::Major,
            VersionPosition::Minor,
            VersionPosition::Build,
            VersionPosition::Revision,
        ];

        positions[..last_position].iter().all(|&position| {
            self.read_version_position(position).ok()
                == Self::read_position(*version, position).ok()
        })
    }

    /// Checks whether a numeric version belongs to the set represented by this
    /// semantic version specification.
    pub fn contains(&self, version: &Version) -> bool {
        self.version.major == version.major
            && self.version.minor == version.minor
            && self
                .version
                .build
                .is_none_or(|build| version.build == Some(build))
            && self
                .version
                .revision
                .is_none_or(|revision| version.revision == Some(revision))
    }

    /// Gets a new semantic version with the specified numeric component
    /// incremented.
    ///
    /// Incrementing build is invalid when the build component is unspecified.
    /// Components after the incremented component are reset to zero while the
    /// original component count, prerelease, and build metadata are preserved.
    pub fn increment(&self, increment: ReleaseVersionIncrement) -> Result<Self> {
        if increment == ReleaseVersionIncrement::Build && self.version.build.is_none() {
            return Err(Error::InvalidOperation(format!(
                "Cannot apply build increment to version '{self}'."
            )));
        }

        let mut major = self.version.major;
        let mut minor = self.version.minor;
        let mut build = self.version.build.unwrap_or(0);
        match increment {
            ReleaseVersionIncrement::Major => {
                major = major.checked_add(1).ok_or_else(|| {
                    Error::InvalidOperation("The major version cannot be incremented.".to_owned())
                })?;
                minor = 0;
                build = 0;
            }
            ReleaseVersionIncrement::Minor => {
                minor = minor.checked_add(1).ok_or_else(|| {
                    Error::InvalidOperation("The minor version cannot be incremented.".to_owned())
                })?;
                build = 0;
            }
            ReleaseVersionIncrement::Build => {
                build = build.checked_add(1).ok_or_else(|| {
                    Error::InvalidOperation("The build version cannot be incremented.".to_owned())
                })?;
            }
        }

        let version = if self.version.build.is_none() {
            Version::new(major, minor)
        } else if self.version.revision.unwrap_or(0) > 0 {
            Version::new_with_revision(major, minor, build, 0)
        } else {
            Version::new_with_build(major, minor, build)
        };
        Self::new(
            version,
            self.prerelease.clone(),
            self.build_metadata.clone(),
        )
    }

    /// Sets the first prerelease identifier. The leading hyphen may be
    /// specified or omitted. An empty value removes the first identifier.
    pub fn set_first_prerelease_tag(&self, new_first_tag: &str) -> Result<Self> {
        let suffix = self
            .prerelease
            .find('.')
            .map_or("", |index| &self.prerelease[index..]);
        let mut prerelease = if self.prerelease.is_empty() {
            new_first_tag.to_owned()
        } else {
            format!("{new_first_tag}{suffix}")
        };
        if !prerelease.is_empty() && !prerelease.starts_with('-') {
            prerelease.insert(0, '-');
        }
        Self::new(self.version, prerelease, self.build_metadata.clone())
    }

    /// Returns a copy without any prerelease identifiers.
    pub fn without_prerelease_tags(&self) -> Self {
        Self {
            version: self.version,
            prerelease: String::new(),
            build_metadata: self.build_metadata.clone(),
        }
    }

    /// Converts a SemVer 2 prerelease such as `-beta.5` to a SemVer 1
    /// compatible value such as `-beta-0005`.
    ///
    /// Numeric identifiers are left-padded to at least `padding_size` digits,
    /// and dots are replaced by hyphens.
    pub fn make_prerelease_semver1_compliant(prerelease: &str, padding_size: usize) -> String {
        if prerelease.is_empty() {
            return String::new();
        }

        let body = prerelease.strip_prefix('-').unwrap_or(prerelease);
        let identifiers = body.split('.').map(|identifier| {
            if identifier.bytes().all(|byte| byte.is_ascii_digit()) {
                let normalized = identifier.trim_start_matches('0');
                let normalized = if normalized.is_empty() {
                    "0"
                } else {
                    normalized
                };
                format!("{normalized:0>padding_size$}")
            } else {
                identifier.to_owned()
            }
        });
        format!("-{}", identifiers.collect::<Vec<_>>().join("-"))
    }

    fn read_position(version: Version, position: VersionPosition) -> Result<i64> {
        match position {
            VersionPosition::Major => Ok(version.major as i64),
            VersionPosition::Minor => Ok(version.minor as i64),
            VersionPosition::Build => Ok(version.build_or_unspecified()),
            VersionPosition::Revision => Ok(version.revision_or_unspecified()),
            _ => Err(Error::InvalidOperation(
                "The position must be one of the four numeric parts.".to_owned(),
            )),
        }
    }
}

impl fmt::Display for SemanticVersion {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(
            formatter,
            "{}{}{}",
            self.version, self.prerelease, self.build_metadata
        )
    }
}

impl FromStr for SemanticVersion {
    type Err = Error;

    fn from_str(value: &str) -> Result<Self> {
        let captures = SEMANTIC_VERSION_PATTERN.captures(value).ok_or_else(|| {
            Error::InvalidFormat(format!(
                "Unrecognized or unsupported semantic version '{value}'."
            ))
        })?;
        let parse_number = |name: &str| {
            captures[name]
                .parse::<i32>()
                .map(|value| value as u32)
                .map_err(|_| {
                    Error::InvalidFormat(format!(
                        "'{value}' contains an invalid version component."
                    ))
                })
        };
        let major = parse_number("major")?;
        let minor = parse_number("minor")?;
        let version = match (captures.name("patch"), captures.name("revision")) {
            (None, None) => Version::new(major, minor),
            (Some(patch), None) => Version::new_with_build(
                major,
                minor,
                patch
                    .as_str()
                    .parse::<i32>()
                    .map(|value| value as u32)
                    .map_err(|_| {
                        Error::InvalidFormat(format!(
                            "'{value}' contains an invalid version component."
                        ))
                    })?,
            ),
            (Some(patch), Some(revision)) => Version::new_with_revision(
                major,
                minor,
                patch
                    .as_str()
                    .parse::<i32>()
                    .map(|value| value as u32)
                    .map_err(|_| {
                        Error::InvalidFormat(format!(
                            "'{value}' contains an invalid version component."
                        ))
                    })?,
                revision
                    .as_str()
                    .parse::<i32>()
                    .map(|value| value as u32)
                    .map_err(|_| {
                        Error::InvalidFormat(format!(
                            "'{value}' contains an invalid version component."
                        ))
                    })?,
            ),
            (None, Some(_)) => unreachable!("the regex requires patch before revision"),
        };
        Self::new(
            version,
            captures
                .name("prerelease")
                .map_or("", |capture| capture.as_str()),
            captures
                .name("build_metadata")
                .map_or("", |capture| capture.as_str()),
        )
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_full_supported_grammar() {
        let parsed: SemanticVersion = "v1.2.3.4-pre.5.{height}+build-metadata.id1"
            .parse()
            .unwrap();
        assert_eq!(Version::new_with_revision(1, 2, 3, 4), parsed.version);
        assert_eq!("-pre.5.{height}", parsed.prerelease);
        assert_eq!("+build-metadata.id1", parsed.build_metadata);
        assert_eq!(
            Some(VersionPosition::Prerelease),
            parsed.version_height_position()
        );
        assert_eq!(
            "1.2.3.4-pre.5.{height}+build-metadata.id1",
            parsed.to_string()
        );
    }

    #[test]
    fn rejects_bad_grammar_and_leading_zeroes() {
        for value in [
            "", "1", "01.2", "1.02", "1.2.03", "1.2-$", "1.2-", "1.2-a.", "1.2+$", "1.2+", "1.2+a.",
        ] {
            assert!(SemanticVersion::try_parse(value).is_none(), "{value}");
        }
    }

    #[test]
    fn constructors_validate_suffixes() {
        assert!(SemanticVersion::new(Version::new(1, 2), "-pre", "+build").is_ok());
        assert!(SemanticVersion::new(Version::new(1, 2), "pre", "").is_err());
        assert!(SemanticVersion::new(Version::new(1, 2), "", "build").is_err());
    }

    #[test]
    fn computes_height_and_commit_positions() {
        let two: SemanticVersion = "1.2".parse().unwrap();
        let three: SemanticVersion = "1.2.3".parse().unwrap();
        let four: SemanticVersion = "1.2.3.4".parse().unwrap();
        assert_eq!(Some(VersionPosition::Build), two.version_height_position());
        assert_eq!(
            Some(VersionPosition::Revision),
            two.git_commit_id_position()
        );
        assert_eq!(
            Some(VersionPosition::Revision),
            three.version_height_position()
        );
        assert_eq!(None, four.version_height_position());
    }

    #[test]
    fn checks_reset_matching_and_containment() {
        let spec: SemanticVersion = "1.2".parse().unwrap();
        assert!(spec.is_matching_version(&Version::new_with_revision(1, 2, 15, 42)));
        assert!(!spec.is_matching_version(&Version::new_with_build(2, 2, 15)));
        assert!(spec.contains(&Version::new_with_revision(1, 2, 15, 42)));

        let first: SemanticVersion = "1.2.3".parse().unwrap();
        let second: SemanticVersion = "1.2.4".parse().unwrap();
        assert!(
            SemanticVersion::will_version_change_reset_version_height(
                &first,
                &second,
                VersionPosition::Revision
            )
            .unwrap()
        );
        assert!(
            SemanticVersion::will_version_change_reset_version_height(
                &first,
                &second,
                VersionPosition::Build
            )
            .unwrap()
        );
    }

    #[test]
    fn edits_and_normalizes_prerelease() {
        let version: SemanticVersion = "1.2-alpha.preview+metadata".parse().unwrap();
        assert_eq!(
            "1.2-beta.preview+metadata",
            version
                .set_first_prerelease_tag("beta")
                .unwrap()
                .to_string()
        );
        assert_eq!(
            "1.2+metadata",
            version.without_prerelease_tags().to_string()
        );
        assert_eq!(
            "-beta-0005-0001-foo5",
            SemanticVersion::make_prerelease_semver1_compliant("-beta.5.00001.foo5", 4)
        );
    }

    #[test]
    fn increments_and_preserves_suffixes_and_precision() {
        let two: SemanticVersion = "1.2-tag+metadata".parse().unwrap();
        assert_eq!(
            "2.0-tag+metadata",
            two.increment(ReleaseVersionIncrement::Major)
                .unwrap()
                .to_string()
        );
        assert_eq!(
            "1.3-tag+metadata",
            two.increment(ReleaseVersionIncrement::Minor)
                .unwrap()
                .to_string()
        );
        assert!(two.increment(ReleaseVersionIncrement::Build).is_err());

        let four: SemanticVersion = "1.2.3.4-tag".parse().unwrap();
        assert_eq!(
            "1.2.4.0-tag",
            four.increment(ReleaseVersionIncrement::Build)
                .unwrap()
                .to_string()
        );
    }

    #[test]
    fn managed_increment_and_first_tag_compatibility_vectors() {
        for (input, increment, expected) in [
            ("1.0", ReleaseVersionIncrement::Minor, "1.1"),
            ("1.1", ReleaseVersionIncrement::Major, "2.0"),
            ("1.2.3", ReleaseVersionIncrement::Minor, "1.3.0"),
            ("1.2.3", ReleaseVersionIncrement::Major, "2.0.0"),
            ("1.2.3", ReleaseVersionIncrement::Build, "1.2.4"),
            ("1.2.3.4", ReleaseVersionIncrement::Minor, "1.3.0.0"),
            ("1.2.3.4", ReleaseVersionIncrement::Major, "2.0.0.0"),
            ("1.2.3.4", ReleaseVersionIncrement::Build, "1.2.4.0"),
            (
                "1.2.3-tag+metadata",
                ReleaseVersionIncrement::Build,
                "1.2.4-tag+metadata",
            ),
        ] {
            let actual = input
                .parse::<SemanticVersion>()
                .unwrap()
                .increment(increment)
                .unwrap();
            assert_eq!(expected, actual.to_string(), "{input}");
        }

        for (input, tag, expected) in [
            ("1.2", "pre", "1.2-pre"),
            ("1.2+build", "-pre", "1.2-pre+build"),
            ("1.2-alpha", "beta", "1.2-beta"),
            ("1.2-alpha.preview", "-beta", "1.2-beta.preview"),
            (
                "1.2-alpha.preview+metadata",
                "beta",
                "1.2-beta.preview+metadata",
            ),
            ("1.2-alpha.{height}", "beta", "1.2-beta.{height}"),
            ("1.2-pre", "", "1.2"),
        ] {
            let actual = input
                .parse::<SemanticVersion>()
                .unwrap()
                .set_first_prerelease_tag(tag)
                .unwrap();
            assert_eq!(expected, actual.to_string(), "{input}");
        }
    }
}
