// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//! [GitHub Actions](https://docs.github.com/actions/learn-github-actions/variables) support.

use std::fs::OpenOptions;
use std::io::{self, Read, Seek, SeekFrom, Write};
use std::path::Path;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::{SystemTime, UNIX_EPOCH};

use crate::cloud::{
    CloudBuild, EnvironmentVariables, empty_variables, env, if_starts_with,
    variable_for_environment,
};

static DELIMITER_SEQUENCE: AtomicU64 = AtomicU64::new(0);

/// GitHub Actions cloud-build integration.
pub struct GitHubActions;

impl GitHubActions {
    fn ignore_github_ref() -> bool {
        env("IGNORE_GITHUB_REF").is_some_and(|value| value.eq_ignore_ascii_case("true"))
    }

    fn building_ref() -> Option<String> {
        (!Self::ignore_github_ref())
            .then(|| env("GITHUB_REF"))
            .flatten()
    }

    /// Formats an assignment for the UTF-8 `GITHUB_ENV` environment file.
    ///
    /// Single-line values use `name=value`. Multiline values use GitHub's heredoc
    /// syntax with a unique delimiter, and their line endings are normalized to LF.
    pub fn format_variable(name: &str, value: &str) -> String {
        if !value.contains(['\n', '\r']) {
            return format!("{name}={value}\n");
        }

        let delimiter = loop {
            let sequence = DELIMITER_SEQUENCE.fetch_add(1, Ordering::Relaxed);
            let nanos = SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .unwrap_or_default()
                .as_nanos();
            let candidate = format!(
                "NBGV_EOF_{:032x}",
                nanos ^ ((std::process::id() as u128) << 64) ^ sequence as u128
            );
            if !value.contains(&candidate) {
                break candidate;
            }
        };

        let normalized_value = value.replace("\r\n", "\n").replace('\r', "\n");
        format!("{name}<<{delimiter}\n{normalized_value}\n{delimiter}\n")
    }

    /// Atomically appends an assignment to a GitHub Actions environment file.
    ///
    /// An exclusive file lock covers the seek and complete write so parallel build
    /// processes cannot overwrite or interleave assignments. UTF-8 is written
    /// without a byte-order mark. An unterminated pre-existing line is terminated.
    pub fn append_variable(
        environment_file_path: impl AsRef<Path>,
        name: &str,
        value: Option<&str>,
    ) -> io::Result<()> {
        let path = environment_file_path.as_ref();
        if path.as_os_str().is_empty() {
            return Err(io::Error::new(
                io::ErrorKind::InvalidInput,
                "environment file path must not be empty",
            ));
        }
        if name.is_empty() {
            return Err(io::Error::new(
                io::ErrorKind::InvalidInput,
                "variable name must not be empty",
            ));
        }
        if name.contains(['\n', '\r']) {
            return Err(io::Error::new(
                io::ErrorKind::InvalidInput,
                "variable names must not contain newlines",
            ));
        }

        let bytes = Self::format_variable(name, value.unwrap_or_default()).into_bytes();
        let mut file = OpenOptions::new()
            .create(true)
            .read(true)
            .write(true)
            .truncate(false)
            .open(path)?;
        file.lock()?;

        let result = (|| {
            let length = file.seek(SeekFrom::End(0))?;
            if length > 0 {
                file.seek(SeekFrom::End(-1))?;
                let mut last = [0];
                file.read_exact(&mut last)?;
                if last[0] != b'\n' {
                    file.seek(SeekFrom::End(0))?;
                    file.write_all(b"\n")?;
                }
            }
            file.seek(SeekFrom::End(0))?;
            file.write_all(&bytes)?;
            file.flush()
        })();

        let unlock_result = file.unlock();
        result.and(unlock_result)
    }
}

