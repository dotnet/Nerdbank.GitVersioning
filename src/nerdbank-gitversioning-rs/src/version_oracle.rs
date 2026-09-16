// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! Assembly of calculated version information in the formats exposed by `nbgv`.

use std::collections::BTreeMap;

use chrono::{DateTime, FixedOffset, SecondsFormat};
use fancy_regex::Regex;
use serde::Serialize;
use serde::ser::{SerializeMap, Serializer};

use crate::cloud::CloudBuild;
use crate::{
    CloudBuildNumberCommitWhen, CloudBuildNumberCommitWhere, CloudBuildNumberOptions, GitContext,
    Result, SemanticVersion, Version, VersionFile, VersionOptions, VersionPosition,
    VersionPrecision, calculate_version_height, get_id_as_version,
};

/// The complete set of version values calculated for a project.
///
/// Values that depend on public-release state or caller-supplied identifiers are exposed as
/// methods because [`public_release`](Self::public_release), [`build_metadata`](Self::build_metadata),
/// and [`extra_prerelease_identifiers`](Self::extra_prerelease_identifiers) remain mutable.
#[derive(Clone, Debug)]
pub struct VersionOracle {
    version_options: Option<VersionOptions>,
    committed_version: Option<VersionOptions>,
    working_version: Option<VersionOptions>,
    cloud_build_number_options: CloudBuildNumberOptions,
    version_git_commit_id_short: Option<String>,
    assembly_informational_version: Version,
    assembly_informational_version_component_count: usize,
    git_commit_date: Option<DateTime<FixedOffset>>,
    git_commit_author_date: Option<DateTime<FixedOffset>>,

    /// Whether the calculated version is a public release.
    pub public_release: bool,
    /// Additional build metadata identifiers.
    pub build_metadata: Vec<String>,
    /// Additional prerelease identifiers.
    pub extra_prerelease_identifiers: Vec<String>,
    /// The ref (branch or tag) being built.
    pub building_ref: Option<String>,
    /// Canonical tag refs that point at the selected commit.
    pub tags: Option<Vec<String>>,
    /// The encoded numeric version.
    pub version: Version,
    /// The full selected commit ID, optionally suffixed by `-dirty`.
    pub git_commit_id: Option<String>,
    /// The abbreviated selected commit ID, optionally suffixed by `-dirty`.
    pub git_commit_id_short: Option<String>,
    /// The calculated version height.
    pub version_height: u32,
}

impl VersionOracle {
    /// Calculates version information from a Git context and optional cloud provider.
    pub fn new(context: &GitContext, cloud_build: Option<&dyn CloudBuild>) -> Result<Self> {
        Self::new_with_height_offset(context, cloud_build, None)
    }

