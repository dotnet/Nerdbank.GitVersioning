// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! Reusable implementation of the `nbgv cloud` command.

use std::collections::{HashMap, HashSet};
use std::fmt;
use std::io::Write;
use std::path::Path;

use crate::cloud::{CloudBuild, EnvironmentVariables, active, supported_cloud_builds};
use crate::{GitContext, GitEngine, VersionOracle, effective_git_engine};

/// The classified failures produced by [`CloudCommand`].
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum CloudCommandError {
    /// The explicitly named provider was not found.
    NoCloudBuildProviderMatch,
    /// A variable was specified more than once.
    DuplicateCloudVariable,
    /// No supported provider could be detected.
    NoCloudBuildEnvDetected,
    /// Version calculation or provider output failed.
    OperationFailed,
}

/// An error from [`CloudCommand`].
#[derive(Debug)]
pub struct CloudCommandException {
    /// The stable error classification.
    pub error: CloudCommandError,
    message: String,
}

impl CloudCommandException {
    fn new(error: CloudCommandError, message: impl Into<String>) -> Self {
        Self {
            error,
            message: message.into(),
        }
    }
}

impl fmt::Display for CloudCommandException {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        formatter.write_str(&self.message)
    }
}

impl std::error::Error for CloudCommandException {}

/// Arguments accepted by [`CloudCommand::set_build_variables`].
#[derive(Clone, Debug, Default)]
pub struct CloudCommandOptions {
    /// Build metadata identifiers to append.
    pub metadata: Vec<String>,
    /// An explicit cloud build number; an empty value uses the calculated number.
    pub version: Option<String>,
    /// An explicit provider name, matched case-insensitively.
    pub ci_system: Option<String>,
    /// Whether all oracle variables should be defined.
    pub all_vars: bool,
    /// Whether the three common version variables should be defined.
    pub common_vars: bool,
    /// Whether the cloud build number should be changed.
    pub cloud_build_number: bool,
    /// Additional variables to define.
    pub additional_variables: Vec<(String, String)>,
    /// Whether a read-write Git engine should be requested.
    pub always_use_libgit2: bool,
}

/// Applies calculated version values to a cloud-build provider.
pub struct CloudCommand<'a> {
    stdout: &'a mut dyn Write,
    stderr: &'a mut dyn Write,
}

impl<'a> CloudCommand<'a> {
    /// Creates a command writing provider messages to the supplied streams.
    pub fn new(stdout: &'a mut dyn Write, stderr: &'a mut dyn Write) -> Self {
        Self { stdout, stderr }
    }

    /// Calculates and sets cloud variables, returning environment changes requested by providers.
    pub fn set_build_variables(
        &mut self,
        project_directory: impl AsRef<Path>,
        options: &CloudCommandOptions,
    ) -> std::result::Result<EnvironmentVariables, CloudCommandException> {
        let provider: Option<Box<dyn CloudBuild>> =
            if let Some(name) = options.ci_system.as_deref().filter(|name| !name.is_empty()) {
                Some(
                    supported_cloud_builds()
                        .into_iter()
                        .find(|provider| provider.name().eq_ignore_ascii_case(name))
                        .ok_or_else(|| {
                            CloudCommandException::new(
                                CloudCommandError::NoCloudBuildProviderMatch,
                                format!("No cloud provider found by the name: \"{name}\""),
                            )
                        })?,
                )
            } else {
                active()
            };

        let engine = effective_git_engine(if options.always_use_libgit2 {
            GitEngine::ReadWrite
        } else {
            GitEngine::ReadOnly
        });
        let context =
            GitContext::create(project_directory, None, engine).map_err(operation_failed)?;
        let mut oracle =
            VersionOracle::new(&context, provider.as_deref()).map_err(operation_failed)?;
        oracle
            .build_metadata
            .extend(options.metadata.iter().cloned());

        let mut variables = Vec::new();
        if options.all_vars {
            variables.extend(oracle.cloud_build_all_vars());
        }
        if options.common_vars {
            variables.extend(oracle.cloud_build_version_vars());
        }
        let mut names: HashSet<String> = variables.iter().map(|(name, _)| name.clone()).collect();
        for (name, value) in &options.additional_variables {
            if !names.insert(name.clone()) {
                return Err(CloudCommandException::new(
                    CloudCommandError::DuplicateCloudVariable,
                    format!("Cloud build variable \"{name}\" specified more than once."),
                ));
            }
            variables.push((name.clone(), value.clone()));
        }

        let mut changes = HashMap::new();
        let provider = provider.ok_or_else(|| {
            CloudCommandException::new(
                CloudCommandError::NoCloudBuildEnvDetected,
                "No cloud build detected.",
            )
        })?;
        if options.cloud_build_number {
            let build_number = options
                .version
                .as_deref()
                .filter(|version| !version.is_empty())
                .map(str::to_owned)
                .unwrap_or_else(|| oracle.cloud_build_number());
            changes.extend(
                provider
                    .set_cloud_build_number(&build_number, self.stdout, self.stderr)
                    .map_err(operation_failed)?,
            );
        }
        for (name, value) in variables {
            changes.extend(
                provider
                    .set_cloud_build_variable(&name, &value, self.stdout, self.stderr)
                    .map_err(operation_failed)?,
            );
        }
        Ok(changes)
    }
}

