// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! Implementations for supported cloud-build services.

mod appveyor;
mod atlassian_bamboo;
mod bitbucket_cloud;
mod github_actions;
mod gitlab;
mod jenkins;
mod space_automation;
mod team_city;
mod travis;
mod visual_studio_team_services;

pub use appveyor::AppVeyor;
pub use atlassian_bamboo::AtlassianBamboo;
pub use bitbucket_cloud::BitbucketCloud;
pub use github_actions::GitHubActions;
pub use gitlab::GitLab;
pub use jenkins::Jenkins;
pub use space_automation::SpaceAutomation;
pub use team_city::TeamCity;
pub use travis::Travis;
pub use visual_studio_team_services::VisualStudioTeamServices;

#[cfg(test)]
mod tests {
    use super::*;
    use crate::cloud::{CloudBuild, active};
    use std::ffi::OsString;
    use std::sync::Mutex;

    static ENVIRONMENT_LOCK: Mutex<()> = Mutex::new(());

    const DETECTION_VARIABLES: &[&str] = &[
        "APPVEYOR",
        "SYSTEM_TEAMPROJECTID",
        "GITHUB_ACTIONS",
        "BUILD_VCS_NUMBER",
        "bamboo.buildKey",
        "JENKINS_URL",
        "GITLAB_CI",
        "TRAVIS",
        "JB_SPACE_GIT_REVISION",
        "BITBUCKET_PIPELINE_UUID",
        "BITBUCKET_STEP_UUID",
        "BITBUCKET_STEP_TRIGGERER_UUID",
    ];

