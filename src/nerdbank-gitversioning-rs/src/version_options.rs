// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

use std::fmt;
use std::str::FromStr;

use serde::de::{self, MapAccess, Visitor};
use serde::ser::SerializeStruct;
use serde::{Deserialize, Deserializer, Serialize, Serializer};

use crate::{Error, FilterPath, Result as CrateResult, SemanticVersion, Version, VersionPosition};

/// The default last component controlled in an assembly version.
pub const DEFAULT_VERSION_PRECISION: VersionPrecision = VersionPrecision::Minor;

/// The placeholder that identifies where version height appears in a semantic version.
pub const VERSION_HEIGHT_PLACEHOLDER: &str = "{height}";

/// The default fixed length of an abbreviated Git commit ID.
pub const DEFAULT_GIT_COMMIT_ID_SHORT_FIXED_LENGTH: u32 = 10;

const DEFAULT_SEMVER1_NUMERIC_IDENTIFIER_PADDING: u32 = 4;
const VERSION_SCHEMA_URL: &str = "https://raw.githubusercontent.com/dotnet/Nerdbank.GitVersioning/main/src/NerdBank.GitVersioning/version.schema.json";

/// The last component to control in a four-integer version.
#[derive(Clone, Copy, Debug, Default, Deserialize, Hash, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum VersionPrecision {
    /// The first integer is the last number set; the rest are zero.
    Major,
    /// The second integer is the last number set; the rest are zero.
    #[default]
    Minor,
    /// The third integer is the last number set; the fourth is zero.
    Build,
    /// All four integers are set.
    Revision,
}

/// The conditions under which a commit ID is included in a cloud build number.
#[derive(Clone, Copy, Debug, Default, Deserialize, Hash, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum CloudBuildNumberCommitWhen {
    /// Always include commit information.
    Always,
    /// Include commit information only for non-public releases.
    #[default]
    NonPublicReleaseOnly,
    /// Never include commit information.
    Never,
}

/// The position at which a commit ID appears in a cloud build number.
#[derive(Clone, Copy, Debug, Default, Deserialize, Hash, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum CloudBuildNumberCommitWhere {
    /// Put the commit ID in build metadata, for example `+ga1b2c3`.
    #[default]
    BuildMetadata,
    /// Use the first 15 bits of the commit ID as the fourth version component.
    FourthVersionComponent,
}

/// The component incremented after creating a release branch.
#[derive(Clone, Copy, Debug, Default, Deserialize, Hash, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum ReleaseVersionIncrement {
    /// Increment the major version.
    Major,
    /// Increment the minor version.
    #[default]
    Minor,
    /// Increment the build component.
    Build,
}

/// Describes the versions and policies used by a build.
///
/// Optional fields deliberately retain the distinction between an omitted value and an
/// explicitly configured default. This is required when an inheriting `version.json` is merged
/// over an ancestor's options.
#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct VersionOptions {
    /// The schema URI written to a `version.json` file.
    #[serde(rename = "$schema", default, skip_serializing_if = "Option::is_none")]
    pub schema: Option<String>,

    /// The version used as the basis for version calculations.
    #[serde(
        default,
        skip_serializing_if = "Option::is_none",
        deserialize_with = "deserialize_optional_from_string",
        serialize_with = "serialize_optional_as_string"
    )]
    pub version: Option<SemanticVersion>,

    /// The version used for an assembly version instead of `version`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub assembly_version: Option<AssemblyVersionOptions>,

    /// The prefix for Git commit IDs in non-public versions.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub git_commit_id_prefix: Option<String>,

    /// A number added to Git height when calculating version height.
    ///
    /// `buildNumberOffset`, used by older files, is accepted as an alias.
    #[serde(
        default,
        alias = "buildNumberOffset",
        skip_serializing_if = "Option::is_none"
    )]
    pub version_height_offset: Option<i32>,

    /// The version to which `versionHeightOffset` applies.
    #[serde(
        default,
        skip_serializing_if = "Option::is_none",
        deserialize_with = "deserialize_optional_from_string",
        serialize_with = "serialize_optional_as_string"
    )]
    pub version_height_offset_applies_to: Option<SemanticVersion>,

    /// The minimum number of digits used for numeric SemVer 1 identifiers.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub sem_ver1_numeric_identifier_padding: Option<u32>,

    /// The fixed number of characters used for abbreviated Git commit IDs.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub git_commit_id_short_fixed_length: Option<u32>,

    /// The minimum length used when Git chooses an unambiguous abbreviated commit ID.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub git_commit_id_short_auto_minimum: Option<u32>,

    /// Whether dirty Git commit IDs are suffixed with `-dirty`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub git_commit_id_include_dirty: Option<bool>,

    /// Options controlling generated NuGet package versions.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub nuget_package_version: Option<NuGetPackageVersionOptions>,

    /// Regular expressions for refs that should default to public releases.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub public_release_ref_spec: Option<Vec<String>>,

    /// Options applicable specifically to cloud builds.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub cloud_build: Option<CloudBuildOptions>,

    /// Settings for release preparation and tagging.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub release: Option<ReleaseOptions>,

    /// Pathspec-like filters used when calculating version height.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub path_filters: Option<Vec<FilterPath>>,

    /// Whether unset values should be inherited from an ancestor `version.json`.
    #[serde(default, skip_serializing_if = "is_false")]
    pub inherit: bool,

    /// A prerelease tag to apply to an inherited version.
    ///
    /// An empty string explicitly suppresses an inherited prerelease tag, while `None` leaves it
    /// unchanged.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub prerelease: Option<String>,
}

