// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! [GitLab CI](https://docs.gitlab.com/ci/variables/) support.

use std::io::{self, Write};

use crate::cloud::{CloudBuild, EnvironmentVariables, empty_variables, env, is_nonempty};

/// GitLab CI integration.
pub struct GitLab;

impl CloudBuild for GitLab {
    fn name(&self) -> &'static str {
        "GitLab"
    }

    fn is_applicable(&self) -> bool {
        is_nonempty(env("GITLAB_CI").as_deref())
    }

    fn is_pull_request(&self) -> bool {
        false
    }

    fn building_branch(&self) -> Option<String> {
        env("CI_COMMIT_TAG").is_none().then(|| {
            format!(
                "refs/heads/{}",
                env("CI_COMMIT_REF_NAME").unwrap_or_default()
            )
        })
    }

    fn building_tag(&self) -> Option<String> {
        env("CI_COMMIT_TAG")
            .filter(|tag| !tag.is_empty())
            .map(|tag| format!("refs/tags/{tag}"))
    }

    fn git_commit_id(&self) -> Option<String> {
        env("CI_COMMIT_SHA")
    }

    fn set_cloud_build_number(
        &self,
        _build_number: &str,
        _stdout: &mut dyn Write,
        _stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables> {
        Ok(empty_variables())
    }

    fn set_cloud_build_variable(
        &self,
        _name: &str,
        _value: &str,
        _stdout: &mut dyn Write,
        _stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables> {
        Ok(empty_variables())
    }
}