    struct EnvironmentGuard(Vec<(&'static str, Option<OsString>)>);

    impl EnvironmentGuard {
        fn set(values: &[(&'static str, Option<&str>)]) -> Self {
            let names = DETECTION_VARIABLES
                .iter()
                .copied()
                .chain(values.iter().map(|(name, _)| *name))
                .collect::<std::collections::HashSet<_>>();
            let old_values = names
                .iter()
                .map(|name| (*name, std::env::var_os(name)))
                .collect();
            for name in DETECTION_VARIABLES {
                // SAFETY: Environment-mutating tests are serialized by ENVIRONMENT_LOCK.
                unsafe { std::env::remove_var(name) };
            }
            for (name, value) in values {
                match value {
                    Some(value) => {
                        // SAFETY: Environment-mutating tests are serialized by ENVIRONMENT_LOCK.
                        unsafe { std::env::set_var(name, value) };
                    }
                    None => {
                        // SAFETY: Environment-mutating tests are serialized by ENVIRONMENT_LOCK.
                        unsafe { std::env::remove_var(name) };
                    }
                }
            }
            Self(old_values)
        }
    }

    impl Drop for EnvironmentGuard {
        fn drop(&mut self) {
            for (name, value) in &self.0 {
                match value {
                    Some(value) => {
                        // SAFETY: Environment-mutating tests are serialized by ENVIRONMENT_LOCK.
                        unsafe { std::env::set_var(name, value) };
                    }
                    None => {
                        // SAFETY: Environment-mutating tests are serialized by ENVIRONMENT_LOCK.
                        unsafe { std::env::remove_var(name) };
                    }
                }
            }
        }
    }

    #[test]
    fn detects_each_provider() {
        type ProviderCase<'a> = (&'a dyn CloudBuild, &'a [(&'static str, Option<&'a str>)]);

        let _lock = ENVIRONMENT_LOCK.lock().unwrap();
        let cases: &[ProviderCase<'_>] = &[
            (&AppVeyor, &[("APPVEYOR", Some("True"))]),
            (
                &VisualStudioTeamServices,
                &[("SYSTEM_TEAMPROJECTID", Some("1"))],
            ),
            (&GitHubActions, &[("GITHUB_ACTIONS", Some("true"))]),
            (&TeamCity, &[("BUILD_VCS_NUMBER", Some("abc"))]),
            (&AtlassianBamboo, &[("bamboo.buildKey", Some("key"))]),
            (&Jenkins, &[("JENKINS_URL", Some("https://jenkins/"))]),
            (&GitLab, &[("GITLAB_CI", Some("true"))]),
            (&Travis, &[("TRAVIS", Some("true"))]),
            (&SpaceAutomation, &[("JB_SPACE_GIT_REVISION", Some("abc"))]),
            (
                &BitbucketCloud,
                &[
                    ("BITBUCKET_PIPELINE_UUID", Some("pipeline")),
                    ("BITBUCKET_STEP_UUID", Some("step")),
                    ("BITBUCKET_STEP_TRIGGERER_UUID", Some("trigger")),
                ],
            ),
        ];

        for (provider, variables) in cases {
            let _environment = EnvironmentGuard::set(variables);
            assert!(
                provider.is_applicable(),
                "{} was not detected",
                provider.name()
            );
            assert_eq!(active().unwrap().name(), provider.name());
        }
    }

    #[test]
    fn provider_refs_match_managed_behavior() {
        let _lock = ENVIRONMENT_LOCK.lock().unwrap();

        {
            let _environment = EnvironmentGuard::set(&[
                ("APPVEYOR", Some("True")),
                ("APPVEYOR_REPO_BRANCH", Some("main")),
                ("APPVEYOR_PULL_REQUEST_NUMBER", Some("12")),
                ("APPVEYOR_REPO_TAG", Some("TRUE")),
                ("APPVEYOR_REPO_TAG_NAME", Some("v1")),
            ]);
            assert_eq!(AppVeyor.building_branch(), None);
            assert_eq!(AppVeyor.building_tag().as_deref(), Some("refs/tags/v1"));
            assert!(AppVeyor.is_pull_request());
        }
        {
            let _environment = EnvironmentGuard::set(&[
                ("BUILD_SOURCEBRANCH", Some("refs/pull/12/merge")),
                ("SYSTEM_TEAMPROJECTID", Some("1")),
            ]);
            assert!(VisualStudioTeamServices.is_pull_request());
            assert_eq!(VisualStudioTeamServices.building_branch(), None);
        }
        {
            let _environment = EnvironmentGuard::set(&[
                ("CI_COMMIT_TAG", Some("1.0.0")),
                ("CI_COMMIT_REF_NAME", Some("main")),
                ("CI_COMMIT_SHA", Some("abc")),
                ("GITLAB_CI", Some("true")),
            ]);
            assert_eq!(GitLab.building_branch(), None);
            assert_eq!(GitLab.building_tag().as_deref(), Some("refs/tags/1.0.0"));
            assert_eq!(GitLab.git_commit_id().as_deref(), Some("abc"));
        }
        {
            let _environment = EnvironmentGuard::set(&[
                ("BITBUCKET_BRANCH", Some("main")),
                ("BITBUCKET_TAG", Some("v1")),
                ("BITBUCKET_PR_ID", Some("  ")),
            ]);
            assert_eq!(
                BitbucketCloud.building_branch().as_deref(),
                Some("refs/heads/main")
            );
            assert_eq!(
                BitbucketCloud.building_tag().as_deref(),
                Some("refs/tags/v1")
            );
            assert!(!BitbucketCloud.is_pull_request());
        }
    }

    #[test]
    fn service_message_outputs_and_environment_updates_are_exact() {
        let mut output = Vec::new();
        let mut errors = Vec::new();

        let variables = VisualStudioTeamServices
            .set_cloud_build_number("1.2.3", &mut output, &mut errors)
            .unwrap();
        assert_eq!(output, b"##vso[build.updatebuildnumber]1.2.3\n");
        assert_eq!(variables["BUILD_BUILDNUMBER"], "1.2.3");

        output.clear();
        let variables = VisualStudioTeamServices
            .set_cloud_build_variable("Git.Build.Version", "value", &mut output, &mut errors)
            .unwrap();
        assert_eq!(
            output,
            b"##vso[task.setvariable variable=Git.Build.Version;]value\n\
              ##vso[task.setvariable variable=Git.Build.Version;isOutput=true;]value\n"
        );
        assert_eq!(variables["GIT_BUILD_VERSION"], "value");

        output.clear();
        let variables = VisualStudioTeamServices
            .set_cloud_build_number("1%\r\n2", &mut output, &mut errors)
            .unwrap();
        assert_eq!(output, b"##vso[build.updatebuildnumber]1%AZP25%0D%0A2\n");
        assert_eq!(variables["BUILD_BUILDNUMBER"], "1%\r\n2");

        output.clear();
        let variables = VisualStudioTeamServices
            .set_cloud_build_variable("N%;]\r\n", "V%\r\n", &mut output, &mut errors)
            .unwrap();
        assert_eq!(
            output,
            b"##vso[task.setvariable variable=N%AZP25%3B%5D%0D%0A;]V%AZP25%0D%0A\n\
              ##vso[task.setvariable variable=N%AZP25%3B%5D%0D%0A;isOutput=true;]V%AZP25%0D%0A\n"
        );
        assert_eq!(variables["N%;]\r\n"], "V%\r\n");

        output.clear();
        TeamCity
            .set_cloud_build_variable("Name", "Value", &mut output, &mut errors)
            .unwrap();
        assert_eq!(
            output,
            b"##teamcity[setParameter name='Name' value='Value']\n\
              ##teamcity[setParameter name='system.Name' value='Value']\n"
        );

        output.clear();
        TeamCity
            .set_cloud_build_number("|'\n\r[]", &mut output, &mut errors)
            .unwrap();
        assert_eq!(output, b"##teamcity[buildNumber '|||'|n|r|[|]']\n");

        output.clear();
        TeamCity
            .set_cloud_build_variable("|'\n\r[]", "|'\n\r[]", &mut output, &mut errors)
            .unwrap();
        assert_eq!(
            output,
            b"##teamcity[setParameter name='|||'|n|r|[|]' value='|||'|n|r|[|]']\n\
              ##teamcity[setParameter name='system.|||'|n|r|[|]' value='|||'|n|r|[|]']\n"
        );
    }
}
