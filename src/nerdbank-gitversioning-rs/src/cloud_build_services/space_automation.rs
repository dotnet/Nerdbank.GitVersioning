// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! [Space Automation](https://www.jetbrains.com/help/space/automation-environment-variables.html) support.

use std::io::{self, Write};

use crate::cloud::{CloudBuild, EnvironmentVariables, empty_variables, env, if_starts_with};

/// JetBrains Space Automation integration.
pub struct SpaceAutomation;

impl CloudBuild for SpaceAutomation {
    fn name(&self) -> &'static str {
        "SpaceAutomation"
    }

    fn is_applicable(&self) -> bool {
        self.git_commit_id().is_some()
    }

    fn is_pull_request(&self) -> bool {
        false
    }

    fn building_branch(&self) -> Option<String> {
        if_starts_with(env("JB_SPACE_GIT_BRANCH"), "refs/heads/")
    }

    fn building_tag(&self) -> Option<String> {
        if_starts_with(env("JB_SPACE_GIT_BRANCH"), "refs/tags/")
    }

    fn git_commit_id(&self) -> Option<String> {
        env("JB_SPACE_GIT_REVISION")
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