    /// Calculates version information with an explicit version-height offset override.
    pub fn new_with_height_offset(
        context: &GitContext,
        cloud_build: Option<&dyn CloudBuild>,
        override_version_height_offset: Option<i32>,
    ) -> Result<Self> {
        let raw_git_commit_id = context
            .git_commit_id()
            .map(|id| id.to_string())
            .or_else(|| cloud_build.and_then(CloudBuild::git_commit_id));

        let mut committed_version = VersionFile::new(context).get_version(Default::default())?.0;
        let mut working_version = if context.is_head()? {
            VersionFile::new(context)
                .get_working_copy_version(Default::default())?
                .0
        } else {
            committed_version.clone()
        };
        if let Some(offset) = override_version_height_offset {
            if let Some(options) = &mut committed_version {
                options.version_height_offset = Some(offset);
            }
            if let Some(options) = &mut working_version {
                options.version_height_offset = Some(offset);
            }
        }

        let building_ref = cloud_build
            .and_then(CloudBuild::building_tag)
            .or_else(|| cloud_build.and_then(CloudBuild::building_branch))
            .or(context.head_canonical_name()?);
        let version_height = calculate_version_height(
            context,
            committed_version.as_ref(),
            working_version.as_ref(),
        )?;
        let version_git_commit_id = version_height
            .commit_id
            .map(|id| id.to_string())
            .or_else(|| raw_git_commit_id.clone());
        let version_options = committed_version
            .clone()
            .or_else(|| working_version.clone());
        let dirty = context.is_head()?
            && version_options
                .as_ref()
                .is_some_and(VersionOptions::git_commit_id_include_dirty_or_default)
            && context.is_working_tree_dirty()?;
        let git_commit_id = raw_git_commit_id.as_ref().map(|id| {
            if dirty {
                format!("{id}-dirty")
            } else {
                id.clone()
            }
        });

        let base_version = version_options
            .as_ref()
            .and_then(|options| options.version.as_ref())
            .map(|version| version.version)
            .unwrap_or_default();
        let component_count = if version_options.as_ref().is_some_and(|options| {
            options.version_height_position() == Some(VersionPosition::Revision)
        }) {
            4
        } else {
            3
        };
        let (version, assembly_informational_version) = if context.is_repository() {
            let version = get_id_as_version(
                context,
                committed_version.as_ref(),
                working_version.as_ref(),
                version_height,
            )?;
            let head_height = crate::VersionHeightCalculation {
                height: version_height.height,
                commit_id: context.git_commit_id(),
            };
            let informational = get_id_as_version(
                context,
                committed_version.as_ref(),
                working_version.as_ref(),
                head_height,
            )?;
            (version, informational)
        } else {
            (base_version, base_version)
        };

        let short_id = |id: &str, selected: bool| -> Result<String> {
            let fixed = version_options.as_ref().map_or(
                10,
                VersionOptions::git_commit_id_short_fixed_length_or_default,
            ) as usize;
            let automatic = version_options.as_ref().map_or(
                0,
                VersionOptions::git_commit_id_short_auto_minimum_or_default,
            ) as usize;
            if automatic > 0 {
                if selected {
                    context.short_unique_commit_id(automatic)
                } else {
                    let oid = git2::Oid::from_str(id).map_err(crate::Error::Git)?;
                    context.short_unique_id(oid, automatic)
                }
            } else {
                id.get(..fixed).map(str::to_owned).ok_or_else(|| {
                    crate::Error::InvalidOperation(format!(
                        "Git commit ID '{id}' is shorter than the configured length {fixed}."
                    ))
                })
            }
        };
        let git_commit_id_short = raw_git_commit_id
            .as_deref()
            .map(|id| short_id(id, true))
            .transpose()?
            .map(|id| if dirty { format!("{id}-dirty") } else { id });
        let version_git_commit_id_short = version_git_commit_id
            .as_deref()
            .map(|id| {
                short_id(
                    id,
                    context
                        .git_commit_id()
                        .is_some_and(|selected| selected.to_string() == id),
                )
            })
            .transpose()?;

        let tags = context.head_tags()?;
        let mut public_release = false;
        if let Some(options) = &version_options
            && let Some(specs) = &options.public_release_ref_spec
        {
            if let Some(reference) = &building_ref {
                let expressions = specs
                    .iter()
                    .map(|expression| Regex::new(expression))
                    .collect::<std::result::Result<Vec<_>, _>>()
                    .map_err(|error| crate::Error::InvalidFormat(error.to_string()))?;
                for expression in expressions {
                    if expression
                        .is_match(reference)
                        .map_err(|error| crate::Error::InvalidFormat(error.to_string()))?
                    {
                        public_release = true;
                        break;
                    }
                }
            }
            if !public_release
                && specs
                    .iter()
                    .any(|expression| expression.starts_with("^refs/tags/"))
                && let Some(tags) = &tags
            {
                for expression in specs {
                    let expression = Regex::new(expression)
                        .map_err(|error| crate::Error::InvalidFormat(error.to_string()))?;
                    for tag in tags {
                        if expression
                            .is_match(tag)
                            .map_err(|error| crate::Error::InvalidFormat(error.to_string()))?
                        {
                            public_release = true;
                            break;
                        }
                    }
                    if public_release {
                        break;
                    }
                }
            }
        }
        let dates = context.git_commit_dates()?;

        Ok(Self {
            cloud_build_number_options: version_options
                .as_ref()
                .map(VersionOptions::cloud_build_or_default)
                .map(|options| options.build_number_or_default())
                .unwrap_or_default(),
            version_options,
            committed_version,
            working_version,
            version_git_commit_id_short,
            assembly_informational_version,
            assembly_informational_version_component_count: component_count,
            git_commit_date: dates.map(|dates| dates.committer),
            git_commit_author_date: dates.map(|dates| dates.author),
            public_release,
            build_metadata: Vec::new(),
            extra_prerelease_identifiers: Vec::new(),
            building_ref,
            tags,
            version,
            git_commit_id,
            git_commit_id_short,
            version_height: version_height.height,
        })
    }

    /// Gets the options selected by the oracle.
    pub fn version_options(&self) -> Option<&VersionOptions> {
        self.version_options.as_ref()
    }

    /// Gets the committed options.
    pub fn committed_version(&self) -> Option<&VersionOptions> {
        self.committed_version.as_ref()
    }

    /// Gets the working-copy options.
    pub fn working_version(&self) -> Option<&VersionOptions> {
        self.working_version.as_ref()
    }

    /// Gets whether a version file was found.
    pub fn version_file_found(&self) -> bool {
        self.version_options.is_some()
    }

    /// Gets the effective height offset.
    pub fn version_height_offset(&self) -> i32 {
        self.version_options
            .as_ref()
            .map_or(0, VersionOptions::effective_version_height_offset)
    }

