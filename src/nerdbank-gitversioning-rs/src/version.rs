// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

use std::cmp::Ordering;
use std::fmt;
use std::str::FromStr;

use crate::{Error, Result};

/// A two-, three-, or four-part numeric version.
///
/// This type follows the important `System.Version` conventions used by
/// Nerdbank.GitVersioning: major and minor are always present, while build and
/// revision may be unspecified. An unspecified component compares less than
/// zero, just as the corresponding `System.Version` component (whose value is
/// `-1`) does.
#[derive(Clone, Copy, Debug, Eq, Hash, PartialEq)]
pub struct Version {
    /// The major version.
    pub major: u32,
    /// The minor version.
    pub minor: u32,
    /// The build component, or `None` when it is unspecified.
    pub build: Option<u32>,
    /// The revision component, or `None` when it is unspecified.
    pub revision: Option<u32>,
}

impl Version {
    /// Creates a version with major and minor components.
    pub const fn new(major: u32, minor: u32) -> Self {
        Self {
            major,
            minor,
            build: None,
            revision: None,
        }
    }

    /// Creates a version with major, minor, and build components.
    pub const fn new_with_build(major: u32, minor: u32, build: u32) -> Self {
        Self {
            major,
            minor,
            build: Some(build),
            revision: None,
        }
    }

    /// Creates a version with all four components.
    pub const fn new_with_revision(major: u32, minor: u32, build: u32, revision: u32) -> Self {
        Self {
            major,
            minor,
            build: Some(build),
            revision: Some(revision),
        }
    }

    /// Creates a version from component values using `-1` for unspecified
    /// build and revision components.
    pub fn from_system_version_parts(
        major: i32,
        minor: i32,
        build: i32,
        revision: i32,
    ) -> Result<Self> {
        if major < 0 || minor < 0 || build < -1 || revision < -1 || (build == -1 && revision != -1)
        {
            return Err(Error::InvalidFormat(
                "Version components must be non-negative, except that build and revision may be -1."
                    .to_owned(),
            ));
        }

        Ok(match (build, revision) {
            (-1, -1) => Self::new(major as u32, minor as u32),
            (build, -1) => Self::new_with_build(major as u32, minor as u32, build as u32),
            (build, revision) => {
                Self::new_with_revision(major as u32, minor as u32, build as u32, revision as u32)
            }
        })
    }

    /// Gets the build component using the `System.Version` value `-1` when it
    /// is unspecified.
    pub const fn build_or_unspecified(self) -> i64 {
        match self.build {
            Some(value) => value as i64,
            None => -1,
        }
    }

    /// Gets the revision component using the `System.Version` value `-1` when
    /// it is unspecified.
    pub const fn revision_or_unspecified(self) -> i64 {
        match self.revision {
            Some(value) => value as i64,
            None => -1,
        }
    }

    /// Returns a version where the specified number of components are
    /// guaranteed to be present. Applicable unspecified components become
    /// zero.
    ///
    /// `field_count` must be between zero and four.
    pub fn ensure_non_negative_components(self, field_count: usize) -> Result<Self> {
        if field_count > 4 {
            return Err(Error::InvalidOperation(
                "The field count must be between 0 and 4.".to_owned(),
            ));
        }

        let build = if field_count >= 3 {
            Some(self.build.unwrap_or(0))
        } else {
            self.build
        };
        let revision = if field_count >= 4 {
            Some(self.revision.unwrap_or(0))
        } else {
            self.revision
        };

        Ok(Self {
            build,
            revision,
            ..self
        })
    }

    /// Converts this version to a string containing exactly `field_count`
    /// components, replacing unspecified requested components with zero.
    ///
    /// `field_count` must be between zero and four.
    pub fn to_string_safe(self, field_count: usize) -> Result<String> {
        if field_count > 4 {
            return Err(Error::InvalidOperation(
                "The field count must be between 0 and 4.".to_owned(),
            ));
        }

        let version = self.ensure_non_negative_components(field_count)?;
        let mut fields = [
            version.major,
            version.minor,
            version.build.unwrap_or(0),
            version.revision.unwrap_or(0),
        ]
        .into_iter();
        Ok((0..field_count)
            .map(|_| fields.next().expect("field count is bounded").to_string())
            .collect::<Vec<_>>()
            .join("."))
    }

