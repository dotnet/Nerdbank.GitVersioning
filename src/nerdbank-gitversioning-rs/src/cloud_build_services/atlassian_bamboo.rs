// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! [Atlassian Bamboo](https://confluence.atlassian.com/bamboo/bamboo-variables-289277087.html) support.

use std::io::{self, Write};

use crate::cloud::{
    CloudBuild, EnvironmentVariables, empty_variables, env, is_nonempty, should_start_with,
};

/// Atlassian Bamboo cloud-build integration.
pub struct AtlassianBamboo;

impl CloudBuild for AtlassianBamboo {
    fn name(&self) -> &'static str {
        "AtlassianBamboo"
    }

    fn is_applicable(&self) -> bool {
        is_nonempty(env("bamboo.buildKey").as_deref())
    }

    fn is_pull_request(&self) -> bool {
        false
    }

    fn building_branch(&self) -> Option<String> {
        should_start_with(env("bamboo.planRepository.branch"), "refs/heads/")
    }

    fn building_tag(&self) -> Option<String> {
        None
    }

    fn git_commit_id(&self) -> Option<String> {
        env("bamboo.planRepository.revision")
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
