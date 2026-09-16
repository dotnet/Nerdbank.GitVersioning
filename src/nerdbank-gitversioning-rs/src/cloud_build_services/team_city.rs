// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! [TeamCity](https://www.jetbrains.com/help/teamcity/predefined-build-parameters.html) support.

use std::io::{self, Write};

use crate::cloud::{
    CloudBuild, EnvironmentVariables, empty_variables, env, if_starts_with, is_nonempty,
};

/// TeamCity cloud-build integration.
pub struct TeamCity;

impl CloudBuild for TeamCity {
    fn name(&self) -> &'static str {
        "TeamCity"
    }

    fn is_applicable(&self) -> bool {
        is_nonempty(self.git_commit_id().as_deref())
    }

    fn is_pull_request(&self) -> bool {
        false
    }

    fn building_branch(&self) -> Option<String> {
        if_starts_with(env("BUILD_GIT_BRANCH"), "refs/heads/")
    }

    fn building_tag(&self) -> Option<String> {
        if_starts_with(env("BUILD_GIT_BRANCH"), "refs/tags/")
    }

    fn git_commit_id(&self) -> Option<String> {
        env("BUILD_VCS_NUMBER")
    }

    fn set_cloud_build_number(
        &self,
        build_number: &str,
        stdout: &mut dyn Write,
        _stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables> {
        writeln!(stdout, "##teamcity[buildNumber '{}']", escape(build_number))?;
        Ok(empty_variables())
    }

    fn set_cloud_build_variable(
        &self,
        name: &str,
        value: &str,
        stdout: &mut dyn Write,
        _stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables> {
        let name = escape(name);
        let value = escape(value);
        writeln!(
            stdout,
            "##teamcity[setParameter name='{name}' value='{value}']"
        )?;
        writeln!(
            stdout,
            "##teamcity[setParameter name='system.{name}' value='{value}']"
        )?;
        Ok(empty_variables())
    }
}

fn escape(value: &str) -> String {
    value
        .replace('|', "||")
        .replace('\'', "|'")
        .replace('\n', "|n")
        .replace('\r', "|r")
        .replace('[', "|[")
        .replace(']', "|]")
}