impl VersionOptions {
    /// The default last component controlled in an assembly version.
    pub const DEFAULT_VERSION_PRECISION: VersionPrecision = DEFAULT_VERSION_PRECISION;

    /// The placeholder that identifies where version height appears in a semantic version.
    pub const VERSION_HEIGHT_PLACEHOLDER: &'static str = VERSION_HEIGHT_PLACEHOLDER;

    /// The default fixed length of an abbreviated Git commit ID.
    pub const DEFAULT_GIT_COMMIT_ID_SHORT_FIXED_LENGTH: u32 =
        DEFAULT_GIT_COMMIT_ID_SHORT_FIXED_LENGTH;

    /// Creates options with the specified version and optional prerelease tag.
    ///
    /// The prerelease tag follows `SemanticVersion` conventions and includes its leading `-`.
    pub fn from_version(version: Version, prerelease: Option<&str>) -> CrateResult<Self> {
        Ok(Self {
            version: Some(SemanticVersion::new(
                version,
                prerelease.unwrap_or_default(),
                "",
            )?),
            ..Self::default()
        })
    }

    /// Deserializes options while resolving relative path filters against a repository directory.
    ///
    /// Use this instead of plain `serde_json::from_str` when the `version.json` file is below the
    /// repository root.
    pub fn from_json(json: &str, repo_relative_base_directory: &str) -> CrateResult<Self> {
        let mut value: serde_json::Value = serde_json::from_str(json)?;
        let path_filters = value
            .as_object_mut()
            .and_then(|object| object.remove("pathFilters"));
        let mut options: Self = serde_json::from_value(value)?;
        if let Some(path_filters) = path_filters {
            let serde_json::Value::Array(path_filters) = path_filters else {
                return Err(Error::InvalidFormat(
                    "'pathFilters' must be an array of strings.".to_owned(),
                ));
            };
            options.path_filters = Some(
                path_filters
                    .into_iter()
                    .map(|path_filter| {
                        let serde_json::Value::String(path_filter) = path_filter else {
                            return Err(Error::InvalidFormat(
                                "Each path filter must be a string.".to_owned(),
                            ));
                        };
                        FilterPath::new(&path_filter, repo_relative_base_directory)
                    })
                    .collect::<CrateResult<Vec<_>>>()?,
            );
        }
        Ok(options)
    }

    /// Serializes options with path filters relative to a repository directory.
    pub fn to_json(&self, repo_relative_base_directory: &str) -> CrateResult<String> {
        let mut without_path_filters = self.clone();
        without_path_filters.path_filters = None;
        let mut value = serde_json::to_value(without_path_filters)?;
        if let Some(path_filters) = &self.path_filters {
            let path_filters = path_filters
                .iter()
                .map(|filter| {
                    filter
                        .to_path_spec(repo_relative_base_directory)
                        .map(serde_json::Value::String)
                })
                .collect::<CrateResult<Vec<_>>>()?;
            let Some(object) = value.as_object_mut() else {
                return Err(Error::InvalidOperation(
                    "VersionOptions did not serialize as an object.".to_owned(),
                ));
            };
            object.insert(
                "pathFilters".to_owned(),
                serde_json::Value::Array(path_filters),
            );
        }
        Ok(serde_json::to_string_pretty(&value)?)
    }

