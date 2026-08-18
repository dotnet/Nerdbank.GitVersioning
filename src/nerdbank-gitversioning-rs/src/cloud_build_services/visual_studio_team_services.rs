// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! [Azure DevOps](https://learn.microsoft.com/azure/devops/pipelines/build/variables) support.

use std::io::{self, Write};

use crate::cloud::{
    CloudBuild, EnvironmentVariables, env, if_starts_with, is_nonempty, variable_for_environment,
};

/// Azure DevOps (formerly Visual Studio Team Services) integration.
pub struct VisualStudioTeamServices;

impl CloudBuild for VisualStudioTeamServices {
    fn name(&self) -> &'static str {
        "VisualStudioTeamServices"
    }

    fn is_applicable(&self) -> bool {
        is_nonempty(env("SYSTEM_TEAMPROJECTID").as_deref())
    }

    fn is_pull_request(&self) -> bool {
        env("BUILD_SOURCEBRANCH").is_some_and(|value| value.starts_with("refs/pull/"))
    }

    fn building_branch(&self) -> Option<String> {
        if_starts_with(env("BUILD_SOURCEBRANCH"), "refs/heads/")
    }

    fn building_tag(&self) -> Option<String> {
        if_starts_with(env("BUILD_SOURCEBRANCH"), "refs/tags/")
    }

    fn git_commit_id(&self) -> Option<String> {
        None
    }

    fn set_cloud_build_number(
        &self,
        build_number: &str,
        stdout: &mut dyn Write,
        _stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables> {
        writeln!(
            stdout,
            "##vso[build.updatebuildnumber]{}",
            escape_data(build_number)
        )?;
        Ok(variable_for_environment("Build.BuildNumber", build_number))
    }

    fn set_cloud_build_variable(
        &self,
        name: &str,
        value: &str,
        stdout: &mut dyn Write,
        _stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables> {
        let escaped_name = escape_property(name);
        let escaped_value = escape_data(value);
        writeln!(
            stdout,
            "##vso[task.setvariable variable={escaped_name};]{escaped_value}"
        )?;
        writeln!(
            stdout,
            "##vso[task.setvariable variable={escaped_name};isOutput=true;]{escaped_value}"
        )?;
        Ok(variable_for_environment(name, value))
    }
}

fn escape_data(value: &str) -> String {
    value
        .replace('%', "%AZP25")
        .replace('\r', "%0D")
        .replace('\n', "%0A")
}

fn escape_property(value: &str) -> String {
    escape_data(value).replace(']', "%5D").replace(';', "%3B")
}