impl CloudBuild for GitHubActions {
    fn name(&self) -> &'static str {
        "GitHubActions"
    }

    fn is_applicable(&self) -> bool {
        env("GITHUB_ACTIONS").as_deref() == Some("true")
    }

    fn is_pull_request(&self) -> bool {
        env("GITHUB_EVENT_NAME").as_deref() == Some("PullRequestEvent")
    }

    fn building_branch(&self) -> Option<String> {
        if_starts_with(Self::building_ref(), "refs/heads/")
    }

    fn building_tag(&self) -> Option<String> {
        if_starts_with(Self::building_ref(), "refs/tags/")
    }

    fn git_commit_id(&self) -> Option<String> {
        (!Self::ignore_github_ref())
            .then(|| env("GITHUB_SHA"))
            .flatten()
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
        name: &str,
        value: &str,
        _stdout: &mut dyn Write,
        _stderr: &mut dyn Write,
    ) -> io::Result<EnvironmentVariables> {
        let environment_file = env("GITHUB_ENV").ok_or_else(|| {
            io::Error::new(io::ErrorKind::InvalidInput, "GITHUB_ENV is not defined")
        })?;
        Self::append_variable(environment_file, name, Some(value))?;
        Ok(variable_for_environment(name, value))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::HashSet;
    use std::fs;
    use std::sync::Arc;
    use std::thread;

    fn test_file() -> std::path::PathBuf {
        std::env::current_dir()
            .unwrap()
            .join(format!("nbgv-github-env-{}.txt", unique_number()))
    }

    fn unique_number() -> u128 {
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos()
            ^ DELIMITER_SEQUENCE.fetch_add(1, Ordering::Relaxed) as u128
    }

    #[test]
    fn formats_single_line_values() {
        assert_eq!(
            GitHubActions::format_variable("Name", "Value"),
            "Name=Value\n"
        );
        assert_eq!(GitHubActions::format_variable("Name", ""), "Name=\n");
        assert_eq!(GitHubActions::format_variable("Name", "a=b"), "Name=a=b\n");
    }

    #[test]
    fn formats_multiline_values_as_heredocs() {
        for value in ["first\nsecond", "first\r\nsecond", "first\rsecond"] {
            let actual = GitHubActions::format_variable("Name", value);
            let lines: Vec<_> = actual.split('\n').collect();
            let delimiter = lines[0].strip_prefix("Name<<").unwrap();
            assert!(!delimiter.is_empty());
            assert_eq!(&lines[1..3], ["first", "second"]);
            assert_eq!(lines[3], delimiter);
            assert_eq!(lines[4], "");
            assert!(!actual.contains('\r'));
            assert!(!value.contains(delimiter));
        }

        assert_ne!(
            GitHubActions::format_variable("Name", "a\nb"),
            GitHubActions::format_variable("Name", "a\nb")
        );
    }

    #[test]
    fn appends_utf8_and_terminates_existing_line() {
        let path = test_file();
        fs::write(&path, "Existing=1").unwrap();
        GitHubActions::append_variable(&path, "Name", Some("é")).unwrap();
        assert_eq!(fs::read(&path).unwrap(), b"Existing=1\nName=\xc3\xa9\n");
        fs::remove_file(path).unwrap();
    }

    #[test]
    fn rejects_invalid_arguments() {
        let path = test_file();
        assert_eq!(
            GitHubActions::append_variable("", "Name", Some("Value"))
                .unwrap_err()
                .kind(),
            io::ErrorKind::InvalidInput
        );
        assert!(GitHubActions::append_variable(&path, "", Some("Value")).is_err());
        assert!(GitHubActions::append_variable(&path, "Na\nme", Some("Value")).is_err());
    }

    #[test]
    fn concurrent_appends_are_complete_and_distinct() {
        const WRITERS: usize = 16;
        const WRITES_PER_WRITER: usize = 100;
        let path = Arc::new(test_file());
        let handles: Vec<_> = (0..WRITERS)
            .map(|writer| {
                let path = Arc::clone(&path);
                thread::spawn(move || {
                    for index in 0..WRITES_PER_WRITER {
                        let name = format!("NBGV_{writer}_{index}");
                        let value = format!(
                            "{}{index}",
                            ((b'a' + writer as u8) as char).to_string().repeat(100)
                        );
                        GitHubActions::append_variable(path.as_ref(), &name, Some(&value)).unwrap();
                    }
                })
            })
            .collect();
        for handle in handles {
            handle.join().unwrap();
        }

        let expected: HashSet<_> = (0..WRITERS)
            .flat_map(|writer| {
                (0..WRITES_PER_WRITER).map(move |index| {
                    format!(
                        "NBGV_{writer}_{index}={}{index}",
                        ((b'a' + writer as u8) as char).to_string().repeat(100)
                    )
                })
            })
            .collect();
        let actual: HashSet<_> = fs::read_to_string(path.as_ref())
            .unwrap()
            .lines()
            .map(str::to_owned)
            .collect();
        assert_eq!(actual, expected);
        fs::remove_file(path.as_ref()).unwrap();
    }
}