    /// Returns the schema URI used by Nerdbank.GitVersioning.
    #[must_use]
    pub const fn schema_url() -> &'static str {
        VERSION_SCHEMA_URL
    }

    /// Returns the configured assembly-version options or their defaults.
    #[must_use]
    pub fn assembly_version_or_default(&self) -> AssemblyVersionOptions {
        self.assembly_version.clone().unwrap_or_default()
    }

    /// Returns the configured version-height offset, defaulting to zero.
    #[must_use]
    pub fn version_height_offset_or_default(&self) -> i32 {
        self.version_height_offset.unwrap_or(0)
    }

    /// Returns the effective version-height offset.
    ///
    /// The configured offset becomes ineffective when the version has changed in a component
    /// that resets height relative to `versionHeightOffsetAppliesTo`.
    #[must_use]
    pub fn effective_version_height_offset(&self) -> i32 {
        if let (Some(applies_to), Some(version), Some(position)) = (
            &self.version_height_offset_applies_to,
            &self.version,
            self.version_height_position(),
        ) && SemanticVersion::will_version_change_reset_version_height(
            applies_to, version, position,
        )
        .unwrap_or(true)
        {
            return 0;
        }

        self.version_height_offset_or_default()
    }

    /// Returns the position in a computed version where version height should appear.
    #[must_use]
    pub fn version_height_position(&self) -> Option<VersionPosition> {
        self.version
            .as_ref()
            .and_then(SemanticVersion::version_height_position)
    }

    /// Returns where commit-ID bits should appear in the numeric version, if anywhere.
    #[must_use]
    pub fn git_commit_id_position(&self) -> Option<VersionPosition> {
        self.version
            .as_ref()
            .and_then(SemanticVersion::git_commit_id_position)
    }

    /// Returns the minimum SemVer 1 numeric identifier width.
    #[must_use]
    pub fn sem_ver1_numeric_identifier_padding_or_default(&self) -> u32 {
        self.sem_ver1_numeric_identifier_padding
            .unwrap_or(DEFAULT_SEMVER1_NUMERIC_IDENTIFIER_PADDING)
    }

    /// Returns the fixed abbreviated Git commit ID length.
    #[must_use]
    pub fn git_commit_id_short_fixed_length_or_default(&self) -> u32 {
        self.git_commit_id_short_fixed_length
            .unwrap_or(DEFAULT_GIT_COMMIT_ID_SHORT_FIXED_LENGTH)
    }

    /// Returns the automatic abbreviated Git commit ID minimum, defaulting to zero.
    #[must_use]
    pub fn git_commit_id_short_auto_minimum_or_default(&self) -> u32 {
        self.git_commit_id_short_auto_minimum.unwrap_or(0)
    }

    /// Returns whether dirty Git commit IDs are marked, defaulting to `false`.
    #[must_use]
    pub fn git_commit_id_include_dirty_or_default(&self) -> bool {
        self.git_commit_id_include_dirty.unwrap_or(false)
    }

    /// Returns the configured NuGet package options or their defaults.
    #[must_use]
    pub fn nuget_package_version_or_default(&self) -> NuGetPackageVersionOptions {
        self.nuget_package_version.clone().unwrap_or_default()
    }

    /// Returns the configured cloud-build options or their defaults.
    #[must_use]
    pub fn cloud_build_or_default(&self) -> CloudBuildOptions {
        self.cloud_build.clone().unwrap_or_default()
    }

    /// Returns the configured release options or their defaults.
    #[must_use]
    pub fn release_or_default(&self) -> ReleaseOptions {
        self.release.clone().unwrap_or_default()
    }

    /// Returns the public-release ref specifications, or an empty slice when none were configured.
    #[must_use]
    pub fn public_release_ref_spec_or_default(&self) -> &[String] {
        self.public_release_ref_spec.as_deref().unwrap_or_default()
    }

    /// Overlays explicitly configured values from an inheriting file onto these options.
    ///
    /// This is the serde equivalent of populating an existing options object in the .NET
    /// implementation. An absent optional value leaves the inherited value unchanged.
    pub fn merge_from(&mut self, overlay: &Self) {
        macro_rules! replace_some {
            ($field:ident) => {
                if overlay.$field.is_some() {
                    self.$field = overlay.$field.clone();
                }
            };
        }

        replace_some!(schema);
        replace_some!(version);
        replace_some!(assembly_version);
        replace_some!(git_commit_id_prefix);
        replace_some!(version_height_offset);
        replace_some!(version_height_offset_applies_to);
        replace_some!(sem_ver1_numeric_identifier_padding);
        replace_some!(git_commit_id_short_fixed_length);
        replace_some!(git_commit_id_short_auto_minimum);
        replace_some!(git_commit_id_include_dirty);
        replace_some!(nuget_package_version);
        replace_some!(public_release_ref_spec);
        replace_some!(cloud_build);
        replace_some!(release);
        replace_some!(path_filters);
        replace_some!(prerelease);
        self.inherit = overlay.inherit;
    }

    /// Merges an inheriting file and converts the result into complete options.
    ///
    /// In addition to overlaying explicitly configured values, this applies the inheriting file's
    /// standalone `prerelease` property and clears `inherit`.
    pub fn merge_inheriting(&mut self, overlay: &Self) -> CrateResult<()> {
        self.merge_from(overlay);
        self.apply_prerelease()?;
        self.inherit = false;
        Ok(())
    }

    /// Applies the standalone `prerelease` property to the semantic version.
    pub fn apply_prerelease(&mut self) -> CrateResult<()> {
        let Some(prerelease) = self.prerelease.clone() else {
            return Ok(());
        };
        let Some(version) = &self.version else {
            return Err(Error::InvalidOperation(
                "The 'prerelease' property cannot be used without a 'version' property.".to_owned(),
            ));
        };

        if prerelease.is_empty() {
            if !version.prerelease.is_empty() {
                self.version = Some(SemanticVersion::new(
                    version.version,
                    "",
                    version.build_metadata.clone(),
                )?);
            }
            self.prerelease = None;
            return Ok(());
        }
        if !version.prerelease.is_empty() {
            return Err(Error::InvalidOperation(
                "The 'prerelease' property cannot be used when the 'version' property already includes a prerelease tag.".to_owned(),
            ));
        }

        let prerelease = if prerelease.starts_with('-') {
            prerelease
        } else {
            format!("-{prerelease}")
        };
        self.version = Some(SemanticVersion::new(
            version.version,
            prerelease,
            version.build_metadata.clone(),
        )?);
        self.prerelease = None;
        Ok(())
    }
}

