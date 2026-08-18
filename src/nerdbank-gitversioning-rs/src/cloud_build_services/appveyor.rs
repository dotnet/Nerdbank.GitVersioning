// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! [AppVeyor](https://www.appveyor.com/docs/environment-variables/) support.

use std::io::{self, ErrorKind, Write};
use std::process::Command;

use crate::cloud::{CloudBuild, EnvironmentVariables, empty_variables, env, is_nonempty};

/// AppVeyor cloud-build integration.
pub struct AppVeyor;

impl AppVeyor {
    fn run(arguments: &[&str], stderr: &mut dyn Write) -> io::Result<()> {
        if is_nonempty(env("_NBGV_UnitTest").as_deref()) {
            return Ok(());
        }

        match Command::new("appveyor").args(arguments).status() {
            Ok(_) => Ok(()),
            Err(error) if error.kind() == ErrorKind::NotFound => {
                writeln!(
                    stderr,
                    "Could not find appveyor tool to set cloud build variable."
                )
            }
            Err(error) => Err(error),
        }
    }
}

impl CloudBuild for AppVeyor {
    fn name(&self) -> &'static str {
        "AppVeyor"
    }

    fn is_applicable(&self) -> bool {
        is_nonempty(env("APPVEYOR").as_deref())
    }

    fn is_pull_request(&self) -> bool {
        is_nonempty(env("APPVEYOR_PULL_REQUEST_NUMBER").as_deref())
    }

    fn building_branch(&self) -> Option<String> {
        (!self.is_pull_request())
            .then(|| env("APPVEYOR_REPO_BRANCH"))
            .flatten()
            .filter(|branch| !branch.is_empty())
            .map(|branch| format!("refs/heads/{branch}"))
    }

    fn building_tag(&self) -> Option<String> {
        env("APPVEYOR_REPO_TAG")
            .is_some_and(|value| value.eq_ignore_ascii_case("true"))
            .then(|| {
                format!(
                    "refs/tags/{}",
                    env("APPVEYOR_REPO_TAG_NAME").unwrap_or_default()
                )
            })
    }

    fn git_commit_id(&self) -> Option<String> {
        None
    }

    fn set_cloud_build_number(
        &self,
        build_number: &str,
        _stdout: &mut dyn Write,
        stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables> {
        Self::run(&["UpdateBuild", "-Version", build_number], stderr)?;
        Ok(empty_variables())
    }

    fn set_cloud_build_variable(
        &self,
        name: &str,
        value: &str,
        _stdout: &mut dyn Write,
        stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables> {
        Self::run(&["SetVariable", "-Name", name, "-Value", value], stderr)?;
        Ok(empty_variables())
    }
}