    /// Gets the number of specified components.
    pub const fn component_count(self) -> usize {
        if self.revision.is_some() {
            4
        } else if self.build.is_some() {
            3
        } else {
            2
        }
    }
}

impl Default for Version {
    fn default() -> Self {
        Self::new(0, 0)
    }
}

impl fmt::Display for Version {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(formatter, "{}.{}", self.major, self.minor)?;
        if let Some(build) = self.build {
            write!(formatter, ".{build}")?;
        }
        if let Some(revision) = self.revision {
            write!(formatter, ".{revision}")?;
        }

        Ok(())
    }
}

impl FromStr for Version {
    type Err = Error;

    fn from_str(value: &str) -> Result<Self> {
        let fields: Vec<_> = value.split('.').collect();
        if !(2..=4).contains(&fields.len())
            || fields
                .iter()
                .any(|field| field.is_empty() || !field.bytes().all(|byte| byte.is_ascii_digit()))
        {
            return Err(Error::InvalidFormat(format!(
                "'{value}' is not a two- to four-component version."
            )));
        }

        let parse = |field: &str| {
            field.parse::<i32>().map(|value| value as u32).map_err(|_| {
                Error::InvalidFormat(format!("'{value}' contains an invalid version component."))
            })
        };
        let major = parse(fields[0])?;
        let minor = parse(fields[1])?;
        Ok(match fields.len() {
            2 => Self::new(major, minor),
            3 => Self::new_with_build(major, minor, parse(fields[2])?),
            4 => Self::new_with_revision(major, minor, parse(fields[2])?, parse(fields[3])?),
            _ => unreachable!(),
        })
    }
}

impl Ord for Version {
    fn cmp(&self, other: &Self) -> Ordering {
        (
            self.major,
            self.minor,
            self.build_or_unspecified(),
            self.revision_or_unspecified(),
        )
            .cmp(&(
                other.major,
                other.minor,
                other.build_or_unspecified(),
                other.revision_or_unspecified(),
            ))
    }
}

impl PartialOrd for Version {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_and_displays_two_to_four_components() {
        for text in ["1.2", "1.2.3", "1.2.3.4"] {
            let version: Version = text.parse().unwrap();
            assert_eq!(text, version.to_string());
            assert_eq!(text.split('.').count(), version.component_count());
        }
    }

    #[test]
    fn rejects_invalid_versions() {
        for text in ["", "1", "1.", "1.2.", "1.2.3.4.5", "-1.2", "1. 2"] {
            assert!(text.parse::<Version>().is_err(), "{text}");
        }
    }

    #[test]
    fn unspecified_components_affect_equality_and_ordering() {
        assert_ne!(Version::new(1, 2), Version::new_with_build(1, 2, 0));
        assert!(Version::new(1, 2) < Version::new_with_build(1, 2, 0));
        assert!(Version::new_with_build(1, 2, 3) < Version::new_with_revision(1, 2, 3, 0));
    }

    #[test]
    fn safe_format_fills_unspecified_components() {
        let version = Version::new(1, 2);
        assert_eq!("", version.to_string_safe(0).unwrap());
        assert_eq!("1", version.to_string_safe(1).unwrap());
        assert_eq!("1.2", version.to_string_safe(2).unwrap());
        assert_eq!("1.2.0", version.to_string_safe(3).unwrap());
        assert_eq!("1.2.0.0", version.to_string_safe(4).unwrap());
        assert!(version.to_string_safe(5).is_err());
    }

    #[test]
    fn creates_from_system_version_parts() {
        assert_eq!(
            Version::new(1, 2),
            Version::from_system_version_parts(1, 2, -1, -1).unwrap()
        );
        assert!(Version::from_system_version_parts(1, 2, -1, 0).is_err());
    }
}