impl PartialEq for VersionOptions {
    fn eq(&self, other: &Self) -> bool {
        self.schema == other.schema
            && self.version == other.version
            && self.assembly_version_or_default() == other.assembly_version_or_default()
            && self.git_commit_id_prefix == other.git_commit_id_prefix
            && self.version_height_offset == other.version_height_offset
            && self.version_height_offset_applies_to == other.version_height_offset_applies_to
            && self.sem_ver1_numeric_identifier_padding == other.sem_ver1_numeric_identifier_padding
            && self.git_commit_id_short_fixed_length == other.git_commit_id_short_fixed_length
            && self.git_commit_id_short_auto_minimum == other.git_commit_id_short_auto_minimum
            && self.git_commit_id_include_dirty == other.git_commit_id_include_dirty
            && self.nuget_package_version_or_default() == other.nuget_package_version_or_default()
            && self.public_release_ref_spec == other.public_release_ref_spec
            && self.cloud_build_or_default() == other.cloud_build_or_default()
            && self.release_or_default() == other.release_or_default()
            && self.path_filters == other.path_filters
            && self.inherit == other.inherit
            && self.prerelease == other.prerelease
    }
}

/// Details of how an assembly version is calculated.
#[derive(Clone, Debug, Default)]
pub struct AssemblyVersionOptions {
    /// The explicit assembly version, with two to four components.
    pub version: Option<Version>,
    /// The additional precision copied from the calculated file version.
    pub precision: Option<VersionPrecision>,
}

impl AssemblyVersionOptions {
    /// Creates options with an explicit assembly version.
    #[must_use]
    pub const fn new(version: Version) -> Self {
        Self {
            version: Some(version),
            precision: None,
        }
    }