fn operation_failed(error: impl fmt::Display) -> CloudCommandException {
    CloudCommandException::new(CloudCommandError::OperationFailed, error.to_string())
}

#[cfg(test)]
mod tests {
    use std::fs;
    use std::path::PathBuf;
    use std::sync::atomic::{AtomicU64, Ordering};

    use git2::{IndexAddOption, Repository, Signature};

    use super::*;

    static NEXT_REPOSITORY: AtomicU64 = AtomicU64::new(0);

    fn repository() -> PathBuf {
        let id = NEXT_REPOSITORY.fetch_add(1, Ordering::Relaxed);
        let path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("target")
            .join("test-repositories")
            .join(format!("cloud-command-{}-{id}", std::process::id()));
        let _ = fs::remove_dir_all(&path);
        fs::create_dir_all(&path).unwrap();
        let repository = Repository::init(&path).unwrap();
        fs::write(path.join("version.json"), r#"{"version":"1.2"}"#).unwrap();
        let mut index = repository.index().unwrap();
        index.add_all(["*"], IndexAddOption::DEFAULT, None).unwrap();
        let tree_id = index.write_tree().unwrap();
        let tree = repository.find_tree(tree_id).unwrap();
        let signature = Signature::now("Test", "test@example.com").unwrap();
        repository
            .commit(Some("HEAD"), &signature, &signature, "version", &tree, &[])
            .unwrap();
        path
    }

    #[test]
    fn explicit_provider_sets_number_variables_and_returns_environment() {
        let path = repository();
        let mut stdout = Vec::new();
        let mut stderr = Vec::new();
        let changes = CloudCommand::new(&mut stdout, &mut stderr)
            .set_build_variables(
                &path,
                &CloudCommandOptions {
                    version: Some("1.2.3.4".into()),
                    ci_system: Some("visualstudioteamservices".into()),
                    cloud_build_number: true,
                    common_vars: true,
                    additional_variables: vec![("Custom".into(), "Value".into())],
                    ..Default::default()
                },
            )
            .unwrap();
        let output = String::from_utf8(stdout).unwrap();
        assert!(output.contains("##vso[build.updatebuildnumber]1.2.3.4"));
        assert!(output.contains("variable=GitBuildVersion;"));
        assert!(output.contains("variable=Custom;"));
        assert_eq!(changes["BUILD_BUILDNUMBER"], "1.2.3.4");
        assert!(stderr.is_empty());
        fs::remove_dir_all(path).unwrap();
    }

    #[test]
    fn reports_unknown_provider_and_exact_duplicates() {
        let path = repository();
        let mut stdout = Vec::new();
        let mut stderr = Vec::new();
        let error = CloudCommand::new(&mut stdout, &mut stderr)
            .set_build_variables(
                &path,
                &CloudCommandOptions {
                    ci_system: Some("missing".into()),
                    ..Default::default()
                },
            )
            .unwrap_err();
        assert_eq!(error.error, CloudCommandError::NoCloudBuildProviderMatch);
        assert_eq!(
            error.to_string(),
            "No cloud provider found by the name: \"missing\""
        );

        CloudCommand::new(&mut stdout, &mut stderr)
            .set_build_variables(
                &path,
                &CloudCommandOptions {
                    ci_system: Some("VisualStudioTeamServices".into()),
                    additional_variables: vec![
                        ("Name".into(), "one".into()),
                        ("name".into(), "two".into()),
                    ],
                    ..Default::default()
                },
            )
            .unwrap();

        let error = CloudCommand::new(&mut stdout, &mut stderr)
            .set_build_variables(
                &path,
                &CloudCommandOptions {
                    ci_system: Some("VisualStudioTeamServices".into()),
                    additional_variables: vec![
                        ("Name".into(), "one".into()),
                        ("Name".into(), "two".into()),
                    ],
                    ..Default::default()
                },
            )
            .unwrap_err();
        assert_eq!(error.error, CloudCommandError::DuplicateCloudVariable);
        assert_eq!(
            error.to_string(),
            "Cloud build variable \"Name\" specified more than once."
        );
        fs::remove_dir_all(path).unwrap();
    }
}
