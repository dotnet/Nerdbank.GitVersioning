// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! [Jenkins Git plugin](https://plugins.jenkins.io/git/#plugin-content-environment-variables) support.

use std::fs;
use std::io::{self, Write};
use std::path::Path;

use crate::cloud::{CloudBuild, EnvironmentVariables, env, is_nonempty, should_start_with};

/// Jenkins cloud-build integration.
pub struct Jenkins;

impl CloudBuild for Jenkins {
    fn name(&self) -> &'static str {
        "Jenkins"
    }

    fn is_applicable(&self) -> bool {
        is_nonempty(env("JENKINS_URL").as_deref())
    }

    fn is_pull_request(&self) -> bool {
        false
    }

    fn building_branch(&self) -> Option<String> {
        should_start_with(
            env("GIT_LOCAL_BRANCH").or_else(|| env("GIT_BRANCH")),
            "refs/heads/",
        )
    }

    fn building_tag(&self) -> Option<String> {
        None
    }

    fn git_commit_id(&self) -> Option<String> {
        env("GIT_COMMIT")
    }

    fn set_cloud_build_number(
        &self,
        build_number: &str,
        stdout: &mut dyn Write,
        _stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables> {
        if let Some(workspace) = env("WORKSPACE").filter(|value| !value.is_empty()) {
            fs::write(
                Path::new(&workspace).join("jenkins_build_number.txt"),
                build_number.as_bytes(),
            )?;
        }
        writeln!(stdout, "## GIT_VERSION: {build_number}")?;
        Ok(EnvironmentVariables::from([(
            "GIT_VERSION".into(),
            build_number.into(),
        )]))
    }

    fn set_cloud_build_variable(
        &self,
        name: &str,
        value: &str,
        _stdout: &mut dyn Write,
        _stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables> {
        Ok(EnvironmentVariables::from([(name.into(), value.into())]))
    }
}