    /// Returns the configured precision, defaulting to `minor`.
    #[must_use]
    pub fn precision_or_default(&self) -> VersionPrecision {
        self.precision.unwrap_or(DEFAULT_VERSION_PRECISION)
    }
}

impl PartialEq for AssemblyVersionOptions {
    fn eq(&self, other: &Self) -> bool {
        self.version == other.version && self.precision_or_default() == other.precision_or_default()
    }
}

impl Eq for AssemblyVersionOptions {}

impl Serialize for AssemblyVersionOptions {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        if self.precision_or_default() == DEFAULT_VERSION_PRECISION
            && let Some(version) = &self.version
        {
            return serializer.serialize_str(&version.to_string());
        }

        let field_count =
            usize::from(self.version.is_some()) + usize::from(self.precision.is_some());
        let mut state = serializer.serialize_struct("AssemblyVersionOptions", field_count)?;
        if let Some(version) = &self.version {
            state.serialize_field("version", &AsString(version))?;
        }
        if let Some(precision) = self.precision {
            state.serialize_field("precision", &precision)?;
        }
        state.end()
    }
}

impl<'de> Deserialize<'de> for AssemblyVersionOptions {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: Deserializer<'de>,
    {
        struct AssemblyVersionVisitor;

        impl<'de> Visitor<'de> for AssemblyVersionVisitor {
            type Value = AssemblyVersionOptions;

            fn expecting(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
                formatter.write_str("an assembly version string or assemblyVersion object")
            }

            fn visit_str<E>(self, value: &str) -> Result<Self::Value, E>
            where
                E: de::Error,
            {
                let version = Version::from_str(value).map_err(E::custom)?;
                Ok(AssemblyVersionOptions::new(version))
            }

            fn visit_map<M>(self, mut map: M) -> Result<Self::Value, M::Error>
            where
                M: MapAccess<'de>,
            {
                let mut version = None;
                let mut precision = None;
                while let Some(key) = map.next_key::<String>()? {
                    match key.as_str() {
                        "version" => {
                            if version.is_some() {
                                return Err(de::Error::duplicate_field("version"));
                            }
                            let text = map.next_value::<String>()?;
                            version = Some(Version::from_str(&text).map_err(de::Error::custom)?);
                        }
                        "precision" => {
                            if precision.is_some() {
                                return Err(de::Error::duplicate_field("precision"));
                            }
                            precision = Some(map.next_value()?);
                        }
                        _ => return Err(de::Error::unknown_field(&key, &["version", "precision"])),
                    }
                }

                Ok(AssemblyVersionOptions { version, precision })
            }
        }

        deserializer.deserialize_any(AssemblyVersionVisitor)
    }
}

/// Settings controlling generated NuGet package versions.
#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct NuGetPackageVersionOptions {
    /// The SemVer generation mode, normally `1` or `2`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub sem_ver: Option<f32>,
    /// The number of version components included in the package version.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub precision: Option<VersionPrecision>,
}

impl NuGetPackageVersionOptions {
    /// The default number of components included in a package version.
    pub const DEFAULT_PRECISION: VersionPrecision = VersionPrecision::Build;

    /// Returns the configured SemVer mode, defaulting to `1`.
    #[must_use]
    pub fn sem_ver_or_default(&self) -> f32 {
        self.sem_ver.unwrap_or(1.0)
    }

    /// Returns the configured precision, defaulting to `build`.
    #[must_use]
    pub fn precision_or_default(&self) -> VersionPrecision {
        self.precision.unwrap_or(Self::DEFAULT_PRECISION)
    }
}

impl PartialEq for NuGetPackageVersionOptions {
    fn eq(&self, other: &Self) -> bool {
        self.sem_ver_or_default() == other.sem_ver_or_default()
            && self.precision_or_default() == other.precision_or_default()
    }
}

/// Options applicable specifically to cloud builds.
#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CloudBuildOptions {
    /// Whether all build properties are elevated to `NBGV_` cloud variables.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub set_all_variables: Option<bool>,
    /// Whether selected calculated version properties are elevated to cloud variables.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub set_version_variables: Option<bool>,
    /// Settings for overriding the cloud build number.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub build_number: Option<CloudBuildNumberOptions>,
}

impl CloudBuildOptions {
    /// Returns whether all build properties are elevated, defaulting to `false`.
    #[must_use]
    pub fn set_all_variables_or_default(&self) -> bool {
        self.set_all_variables.unwrap_or(false)
    }