    /// Gets the height including its effective offset.
    pub fn version_height_with_offset(&self) -> i64 {
        i64::from(self.version_height) + i64::from(self.version_height_offset())
    }

    /// Gets the assembly version.
    pub fn assembly_version(&self) -> Version {
        let version = self
            .version
            .ensure_non_negative_components(4)
            .expect("four fields");
        let assembly = self
            .version_options
            .as_ref()
            .map(VersionOptions::assembly_version_or_default);
        let result = if let Some(explicit) = assembly.as_ref().and_then(|options| options.version) {
            explicit
        } else {
            apply_precision(
                version,
                assembly
                    .as_ref()
                    .map_or(VersionPrecision::Minor, |options| {
                        options.precision_or_default()
                    }),
            )
        };
        result
            .ensure_non_negative_components(4)
            .expect("four fields")
    }

    /// Gets the assembly file version.
    pub const fn assembly_file_version(&self) -> Version {
        self.version
    }

    /// Gets the assembly informational version.
    pub fn assembly_informational_version(&self) -> String {
        format!(
            "{}{}{}",
            self.assembly_informational_version
                .to_string_safe(self.assembly_informational_version_component_count)
                .expect("component count is valid"),
            self.prerelease_version(),
            format_build_metadata(self.build_metadata_with_commit_id())
        )
    }

    /// Gets the prerelease fragment, including a leading hyphen.
    pub fn prerelease_version(&self) -> String {
        let mut result = self
            .version_options
            .as_ref()
            .and_then(|options| options.version.as_ref())
            .map_or_else(String::new, |version| {
                self.replace_macros(&version.prerelease)
            });
        let semver2 = self
            .version_options
            .as_ref()
            .map(VersionOptions::nuget_package_version_or_default)
            .is_some_and(|options| options.sem_ver_or_default() >= 2.0);
        for identifier in &self.extra_prerelease_identifiers {
            if result.is_empty() {
                result.push('-');
            } else {
                result.push(if semver2 { '.' } else { '-' });
            }
            result.push_str(identifier);
        }
        result
    }

    /// Gets the prerelease fragment without leading hyphens.
    pub fn prerelease_version_no_leading_hyphen(&self) -> String {
        self.prerelease_version().trim_start_matches('-').to_owned()
    }

    /// Gets the numeric version without its revision component.
    pub fn simple_version(&self) -> Version {
        match self.version.build {
            Some(build) => Version::new_with_build(self.version.major, self.version.minor, build),
            None => Version::new(self.version.major, self.version.minor),
        }
    }

    /// Gets the third numeric component, treating an unspecified value as zero.
    pub fn build_number(&self) -> u32 {
        self.version.build.unwrap_or(0)
    }

    /// Gets the fourth numeric component using `-1` when unspecified.
    pub fn version_revision(&self) -> i64 {
        self.version.revision_or_unspecified()
    }

    /// Gets the major/minor numeric version.
    pub const fn major_minor_version(&self) -> Version {
        Version::new(self.version.major, self.version.minor)
    }

    /// Gets the selected commit's committer date.
    pub const fn git_commit_date(&self) -> Option<DateTime<FixedOffset>> {
        self.git_commit_date
    }

    /// Gets the selected commit's author date.
    pub const fn git_commit_author_date(&self) -> Option<DateTime<FixedOffset>> {
        self.git_commit_author_date
    }

    /// Gets build metadata with the selected commit ID first.
    pub fn build_metadata_with_commit_id(&self) -> Vec<String> {
        self.git_commit_id_short
            .iter()
            .cloned()
            .chain(self.build_metadata.iter().cloned())
            .collect()
    }

    fn version_build_metadata_with_commit_id(&self) -> Vec<String> {
        self.version_git_commit_id_short
            .iter()
            .cloned()
            .chain(self.build_metadata.iter().cloned())
            .collect()
    }

    /// Gets the `+`-prefixed build metadata fragment used by calculated versions.
    pub fn build_metadata_fragment(&self) -> String {
        format_build_metadata(self.version_build_metadata_with_commit_id())
    }

    /// Gets the SemVer 1 representation.
    pub fn sem_ver1(&self) -> String {
        format!(
            "{}{}{}",
            self.version.to_string_safe(3).expect("three fields"),
            self.prerelease_version_semver1(),
            if self.public_release {
                String::new()
            } else {
                format!(
                    "-{}",
                    self.version_git_commit_id_short.as_deref().unwrap_or("")
                )
            }
        )
    }

    /// Gets the SemVer 2 representation.
    pub fn sem_ver2(&self) -> String {
        format!(
            "{}{}{}",
            self.version.to_string_safe(3).expect("three fields"),
            self.prerelease_version(),
            self.semver2_build_metadata()
        )
    }

