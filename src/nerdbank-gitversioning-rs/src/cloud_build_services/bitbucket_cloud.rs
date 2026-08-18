// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! [Bitbucket Cloud](https://support.atlassian.com/bitbucket-cloud/docs/variables-and-secrets/) support.

use std::io::{self, Write};

use crate::cloud::{CloudBuild, EnvironmentVariables, empty_variables, env, should_start_with};

/// Bitbucket Cloud Pipelines integration.
pub struct BitbucketCloud;

impl CloudBuild for BitbucketCloud {
    fn name(&self) -> &'static str {
        "BitbucketCloud"
    }

    fn is_applicable(&self) -> bool {
        [
            "BITBUCKET_PIPELINE_UUID",
            "BITBUCKET_STEP_UUID",
            "BITBUCKET_STEP_TRIGGERER_UUID",
        ]
        .into_iter()
        .all(|name| env(name).is_some_and(|value| !value.trim().is_empty()))
    }

    fn is_pull_request(&self) -> bool {
        env("BITBUCKET_PR_ID").is_some_and(|value| !value.trim().is_empty())
    }

    fn building_branch(&self) -> Option<String> {
        should_start_with(env("BITBUCKET_BRANCH"), "refs/heads/")
    }

    fn building_tag(&self) -> Option<String> {
        should_start_with(env("BITBUCKET_TAG"), "refs/tags/")
    }

    fn git_commit_id(&self) -> Option<String> {
        env("BITBUCKET_COMMIT")
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