    /// Returns whether version properties are elevated, defaulting to `true`.
    #[must_use]
    pub fn set_version_variables_or_default(&self) -> bool {
        self.set_version_variables.unwrap_or(true)
    }

    /// Returns build-number settings or their defaults.
    #[must_use]
    pub fn build_number_or_default(&self) -> CloudBuildNumberOptions {
        self.build_number.clone().unwrap_or_default()
    }
}

impl PartialEq for CloudBuildOptions {
    fn eq(&self, other: &Self) -> bool {
        self.set_all_variables_or_default() == other.set_all_variables_or_default()
            && self.set_version_variables_or_default() == other.set_version_variables_or_default()
            && self.build_number_or_default() == other.build_number_or_default()
    }
}

impl Eq for CloudBuildOptions {}

/// Settings for overriding a cloud build number with version information.
#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CloudBuildNumberOptions {
    /// Whether to override the preset cloud build number.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub enabled: Option<bool>,
    /// When and where commit information is included.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub include_commit_id: Option<CloudBuildNumberCommitIdOptions>,
}

impl CloudBuildNumberOptions {
    /// Returns whether build-number overriding is enabled, defaulting to `false`.
    #[must_use]
    pub fn enabled_or_default(&self) -> bool {
        self.enabled.unwrap_or(false)
    }

    /// Returns commit-ID settings or their defaults.
    #[must_use]
    pub fn include_commit_id_or_default(&self) -> CloudBuildNumberCommitIdOptions {
        self.include_commit_id.clone().unwrap_or_default()
    }
}

impl PartialEq for CloudBuildNumberOptions {
    fn eq(&self, other: &Self) -> bool {
        self.enabled_or_default() == other.enabled_or_default()
            && self.include_commit_id_or_default() == other.include_commit_id_or_default()
    }
}

impl Eq for CloudBuildNumberOptions {}

/// Describes when and where commit information appears in a cloud build number.
#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct CloudBuildNumberCommitIdOptions {
    /// The conditions under which the commit ID is included.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub when: Option<CloudBuildNumberCommitWhen>,
    /// The position at which the commit ID is included.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub r#where: Option<CloudBuildNumberCommitWhere>,
}

impl CloudBuildNumberCommitIdOptions {
    /// Returns the inclusion condition, defaulting to non-public releases only.
    #[must_use]
    pub fn when_or_default(&self) -> CloudBuildNumberCommitWhen {
        self.when.unwrap_or_default()
    }

    /// Returns the inclusion position, defaulting to build metadata.
    #[must_use]
    pub fn where_or_default(&self) -> CloudBuildNumberCommitWhere {
        self.r#where.unwrap_or_default()
    }
}

impl PartialEq for CloudBuildNumberCommitIdOptions {
    fn eq(&self, other: &Self) -> bool {
        self.when_or_default() == other.when_or_default()
            && self.where_or_default() == other.where_or_default()
    }
}

impl Eq for CloudBuildNumberCommitIdOptions {}

/// Settings for the `prepare-release` and `tag` commands.
#[derive(Clone, Debug, Default, Deserialize, Serialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ReleaseOptions {
    /// The template for release tag names.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub tag_name: Option<String>,
    /// The template for release branch names.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub branch_name: Option<String>,
    /// The component incremented while preparing a release.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub version_increment: Option<ReleaseVersionIncrement>,
    /// The first/default prerelease tag for new versions.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub first_unstable_tag: Option<String>,
}

impl ReleaseOptions {
    /// Returns the tag-name template, defaulting to `v{version}`.
    #[must_use]
    pub fn tag_name_or_default(&self) -> &str {
        self.tag_name.as_deref().unwrap_or("v{version}")
    }

    /// Returns the branch-name template, defaulting to `v{version}`.
    #[must_use]
    pub fn branch_name_or_default(&self) -> &str {
        self.branch_name.as_deref().unwrap_or("v{version}")
    }

    /// Returns the release increment, defaulting to `minor`.
    #[must_use]
    pub fn version_increment_or_default(&self) -> ReleaseVersionIncrement {
        self.version_increment.unwrap_or_default()
    }