    /// Gets the NuGet package version.
    pub fn nuget_package_version(&self) -> String {
        if self
            .version_options
            .as_ref()
            .map(VersionOptions::nuget_package_version_or_default)
            .map_or(1.0, |options| options.sem_ver_or_default())
            == 1.0
        {
            self.nuget_semver1()
        } else {
            self.nuget_semver2()
        }
    }

    /// Gets the Chocolatey package version.
    pub fn chocolatey_package_version(&self) -> String {
        self.nuget_semver1()
    }

    /// Gets the NPM package version.
    pub fn npm_package_version(&self) -> String {
        self.sem_ver2()
    }

    /// Gets the SemVer 1 numeric-identifier padding.
    pub fn sem_ver1_numeric_identifier_padding(&self) -> u32 {
        self.version_options.as_ref().map_or(4, |options| {
            options.sem_ver1_numeric_identifier_padding_or_default()
        })
    }

    /// Gets the cloud build number.
    pub fn cloud_build_number(&self) -> String {
        let commit = self
            .cloud_build_number_options
            .include_commit_id_or_default();
        let include = commit.when_or_default() == CloudBuildNumberCommitWhen::Always
            || (commit.when_or_default() == CloudBuildNumberCommitWhen::NonPublicReleaseOnly
                && !self.public_release);
        let metadata_commit =
            include && commit.where_or_default() == CloudBuildNumberCommitWhere::BuildMetadata;
        let include_revision = (include
            && commit.where_or_default() == CloudBuildNumberCommitWhere::FourthVersionComponent)
            || self
                .version_options
                .as_ref()
                .and_then(|options| options.version.as_ref())
                .is_some_and(|version| {
                    version.version_height_position() == Some(VersionPosition::Revision)
                        || version.version.revision.is_some()
                });
        let metadata = if metadata_commit {
            self.version_build_metadata_with_commit_id()
        } else {
            self.build_metadata.clone()
        };
        format!(
            "{}{}{}",
            if include_revision {
                self.version.to_string()
            } else {
                self.simple_version().to_string()
            },
            self.prerelease_version(),
            format_build_metadata(metadata)
        )
    }

    /// Gets whether setting the cloud build number is enabled.
    pub fn cloud_build_number_enabled(&self) -> bool {
        self.cloud_build_number_options.enabled_or_default()
    }

    /// Gets whether all `NBGV_` variables should be set.
    pub fn cloud_build_all_vars_enabled(&self) -> bool {
        self.version_options
            .as_ref()
            .map(VersionOptions::cloud_build_or_default)
            .is_some_and(|options| options.set_all_variables_or_default())
    }

    /// Gets whether common version variables should be set.
    pub fn cloud_build_version_vars_enabled(&self) -> bool {
        self.version_options
            .as_ref()
            .map(VersionOptions::cloud_build_or_default)
            .is_none_or(|options| options.set_version_variables_or_default())
    }

    /// Gets the common cloud-build variables.
    pub fn cloud_build_version_vars(&self) -> BTreeMap<String, String> {
        BTreeMap::from([
            (
                "GitAssemblyInformationalVersion".to_owned(),
                self.assembly_informational_version(),
            ),
            ("GitBuildVersion".to_owned(), self.version.to_string()),
            (
                "GitBuildVersionSimple".to_owned(),
                self.simple_version().to_string(),
            ),
        ])
    }

    /// Gets every cloud-build variable exposed by the managed oracle.
    pub fn cloud_build_all_vars(&self) -> BTreeMap<String, String> {
        let mut values = BTreeMap::new();
        macro_rules! add {
            ($name:literal, $value:expr) => {
                values.insert(concat!("NBGV_", $name).to_owned(), $value);
            };
        }
        add!("CloudBuildNumber", self.cloud_build_number());
        add!("VersionFileFound", dotnet_bool(self.version_file_found()));
        add!("AssemblyVersion", self.assembly_version().to_string());
        add!("AssemblyFileVersion", self.version.to_string());
        add!(
            "AssemblyInformationalVersion",
            self.assembly_informational_version()
        );
        add!("PublicRelease", dotnet_bool(self.public_release));
        add!("PrereleaseVersion", self.prerelease_version());
        add!(
            "PrereleaseVersionNoLeadingHyphen",
            self.prerelease_version_no_leading_hyphen()
        );
        add!("SimpleVersion", self.simple_version().to_string());
        add!("BuildNumber", self.build_number().to_string());
        add!("VersionRevision", self.version_revision().to_string());
        add!("MajorMinorVersion", self.major_minor_version().to_string());
        add!("VersionMajor", self.version.major.to_string());
        add!("VersionMinor", self.version.minor.to_string());
        if let Some(value) = &self.git_commit_id {
            add!("GitCommitId", value.clone());
        }
        if let Some(value) = &self.git_commit_id_short {
            add!("GitCommitIdShort", value.clone());
        }
        if let Some(value) = self.git_commit_date {
            add!("GitCommitDate", dotnet_roundtrip(value));
        }
        if let Some(value) = self.git_commit_author_date {
            add!("GitCommitAuthorDate", dotnet_roundtrip(value));
        }
        add!("VersionHeight", self.version_height.to_string());
        add!(
            "VersionHeightOffset",
            self.version_height_offset().to_string()
        );
        if let Some(value) = &self.building_ref {
            add!("BuildingRef", value.clone());
        }
        add!("Version", self.version.to_string());
        add!("BuildMetadataFragment", self.build_metadata_fragment());
        add!("NuGetPackageVersion", self.nuget_package_version());
        add!(
            "ChocolateyPackageVersion",
            self.chocolatey_package_version()
        );
        add!("NpmPackageVersion", self.npm_package_version());
        add!("SemVer1", self.sem_ver1());
        add!("SemVer2", self.sem_ver2());
        add!(
            "SemVer1NumericIdentifierPadding",
            self.sem_ver1_numeric_identifier_padding().to_string()
        );
        values
    }

