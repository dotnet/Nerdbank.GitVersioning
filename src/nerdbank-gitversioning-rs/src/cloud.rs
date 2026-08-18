// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! Cloud-build provider discovery and common functionality.

use std::collections::HashMap;
use std::io::{self, Write};

use crate::cloud_build_services::{
    AppVeyor, AtlassianBamboo, BitbucketCloud, GitHubActions, GitLab, Jenkins, SpaceAutomation,
    TeamCity, Travis, VisualStudioTeamServices,
};

/// Environment changes that should be applied by the caller.
pub type EnvironmentVariables = HashMap<String, String>;

/// Describes a supported cloud-build service.
pub trait CloudBuild: Send + Sync {
    /// Returns the stable name of this provider.
    fn name(&self) -> &'static str;

    /// Returns whether this provider recognizes the current environment.
    fn is_applicable(&self) -> bool;

    /// Returns whether the current build validates a pull request.
    fn is_pull_request(&self) -> bool;

    /// Returns the fully qualified branch ref being built, when known.
    fn building_branch(&self) -> Option<String>;

    /// Returns the fully qualified tag ref being built, when known.
    fn building_tag(&self) -> Option<String>;

    /// Returns the Git commit ID being built, when supplied by the provider.
    fn git_commit_id(&self) -> Option<String>;

    /// Updates the provider's build number and returns environment changes for the caller.
    fn set_cloud_build_number(
        &self,
        build_number: &str,
        stdout: &mut dyn Write,
        stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables>;

    /// Sets a provider variable and returns environment changes for the caller.
    fn set_cloud_build_variable(
        &self,
        name: &str,
        value: &str,
        stdout: &mut dyn Write,
        stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables>;
}

/// Creates the supported providers in detection-priority order.
pub fn supported_cloud_builds() -> Vec<Box<dyn CloudBuild>> {
    vec![
        Box::new(AppVeyor),
        Box::new(VisualStudioTeamServices),
        Box::new(GitHubActions),
        Box::new(TeamCity),
        Box::new(AtlassianBamboo),
        Box::new(Jenkins),
        Box::new(GitLab),
        Box::new(Travis),
        Box::new(SpaceAutomation),
        Box::new(BitbucketCloud),
    ]
}

/// Returns the first provider that recognizes the current environment.
pub fn active() -> Option<Box<dyn CloudBuild>> {
    supported_cloud_builds()
        .into_iter()
        .find(|provider| provider.is_applicable())
}

/// Adds `prefix` to a nonempty value unless it is already present.
pub(crate) fn should_start_with(value: Option<String>, prefix: &str) -> Option<String> {
    value.map(|value| {
        if value.is_empty() || value.starts_with(prefix) {
            value
        } else {
            format!("{prefix}{value}")
        }
    })
}

/// Retains a value only when it starts with `prefix`.
pub(crate) fn if_starts_with(value: Option<String>, prefix: &str) -> Option<String> {
    value.filter(|value| value.starts_with(prefix))
}

pub(crate) fn env(name: &str) -> Option<String> {
    std::env::var_os(name).map(|value| value.to_string_lossy().into_owned())
}

pub(crate) fn is_nonempty(value: Option<&str>) -> bool {
    value.is_some_and(|value| !value.is_empty())
}

pub(crate) fn empty_variables() -> EnvironmentVariables {
    EnvironmentVariables::new()
}

pub(crate) fn variable_for_environment(name: &str, value: &str) -> EnvironmentVariables {
    EnvironmentVariables::from([(name.to_uppercase().replace('.', "_"), value.to_owned())])
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ref_helpers_match_cloud_build_conventions() {
        assert_eq!(
            should_start_with(Some("main".into()), "refs/heads/").as_deref(),
            Some("refs/heads/main")
        );
        assert_eq!(
            should_start_with(Some("refs/heads/main".into()), "refs/heads/").as_deref(),
            Some("refs/heads/main")
        );
        assert_eq!(should_start_with(None, "refs/heads/"), None);
        assert_eq!(
            if_starts_with(Some("refs/tags/v1".into()), "refs/tags/").as_deref(),
            Some("refs/tags/v1")
        );
        assert_eq!(if_starts_with(Some("main".into()), "refs/heads/"), None);
    }

    #[test]
    fn provider_order_matches_managed_library() {
        let names: Vec<_> = supported_cloud_builds()
            .iter()
            .map(|provider| provider.name())
            .collect();
        assert_eq!(
            names,
            [
                "AppVeyor",
                "VisualStudioTeamServices",
                "GitHubActions",
                "TeamCity",
                "AtlassianBamboo",
                "Jenkins",
                "GitLab",
                "Travis",
                "SpaceAutomation",
                "BitbucketCloud",
            ]
        );
    }
}