    /// Returns the first unstable tag, defaulting to `alpha`.
    #[must_use]
    pub fn first_unstable_tag_or_default(&self) -> &str {
        self.first_unstable_tag.as_deref().unwrap_or("alpha")
    }
}

impl PartialEq for ReleaseOptions {
    fn eq(&self, other: &Self) -> bool {
        self.tag_name_or_default() == other.tag_name_or_default()
            && self.branch_name_or_default() == other.branch_name_or_default()
            && self.version_increment_or_default() == other.version_increment_or_default()
            && self.first_unstable_tag_or_default() == other.first_unstable_tag_or_default()
    }
}

impl Eq for ReleaseOptions {}

struct AsString<'a, T>(&'a T);

impl<T: fmt::Display> Serialize for AsString<'_, T> {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        serializer.serialize_str(&self.0.to_string())
    }
}

fn deserialize_optional_from_string<'de, D, T>(deserializer: D) -> Result<Option<T>, D::Error>
where
    D: Deserializer<'de>,
    T: FromStr,
    T::Err: fmt::Display,
{
    Option::<String>::deserialize(deserializer)?
        .map(|value| T::from_str(&value).map_err(de::Error::custom))
        .transpose()
}

fn serialize_optional_as_string<S, T>(value: &Option<T>, serializer: S) -> Result<S::Ok, S::Error>
where
    S: Serializer,
    T: fmt::Display,
{
    match value {
        Some(value) => serializer.serialize_some(&value.to_string()),
        None => serializer.serialize_none(),
    }
}