    fn replace_macros(&self, value: &str) -> String {
        value.replace("{height}", &self.version_height_with_offset().to_string())
    }

    fn prerelease_version_semver1(&self) -> String {
        SemanticVersion::make_prerelease_semver1_compliant(
            &self.prerelease_version(),
            self.sem_ver1_numeric_identifier_padding() as usize,
        )
    }

    fn non_public_commit_prerelease(&self) -> String {
        format!(
            "{}{}{}",
            if self.prerelease_version().is_empty() {
                "-"
            } else {
                "."
            },
            self.version_options
                .as_ref()
                .and_then(|options| options.git_commit_id_prefix.as_deref())
                .unwrap_or("g"),
            self.version_git_commit_id_short.as_deref().unwrap_or("")
        )
    }

    fn semver2_build_metadata(&self) -> String {
        format!(
            "{}{}",
            if self.public_release {
                String::new()
            } else {
                self.non_public_commit_prerelease()
            },
            format_build_metadata(self.build_metadata.clone())
        )
    }

    fn package_version_base(&self) -> (Version, VersionPrecision, usize) {
        let options = self
            .version_options
            .as_ref()
            .map(VersionOptions::nuget_package_version_or_default)
            .unwrap_or_default();
        let precision = options.precision_or_default();
        let version = apply_precision(
            self.version
                .ensure_non_negative_components(4)
                .expect("four fields"),
            precision,
        );
        let fields = if precision == VersionPrecision::Revision {
            4
        } else {
            3
        };
        (version, precision, fields)
    }

    fn nuget_semver1(&self) -> String {
        let (version, _, fields) = self.package_version_base();
        let suffix = if self.public_release {
            String::new()
        } else {
            format!(
                "-{}{}",
                self.version_options
                    .as_ref()
                    .and_then(|options| options.git_commit_id_prefix.as_deref())
                    .unwrap_or("g"),
                self.version_git_commit_id_short.as_deref().unwrap_or("")
            )
        };
        format!(
            "{}{}{}",
            version.to_string_safe(fields).expect("field count"),
            self.prerelease_version_semver1(),
            suffix
        )
    }

    fn nuget_semver2(&self) -> String {
        let (version, _, fields) = self.package_version_base();
        format!(
            "{}{}{}",
            version.to_string_safe(fields).expect("field count"),
            self.prerelease_version(),
            self.semver2_build_metadata()
        )
    }
}

