// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! [Travis CI](https://docs.travis-ci.com/user/environment-variables/#default-environment-variables) support.

use std::io::{self, Write};

use crate::cloud::{
    CloudBuild, EnvironmentVariables, empty_variables, env, is_nonempty, should_start_with,
};

/// Travis CI cloud-build integration.
pub struct Travis;

impl CloudBuild for Travis {
    fn name(&self) -> &'static str {
        "Travis"
    }

    fn is_applicable(&self) -> bool {
        is_nonempty(env("TRAVIS").as_deref())
    }

    fn is_pull_request(&self) -> bool {
        is_nonempty(env("TRAVIS_PULL_REQUEST_BRANCH").as_deref())
    }

    fn building_branch(&self) -> Option<String> {
        should_start_with(env("TRAVIS_BRANCH"), "refs/heads/")
    }

    fn building_tag(&self) -> Option<String> {
        env("TRAVIS_TAG")
    }

    fn git_commit_id(&self) -> Option<String> {
        env("TRAVIS_COMMIT")
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