const fn is_false(value: &bool) -> bool {
    !*value
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::{Value, json};

    #[test]
    fn defaults_match_schema() {
        let options = VersionOptions::default();
        assert_eq!(options.version_height_offset_or_default(), 0);
        assert_eq!(options.sem_ver1_numeric_identifier_padding_or_default(), 4);
        assert_eq!(options.git_commit_id_short_fixed_length_or_default(), 10);
        assert!(!options.git_commit_id_include_dirty_or_default());
        assert_eq!(
            options.assembly_version_or_default().precision_or_default(),
            VersionPrecision::Minor
        );
        assert_eq!(
            options
                .nuget_package_version_or_default()
                .precision_or_default(),
            VersionPrecision::Build
        );
        assert!(
            options
                .cloud_build_or_default()
                .set_version_variables_or_default()
        );
        assert_eq!(
            options.release_or_default().first_unstable_tag_or_default(),
            "alpha"
        );
    }

    #[test]
    fn exact_camel_case_json_and_defaults_are_omitted() {
        let options = VersionOptions {
            git_commit_id_short_fixed_length: Some(12),
            cloud_build: Some(CloudBuildOptions {
                set_all_variables: Some(true),
                ..Default::default()
            }),
            release: Some(ReleaseOptions {
                first_unstable_tag: Some("beta".to_owned()),
                ..Default::default()
            }),
            ..Default::default()
        };

        let value = serde_json::to_value(options).unwrap();
        assert_eq!(
            value,
            json!({
                "gitCommitIdShortFixedLength": 12,
                "cloudBuild": { "setAllVariables": true },
                "release": { "firstUnstableTag": "beta" }
            })
        );
    }

    #[test]
    fn path_filters_are_resolved_relative_to_version_file() {
        let options =
            VersionOptions::from_json(r#"{"pathFilters":["src/**",":^temp"]}"#, "eng").unwrap();
        let filters = options.path_filters.as_ref().unwrap();
        assert_eq!(filters[0].repo_relative_path(), "eng/src/**");
        assert_eq!(filters[1].repo_relative_path(), "eng/temp");

        let value: Value = serde_json::from_str(&options.to_json("eng").unwrap()).unwrap();
        assert_eq!(value["pathFilters"], json!(["./src/**", ":!temp"]));
    }

    #[test]
    fn assembly_version_accepts_shorthand_and_object_forms() {
        let shorthand: VersionOptions =
            serde_json::from_value(json!({ "assemblyVersion": "1.2.3.4" })).unwrap();
        assert_eq!(
            shorthand
                .assembly_version
                .as_ref()
                .unwrap()
                .precision_or_default(),
            VersionPrecision::Minor
        );
        assert_eq!(
            serde_json::to_value(&shorthand).unwrap(),
            json!({ "assemblyVersion": "1.2.3.4" })
        );

        let object: VersionOptions = serde_json::from_value(json!({
            "assemblyVersion": { "version": "1.2", "precision": "revision" }
        }))
        .unwrap();
        assert_eq!(
            object.assembly_version.as_ref().unwrap().precision,
            Some(VersionPrecision::Revision)
        );
        assert_eq!(
            serde_json::to_value(object).unwrap(),
            json!({
                "assemblyVersion": { "version": "1.2", "precision": "revision" }
            })
        );
    }

    #[test]
    fn nested_equality_compares_effective_defaults() {
        assert_eq!(
            AssemblyVersionOptions::default(),
            AssemblyVersionOptions {
                precision: Some(VersionPrecision::Minor),
                ..Default::default()
            }
        );
        assert_eq!(
            NuGetPackageVersionOptions::default(),
            NuGetPackageVersionOptions {
                sem_ver: Some(1.0),
                precision: Some(VersionPrecision::Build),
            }
        );
        assert_eq!(
            CloudBuildOptions::default(),
            CloudBuildOptions {
                set_all_variables: Some(false),
                set_version_variables: Some(true),
                build_number: Some(CloudBuildNumberOptions::default()),
            }
        );
        assert_eq!(
            ReleaseOptions::default(),
            ReleaseOptions {
                tag_name: Some("v{version}".to_owned()),
                branch_name: Some("v{version}".to_owned()),
                version_increment: Some(ReleaseVersionIncrement::Minor),
                first_unstable_tag: Some("alpha".to_owned()),
            }
        );
    }

    #[test]
    fn merge_only_replaces_explicit_values() {
        let mut parent = VersionOptions {
            git_commit_id_prefix: Some("g".to_owned()),
            version_height_offset: Some(7),
            cloud_build: Some(CloudBuildOptions {
                set_version_variables: Some(true),
                ..Default::default()
            }),
            ..Default::default()
        };
        let child = VersionOptions {
            version_height_offset: Some(0),
            prerelease: Some(String::new()),
            inherit: true,
            ..Default::default()
        };

        parent.merge_from(&child);
        assert_eq!(parent.git_commit_id_prefix.as_deref(), Some("g"));
        assert_eq!(parent.version_height_offset, Some(0));
        assert_eq!(parent.prerelease.as_deref(), Some(""));
        assert!(parent.inherit);
    }

    #[test]
    fn merging_inheriting_options_applies_prerelease() {
        let mut parent: VersionOptions =
            serde_json::from_value(json!({ "version": "1.2" })).unwrap();
        let child: VersionOptions =
            serde_json::from_value(json!({ "inherit": true, "prerelease": "beta" })).unwrap();

        parent.merge_inheriting(&child).unwrap();
        assert_eq!(parent.version.unwrap().to_string(), "1.2-beta");
        assert_eq!(parent.prerelease, None);
        assert!(!parent.inherit);

        let mut suppress: VersionOptions =
            serde_json::from_value(json!({ "version": "1.2-alpha" })).unwrap();
        let child: VersionOptions =
            serde_json::from_value(json!({ "inherit": true, "prerelease": "" })).unwrap();
        suppress.merge_inheriting(&child).unwrap();
        assert_eq!(suppress.version.unwrap().to_string(), "1.2");
    }

    #[test]
    fn obsolete_offset_name_is_accepted_but_modern_name_is_written() {
        let options: VersionOptions =
            serde_json::from_value(json!({ "buildNumberOffset": -3 })).unwrap();
        assert_eq!(options.version_height_offset, Some(-3));
        let value = serde_json::to_value(options).unwrap();
        assert_eq!(value.get("versionHeightOffset"), Some(&Value::from(-3)));
        assert!(value.get("buildNumberOffset").is_none());
    }

    #[test]
    fn effective_offset_stops_applying_after_height_reset() {
        let options: VersionOptions = serde_json::from_value(json!({
            "version": "2.0",
            "versionHeightOffset": 42,
            "versionHeightOffsetAppliesTo": "1.0"
        }))
        .unwrap();
        assert_eq!(options.effective_version_height_offset(), 0);

        let compatible: VersionOptions = serde_json::from_value(json!({
            "version": "1.0-beta",
            "versionHeightOffset": 42,
            "versionHeightOffsetAppliesTo": "1.0-alpha"
        }))
        .unwrap();
        assert_eq!(compatible.effective_version_height_offset(), 42);
    }
}