impl Serialize for VersionOracle {
    fn serialize<S>(&self, serializer: S) -> std::result::Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        let mut map = serializer.serialize_map(None)?;
        macro_rules! entry {
            ($name:literal, $value:expr) => {
                map.serialize_entry($name, &$value)?;
            };
        }
        entry!("CloudBuildNumber", self.cloud_build_number());
        entry!("CloudBuildNumberEnabled", self.cloud_build_number_enabled());
        entry!(
            "BuildMetadataWithCommitId",
            self.build_metadata_with_commit_id()
        );
        entry!("VersionFileFound", self.version_file_found());
        entry!("VersionOptions", self.version_options);
        entry!("AssemblyVersion", self.assembly_version().to_string());
        entry!("AssemblyFileVersion", self.version.to_string());
        entry!(
            "AssemblyInformationalVersion",
            self.assembly_informational_version()
        );
        entry!("PublicRelease", self.public_release);
        entry!("PrereleaseVersion", self.prerelease_version());
        entry!(
            "PrereleaseVersionNoLeadingHyphen",
            self.prerelease_version_no_leading_hyphen()
        );
        entry!("SimpleVersion", self.simple_version().to_string());
        entry!("BuildNumber", self.build_number());
        entry!("VersionRevision", self.version_revision());
        entry!("MajorMinorVersion", self.major_minor_version().to_string());
        entry!("VersionMajor", self.version.major);
        entry!("VersionMinor", self.version.minor);
        entry!("GitCommitId", self.git_commit_id);
        entry!("GitCommitIdShort", self.git_commit_id_short);
        entry!("GitCommitDate", self.git_commit_date);
        entry!("GitCommitAuthorDate", self.git_commit_author_date);
        entry!("VersionHeight", self.version_height);
        entry!("VersionHeightOffset", self.version_height_offset());
        entry!("BuildingRef", self.building_ref);
        entry!("Tags", self.tags);
        entry!("Version", self.version.to_string());
        entry!(
            "CloudBuildAllVarsEnabled",
            self.cloud_build_all_vars_enabled()
        );
        entry!("CloudBuildAllVars", self.cloud_build_all_vars());
        entry!(
            "CloudBuildVersionVarsEnabled",
            self.cloud_build_version_vars_enabled()
        );
        entry!("CloudBuildVersionVars", self.cloud_build_version_vars());
        entry!("BuildMetadata", self.build_metadata);
        entry!(
            "ExtraPrereleaseIdentifiers",
            self.extra_prerelease_identifiers
        );
        entry!("BuildMetadataFragment", self.build_metadata_fragment());
        entry!("NuGetPackageVersion", self.nuget_package_version());
        entry!(
            "ChocolateyPackageVersion",
            self.chocolatey_package_version()
        );
        entry!("NpmPackageVersion", self.npm_package_version());
        entry!("SemVer1", self.sem_ver1());
        entry!("SemVer2", self.sem_ver2());
        entry!(
            "SemVer1NumericIdentifierPadding",
            self.sem_ver1_numeric_identifier_padding()
        );
        map.end()
    }
}

fn apply_precision(version: Version, precision: VersionPrecision) -> Version {
    Version::new_with_revision(
        version.major,
        if precision == VersionPrecision::Major {
            0
        } else {
            version.minor
        },
        if matches!(
            precision,
            VersionPrecision::Build | VersionPrecision::Revision
        ) {
            version.build.unwrap_or(0)
        } else {
            0
        },
        if precision == VersionPrecision::Revision {
            version.revision.unwrap_or(0)
        } else {
            0
        },
    )
}

fn format_build_metadata(identifiers: impl IntoIterator<Item = String>) -> String {
    let identifiers: Vec<_> = identifiers.into_iter().collect();
    if identifiers.is_empty() {
        String::new()
    } else {
        format!("+{}", identifiers.join("."))
    }
}

fn dotnet_bool(value: bool) -> String {
    if value { "True" } else { "False" }.to_owned()
}

fn dotnet_roundtrip(value: DateTime<FixedOffset>) -> String {
    let base = value.to_rfc3339_opts(SecondsFormat::Secs, false);
    let offset = &base[base.len() - 6..];
    format!("{}.0000000{}", &base[..base.len() - 6], offset)
}

#[cfg(test)]
mod tests {
    use std::fs;
    use std::path::{Path, PathBuf};
    use std::sync::atomic::{AtomicU64, Ordering};

    use git2::{IndexAddOption, Oid, Repository, Signature, Time};
    use serde_json::Value;

    use super::*;
    use crate::GitEngine;

    static NEXT_REPOSITORY: AtomicU64 = AtomicU64::new(0);

    struct TestRepository {
        path: PathBuf,
    }

    impl TestRepository {
        fn new(version_json: &str) -> Self {
            let id = NEXT_REPOSITORY.fetch_add(1, Ordering::Relaxed);
            let path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
                .join("target")
                .join("test-repositories")
                .join(format!("oracle-{}-{id}", std::process::id()));
            let _ = fs::remove_dir_all(&path);
            fs::create_dir_all(&path).unwrap();
            Repository::init(&path).unwrap();
            fs::write(path.join("version.json"), version_json).unwrap();
            let result = Self { path };
            result.commit("version");
            result
        }

        fn context(&self) -> GitContext {
            GitContext::create(&self.path, None, GitEngine::ReadOnly).unwrap()
        }

        fn commit(&self, message: &str) -> Oid {
            let repository = Repository::open(&self.path).unwrap();
            let mut index = repository.index().unwrap();
            index.add_all(["*"], IndexAddOption::DEFAULT, None).unwrap();
            index.write().unwrap();
            let tree_id = index.write_tree().unwrap();
            let tree = repository.find_tree(tree_id).unwrap();
            let signature =
                Signature::new("Test", "test@example.com", &Time::new(1_700_000_000, -420))
                    .unwrap();
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

    fn oracle(repository: &TestRepository) -> VersionOracle {
        VersionOracle::new(&repository.context(), None).unwrap()
    }

    #[test]
    fn calculates_core_versions_commit_encoding_and_dates() {
        let repository = TestRepository::new(r#"{"version":"1.2"}"#);
        let oracle = oracle(&repository);
        let commit = repository.context().git_commit_id().unwrap();

        assert_eq!(oracle.version_height, 1);
        assert_eq!(
            oracle.version,
            Version::new_with_revision(1, 2, 1, u32::from(crate::truncated_commit_id(commit)))
        );
        assert_eq!(oracle.simple_version().to_string(), "1.2.1");
        assert_eq!(oracle.assembly_version().to_string(), "1.2.0.0");
        assert_eq!(oracle.assembly_file_version(), oracle.version);
        assert_eq!(
            oracle.git_commit_id_short.as_deref(),
            Some(&commit.to_string()[..10])
        );
        assert_eq!(
            oracle.git_commit_date().unwrap().to_rfc3339(),
            "2023-11-14T15:13:20-07:00"
        );
        assert!(
            oracle
                .assembly_informational_version()
                .starts_with("1.2.1+")
        );
    }

    #[test]
    fn formats_semver_packages_metadata_and_prerelease_extras() {
        let repository = TestRepository::new(
            r#"{"version":"7.8.9-beta.{height}","versionHeightOffset":2,"nugetPackageVersion":{"semVer":2}}"#,
        );
        let mut oracle = oracle(&repository);
        oracle.public_release = true;
        oracle.build_metadata.extend(["one".into(), "two".into()]);
        oracle
            .extra_prerelease_identifiers
            .extend(["x".into(), "2".into()]);

        assert_eq!(oracle.prerelease_version(), "-beta.3.x.2");
        assert_eq!(oracle.sem_ver2(), "7.8.9-beta.3.x.2+one.two");
        assert_eq!(oracle.npm_package_version(), oracle.sem_ver2());
        assert_eq!(oracle.nuget_package_version(), oracle.sem_ver2());
        assert!(!oracle.build_metadata_fragment().ends_with("+one.two"));
        assert!(oracle.build_metadata_fragment().contains(".one.two"));

        oracle.public_release = false;
        assert!(oracle.nuget_package_version().contains(".g"));
    }

    #[test]
    fn semver1_padding_precision_and_custom_assembly_version_match_managed_cases() {
        let repository = TestRepository::new(
            r#"{
                "version":"7.8.9-foo.5.bar.1",
                "semVer1NumericIdentifierPadding":4,
                "assemblyVersion":{"precision":"build"},
                "nugetPackageVersion":{"semVer":1,"precision":"minor"}
            }"#,
        );
        let mut oracle = oracle(&repository);
        oracle.public_release = true;

        assert_eq!(oracle.sem_ver1(), "7.8.9-foo-0005-bar-0001");
        assert_eq!(oracle.nuget_package_version(), "7.8.0-foo-0005-bar-0001");
        assert_eq!(
            oracle.chocolatey_package_version(),
            oracle.nuget_package_version()
        );
        assert_eq!(oracle.assembly_version().to_string(), "7.8.9.0");
    }

    #[test]
    fn dirty_ids_and_working_version_are_observed_only_at_head() {
        let repository = TestRepository::new(r#"{"version":"1.2","gitCommitIdIncludeDirty":true}"#);
        fs::write(repository.path.join("tracked.txt"), "second commit").unwrap();
        repository.commit("second");
        fs::write(
            repository.path.join("version.json"),
            r#"{"version":"1.3","gitCommitIdIncludeDirty":true}"#,
        )
        .unwrap();
        let oracle = oracle(&repository);

        assert_eq!(oracle.major_minor_version().to_string(), "1.3");
        assert_eq!(oracle.version_height, 0);
        assert!(oracle.git_commit_id.as_deref().unwrap().ends_with("-dirty"));
        assert!(
            oracle
                .git_commit_id_short
                .as_deref()
                .unwrap()
                .ends_with("-dirty")
        );

        let historical =
            GitContext::create(&repository.path, Some("HEAD~1"), GitEngine::ReadOnly).unwrap();
        let historical = VersionOracle::new(&historical, None).unwrap();
        assert_eq!(historical.major_minor_version().to_string(), "1.2");
        assert!(
            !historical
                .git_commit_id
                .as_deref()
                .unwrap()
                .ends_with("-dirty")
        );
    }

    #[test]
    fn dirty_tracking_matches_managed_tracked_untracked_and_ignored_cases() {
        let default = TestRepository::new(r#"{"version":"1.2"}"#);
        fs::write(default.path.join("untracked.txt"), "content").unwrap();
        assert!(
            !oracle(&default)
                .git_commit_id
                .as_deref()
                .unwrap()
                .ends_with("-dirty")
        );

        let enabled = TestRepository::new(r#"{"version":"1.2","gitCommitIdIncludeDirty":true}"#);
        fs::write(enabled.path.join("untracked.txt"), "content").unwrap();
        assert!(
            oracle(&enabled)
                .git_commit_id
                .as_deref()
                .unwrap()
                .ends_with("-dirty")
        );

        let ignored = TestRepository::new(r#"{"version":"1.2","gitCommitIdIncludeDirty":true}"#);
        fs::write(ignored.path.join(".gitignore"), "ignored.txt\n").unwrap();
        {
            let repository = Repository::open(&ignored.path).unwrap();
            let mut index = repository.index().unwrap();
            index.add_path(Path::new(".gitignore")).unwrap();
            index.write().unwrap();
        }
        ignored.commit("ignore rules");
        fs::write(ignored.path.join("ignored.txt"), "content").unwrap();
        assert!(
            ignored
                .context()
                .is_ignored(ignored.path.join("ignored.txt"))
                .unwrap()
        );
        assert!(
            !oracle(&ignored)
                .git_commit_id
                .as_deref()
                .unwrap()
                .ends_with("-dirty")
        );
    }

    #[test]
    fn public_release_matches_branch_and_tags() {
        let repository =
            TestRepository::new(r#"{"version":"1.2","publicReleaseRefSpec":["^refs/tags/v"]}"#);
        let git = Repository::open(&repository.path).unwrap();
        let commit = git.head().unwrap().peel_to_commit().unwrap();
        git.tag_lightweight("v1.2", commit.as_object(), false)
            .unwrap();
        assert!(oracle(&repository).public_release);
    }

    #[test]
    fn public_release_ref_spec_supports_lookaround_and_backreferences() {
        let lookaround = TestRepository::new(
            r#"{"version":"1.2","publicReleaseRefSpec":["^refs/heads/(?=master$)master$"]}"#,
        );
        assert!(oracle(&lookaround).public_release);

        let backreference = TestRepository::new(
            r#"{"version":"1.2","publicReleaseRefSpec":["^refs/heads/(release)-\\1$"]}"#,
        );
        let git = Repository::open(&backreference.path).unwrap();
        let commit_id = git.head().unwrap().peel_to_commit().unwrap().id();
        git.reference("refs/heads/release-release", commit_id, true, "test")
            .unwrap();
        git.set_head("refs/heads/release-release").unwrap();
        drop(git);
        assert!(oracle(&backreference).public_release);
    }

    #[test]
    fn cloud_numbers_cover_metadata_and_fourth_component() {
        let metadata = TestRepository::new(
            r#"{"version":"1.2-alpha","cloudBuild":{"buildNumber":{"includeCommitId":{"when":"always","where":"buildMetadata"}}}}"#,
        );
        let metadata_oracle = oracle(&metadata);
        assert_eq!(
            metadata_oracle.cloud_build_number(),
            format!(
                "1.2.1-alpha+{}",
                metadata_oracle.git_commit_id_short.as_deref().unwrap()
            )
        );

        let fourth = TestRepository::new(
            r#"{"version":"1.2-alpha","cloudBuild":{"buildNumber":{"includeCommitId":{"when":"always","where":"fourthVersionComponent"}}}}"#,
        );
        let fourth_oracle = oracle(&fourth);
        assert_eq!(
            fourth_oracle.cloud_build_number(),
            format!("{}-alpha", fourth_oracle.version)
        );
    }

    #[test]
    fn all_variables_and_json_use_stable_managed_property_names() {
        let repository = TestRepository::new(r#"{"version":"1.2"}"#);
        let oracle = oracle(&repository);
        let variables = oracle.cloud_build_all_vars();
        assert_eq!(variables["NBGV_VersionFileFound"], "True");
        assert_eq!(variables["NBGV_Version"], oracle.version.to_string());
        assert!(!variables.contains_key("NBGV_Tags"));
        assert_eq!(
            oracle.cloud_build_version_vars()["GitBuildVersion"],
            oracle.version.to_string()
        );

        let value: Value = serde_json::to_value(&oracle).unwrap();
        for property in [
            "CloudBuildNumber",
            "VersionFileFound",
            "AssemblyInformationalVersion",
            "NuGetPackageVersion",
            "GitCommitDate",
            "CloudBuildAllVars",
        ] {
            assert!(value.get(property).is_some(), "{property}");
        }
        assert!(value.get("cloudBuildNumber").is_none());
    }

    #[test]
    fn no_repository_still_produces_zero_versions() {
        let path = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("target")
            .join("not-a-repository");
        let _ = fs::remove_dir_all(&path);
        fs::create_dir_all(&path).unwrap();
        let context = GitContext::create(&path, None, GitEngine::Disabled).unwrap();
        let oracle = VersionOracle::new(&context, None).unwrap();
        assert_eq!(oracle.version_height, 0);
        assert_eq!(oracle.version.major, 0);
        assert_eq!(oracle.version.minor, 0);
        let _ = fs::remove_dir_all(path);
    }
}
