//! Cargo build-script integration for [Nerdbank.GitVersioning][nbgv].
//!
//! Call [`emit`] from `build.rs`, then use the emitted `NBGV_*` variables with
//! Rust's `env!` macro. [`Config::emit_and_write_module`] additionally writes an
//! `nbgv.rs` file to `OUT_DIR` containing the typed `NBGV_VERSION` constant.
//!
//! [nbgv]: https://github.com/dotnet/Nerdbank.GitVersioning

use chrono::DateTime;
use serde::Deserialize;
use serde_json::Value;
use std::env;
use std::ffi::{OsStr, OsString};
use std::fmt;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};
use std::process::Command;

const SCHEMA_VERSION: u64 = 1;

/// The version information returned by `nbgv`.
#[derive(Clone, Debug, Eq, PartialEq)]
pub struct Version {
    /// The complete SemVer 2.0 version.
    pub sem_ver2: String,
    /// The numeric `System.Version` representation.
    pub version: String,
    /// The Git version height before applying `versionHeightOffset`.
    pub version_height: i64,
    /// The full Git object ID, or `None` when Git information is unavailable.
    pub git_commit_id: Option<String>,
    /// The abbreviated Git object ID, or `None` when Git information is unavailable.
    pub git_commit_id_short: Option<String>,
    /// The UTC commit timestamp, or `None` when Git information is unavailable.
    pub git_commit_date: Option<String>,
    /// Whether public release mode is active.
    pub public_release: bool,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct AdapterDocument {
    schema_version: u64,
    sem_ver2: String,
    version: String,
    version_height: i64,
    git_commit_id: Option<String>,
    git_commit_id_short: Option<String>,
    git_commit_date: Option<String>,
    public_release: bool,
}

/// Configures an `nbgv` invocation.
#[derive(Clone, Debug, Default)]
pub struct Config {
    executable: Option<OsString>,
    project_dir: Option<PathBuf>,
}

impl Config {
    /// Creates a configuration that versions `CARGO_MANIFEST_DIR`.
    pub fn new() -> Self {
        Self::default()
    }

    /// Selects the exact executable path or command name to launch.
    ///
    /// This is one executable, not a shell command or a command with arguments.
    pub fn executable(mut self, executable: impl AsRef<OsStr>) -> Self {
        self.executable = Some(executable.as_ref().to_owned());
        self
    }

    /// Selects the project directory passed to `nbgv` as its working directory.
    pub fn project_dir(mut self, project_dir: impl AsRef<Path>) -> Self {
        self.project_dir = Some(project_dir.as_ref().to_owned());
        self
    }

    /// Invokes `nbgv get-version --format adapter` and validates its response.
    pub fn get_version(&self) -> Result<Version, Error> {
        let executable = self
            .executable
            .clone()
            .or_else(|| env::var_os("NBGV_EXECUTABLE"))
            .unwrap_or_else(|| OsString::from("nbgv"));
        let project_dir = self.effective_project_dir()?;
        let output = Command::new(&executable)
            .args(["get-version", "--format", "adapter"])
            .current_dir(&project_dir)
            .output()
            .map_err(|source| Error::Launch {
                executable: executable.clone(),
                source,
            })?;

        if !output.status.success() {
            return Err(Error::CommandFailed {
                executable,
                code: output.status.code(),
                stderr: String::from_utf8_lossy(&output.stderr).trim().to_owned(),
            });
        }

        parse_adapter_json(&output.stdout)
    }

    /// Invokes `nbgv` and writes Cargo environment and rebuild instructions.
    pub fn emit(&self) -> Result<Version, Error> {
        let version = self.get_version()?;
        self.emit_rerun_instructions()?;
        emit_environment(&version);
        Ok(version)
    }

    /// Calls [`Config::emit`] and writes `OUT_DIR/nbgv.rs`.
    ///
    /// Include that file at crate scope with
    /// `include!(concat!(env!("OUT_DIR"), "/nbgv.rs"));`.
    pub fn emit_and_write_module(&self) -> Result<Version, Error> {
        let version = self.emit()?;
        let out_dir = env::var_os("OUT_DIR").ok_or(Error::MissingEnvironment("OUT_DIR"))?;
        write_module(&PathBuf::from(out_dir).join("nbgv.rs"), &version)?;
        Ok(version)
    }

    fn effective_project_dir(&self) -> Result<PathBuf, Error> {
        match &self.project_dir {
            Some(path) => Ok(path.clone()),
            None => env::var_os("CARGO_MANIFEST_DIR")
                .map(PathBuf::from)
                .ok_or(Error::MissingEnvironment("CARGO_MANIFEST_DIR")),
        }
    }

    fn emit_rerun_instructions(&self) -> Result<(), Error> {
        for name in [
            "NBGV_EXECUTABLE",
            "NBGV_GitEngine",
            "PATH",
            "PublicRelease",
            "GITHUB_ACTOR",
            "DEPENDABOT",
            "SYSTEM_TEAMPROJECTID",
            "BUILD_SOURCEBRANCH",
            "TRAVIS",
            "TRAVIS_BRANCH",
            "TRAVIS_TAG",
            "TRAVIS_COMMIT",
            "TRAVIS_PULL_REQUEST_BRANCH",
            "JB_SPACE_GIT_REVISION",
            "JB_SPACE_GIT_BRANCH",
            "BUILD_VCS_NUMBER",
            "BUILD_GIT_BRANCH",
            "GITLAB_CI",
            "CI_COMMIT_TAG",
            "CI_COMMIT_REF_NAME",
            "CI_COMMIT_SHA",
            "bamboo.buildKey",
            "bamboo.planRepository.branch",
            "bamboo.planRepository.revision",
            "BITBUCKET_PIPELINE_UUID",
            "BITBUCKET_STEP_UUID",
            "BITBUCKET_STEP_TRIGGERER_UUID",
            "BITBUCKET_PR_ID",
            "BITBUCKET_BRANCH",
            "BITBUCKET_TAG",
            "BITBUCKET_COMMIT",
            "JENKINS_URL",
            "GIT_COMMIT",
            "GIT_LOCAL_BRANCH",
            "GIT_BRANCH",
            "WORKSPACE",
            "APPVEYOR",
            "APPVEYOR_REPO_BRANCH",
            "APPVEYOR_REPO_TAG",
            "APPVEYOR_REPO_TAG_NAME",
            "APPVEYOR_PULL_REQUEST_NUMBER",
            "GITHUB_ACTIONS",
            "GITHUB_EVENT_NAME",
            "GITHUB_SHA",
            "GITHUB_REF",
            "IGNORE_GITHUB_REF",
        ] {
            println!("cargo::rerun-if-env-changed={name}");
        }
        let selected_executable = self
            .executable
            .clone()
            .or_else(|| env::var_os("NBGV_EXECUTABLE"));
        if let Some(path) = selected_executable
            .map(PathBuf::from)
            .filter(|path| path.is_file())
        {
            println!("cargo::rerun-if-changed={}", path.display());
        }

        let project_dir = self.effective_project_dir()?;
        let ancestors: Vec<_> = project_dir.ancestors().map(Path::to_path_buf).collect();
        let repository_root = ancestors
            .iter()
            .find(|path| path.join(".git").exists())
            .cloned();

        for ancestor in &ancestors {
            println!(
                "cargo::rerun-if-changed={}",
                ancestor.join("version.json").display()
            );
            if Some(ancestor) == repository_root.as_ref() {
                break;
            }
        }

        if let Some(root) = repository_root {
            for path in git_inputs(&root)? {
                println!("cargo::rerun-if-changed={}", path.display());
            }
        }

        Ok(())
    }
}

/// Invokes `nbgv` using the default configuration and emits Cargo instructions.
pub fn emit() -> Result<Version, Error> {
    Config::new().emit()
}

/// Invokes `nbgv` and emits Cargo instructions and a generated Rust module.
pub fn emit_and_write_module() -> Result<Version, Error> {
    Config::new().emit_and_write_module()
}

/// Emits a build-script invocation that panics with a useful error on failure.
///
/// With no argument it uses [`Config::new`]. An optional expression may yield a
/// customized [`Config`].
#[macro_export]
macro_rules! emit {
    () => {
        $crate::Config::new()
            .emit_and_write_module()
            .expect("Nerdbank.GitVersioning failed")
    };
    ($config:expr) => {
        ($config)
            .emit_and_write_module()
            .expect("Nerdbank.GitVersioning failed")
    };
}

/// An error produced by the adapter.
#[derive(Debug)]
pub enum Error {
    /// A required Cargo environment variable was missing.
    MissingEnvironment(&'static str),
    /// The selected executable could not be launched.
    Launch {
        /// The executable selected by the documented resolution policy.
        executable: OsString,
        /// The operating system error.
        source: io::Error,
    },
    /// `nbgv` returned an unsuccessful exit status.
    CommandFailed {
        /// The executable that was launched.
        executable: OsString,
        /// The process exit code, if the process exited normally.
        code: Option<i32>,
        /// Standard error produced by `nbgv`.
        stderr: String,
    },
    /// The adapter response did not conform to schema version 1.
    InvalidResponse(String),
    /// A filesystem operation failed.
    Io {
        /// The path involved in the operation.
        path: PathBuf,
        /// The operating system error.
        source: io::Error,
    },
}

impl fmt::Display for Error {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::MissingEnvironment(name) => {
                write!(formatter, "required environment variable {name} is not set")
            }
            Self::Launch { executable, source } => write!(
                formatter,
                "failed to launch nbgv executable {:?}: {source}",
                executable
            ),
            Self::CommandFailed {
                executable,
                code,
                stderr,
            } => write!(
                formatter,
                "nbgv executable {:?} exited with {}{}",
                executable,
                code.map_or_else(|| "no exit code".to_owned(), |value| value.to_string()),
                if stderr.is_empty() {
                    String::new()
                } else {
                    format!(": {stderr}")
                }
            ),
            Self::InvalidResponse(message) => write!(formatter, "invalid nbgv response: {message}"),
            Self::Io { path, source } => {
                write!(formatter, "failed to access {}: {source}", path.display())
            }
        }
    }
}

impl std::error::Error for Error {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Self::Launch { source, .. } | Self::Io { source, .. } => Some(source),
            _ => None,
        }
    }
}

fn parse_adapter_json(bytes: &[u8]) -> Result<Version, Error> {
    let value: Value = serde_json::from_slice(bytes)
        .map_err(|error| Error::InvalidResponse(format!("malformed JSON: {error}")))?;
    let object = value
        .as_object()
        .ok_or_else(|| Error::InvalidResponse("the root must be a JSON object".to_owned()))?;
    let schema_version = object
        .get("schemaVersion")
        .and_then(Value::as_u64)
        .ok_or_else(|| Error::InvalidResponse("schemaVersion must be an integer".to_owned()))?;
    if schema_version != SCHEMA_VERSION {
        return Err(Error::InvalidResponse(format!(
            "unsupported schemaVersion {schema_version}; expected {SCHEMA_VERSION}"
        )));
    }
    for name in [
        "semVer2",
        "version",
        "versionHeight",
        "gitCommitId",
        "gitCommitIdShort",
        "gitCommitDate",
        "publicRelease",
    ] {
        if !object.contains_key(name) {
            return Err(Error::InvalidResponse(format!(
                "required property {name} is missing"
            )));
        }
    }

    let document: AdapterDocument =
        serde_json::from_value(value).map_err(|error| Error::InvalidResponse(error.to_string()))?;
    debug_assert_eq!(document.schema_version, SCHEMA_VERSION);
    validate_document(&document)?;
    Ok(Version {
        sem_ver2: document.sem_ver2,
        version: document.version,
        version_height: document.version_height,
        git_commit_id: document.git_commit_id,
        git_commit_id_short: document.git_commit_id_short,
        git_commit_date: document.git_commit_date,
        public_release: document.public_release,
    })
}

fn validate_document(document: &AdapterDocument) -> Result<(), Error> {
    semver::Version::parse(&document.sem_ver2)
        .map_err(|error| Error::InvalidResponse(format!("semVer2 is invalid: {error}")))?;

    let components: Vec<_> = document.version.split('.').collect();
    if !(2..=4).contains(&components.len())
        || components
            .iter()
            .any(|part| part.is_empty() || !part.bytes().all(|byte| byte.is_ascii_digit()))
    {
        return Err(Error::InvalidResponse(
            "version must contain two to four non-negative integer components".to_owned(),
        ));
    }

    if let Some(full) = &document.git_commit_id {
        if !is_lower_hex(full) {
            return Err(Error::InvalidResponse(
                "gitCommitId must be lowercase hexadecimal".to_owned(),
            ));
        }
    }
    if let Some(short) = &document.git_commit_id_short {
        if !short.is_empty() && !is_lower_hex(short) {
            return Err(Error::InvalidResponse(
                "gitCommitIdShort must be lowercase hexadecimal or empty".to_owned(),
            ));
        }
        if let Some(full) = &document.git_commit_id {
            if !full.starts_with(short) {
                return Err(Error::InvalidResponse(
                    "gitCommitIdShort must be a prefix of gitCommitId".to_owned(),
                ));
            }
        }
    }
    if let Some(date) = &document.git_commit_date {
        let bytes = date.as_bytes();
        let punctuation_is_exact = bytes.len() == 20
            && bytes[4] == b'-'
            && bytes[7] == b'-'
            && bytes[10] == b'T'
            && bytes[13] == b':'
            && bytes[16] == b':'
            && bytes[19] == b'Z';
        let numeric_positions_are_digits = bytes.len() == 20
            && bytes
                .iter()
                .enumerate()
                .filter(|(index, _)| ![4, 7, 10, 13, 16, 19].contains(index))
                .all(|(_, byte)| byte.is_ascii_digit());
        if !punctuation_is_exact
            || !numeric_positions_are_digits
            || DateTime::parse_from_rfc3339(date).is_err()
        {
            return Err(Error::InvalidResponse(
                "gitCommitDate must be an RFC 3339 UTC timestamp in whole seconds".to_owned(),
            ));
        }
    }
    Ok(())
}

fn is_lower_hex(value: &str) -> bool {
    !value.is_empty()
        && value
            .bytes()
            .all(|byte| byte.is_ascii_digit() || (b'a'..=b'f').contains(&byte))
}

fn emit_environment(version: &Version) {
    for (name, value) in [
        ("NBGV_SEMVER2", version.sem_ver2.as_str()),
        ("NBGV_VERSION", version.version.as_str()),
        ("NBGV_VERSION_HEIGHT", &version.version_height.to_string()),
        (
            "NBGV_GIT_COMMIT_ID",
            version.git_commit_id.as_deref().unwrap_or(""),
        ),
        (
            "NBGV_GIT_COMMIT_ID_SHORT",
            version.git_commit_id_short.as_deref().unwrap_or(""),
        ),
        (
            "NBGV_GIT_COMMIT_DATE",
            version.git_commit_date.as_deref().unwrap_or(""),
        ),
        (
            "NBGV_PUBLIC_RELEASE",
            if version.public_release {
                "true"
            } else {
                "false"
            },
        ),
    ] {
        println!("cargo::rustc-env={name}={value}");
    }
}

fn write_module(path: &Path, version: &Version) -> Result<(), Error> {
    let contents = format!(
        "\
/// Version information generated by Nerdbank.GitVersioning.
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct NbgvVersion {{
    /// The complete SemVer 2.0 version.
    pub sem_ver2: &'static str,
    /// The numeric System.Version representation.
    pub version: &'static str,
    /// The Git version height.
    pub version_height: i64,
    /// The full Git object ID, when available.
    pub git_commit_id: Option<&'static str>,
    /// The abbreviated Git object ID, when available.
    pub git_commit_id_short: Option<&'static str>,
    /// The UTC Git commit date, when available.
    pub git_commit_date: Option<&'static str>,
    /// Whether public release mode is active.
    pub public_release: bool,
}}

/// Version information captured by this package's build script.
pub const NBGV_VERSION: NbgvVersion = NbgvVersion {{
    sem_ver2: {sem_ver2:?},
    version: {version_string:?},
    version_height: {version_height},
    git_commit_id: {git_commit_id},
    git_commit_id_short: {git_commit_id_short},
    git_commit_date: {git_commit_date},
    public_release: {public_release},
}};
",
        sem_ver2 = version.sem_ver2,
        version_string = version.version,
        version_height = version.version_height,
        git_commit_id = option_literal(version.git_commit_id.as_deref()),
        git_commit_id_short = option_literal(version.git_commit_id_short.as_deref()),
        git_commit_date = option_literal(version.git_commit_date.as_deref()),
        public_release = version.public_release,
    );
    fs::write(path, contents).map_err(|source| Error::Io {
        path: path.to_owned(),
        source,
    })
}

fn option_literal(value: Option<&str>) -> String {
    value.map_or_else(|| "None".to_owned(), |value| format!("Some({value:?})"))
}

fn git_inputs(repository_root: &Path) -> Result<Vec<PathBuf>, Error> {
    let marker = repository_root.join(".git");
    let git_dir = if marker.is_dir() {
        marker
    } else {
        let contents = fs::read_to_string(&marker).map_err(|source| Error::Io {
            path: marker.clone(),
            source,
        })?;
        let path = contents
            .strip_prefix("gitdir:")
            .map(str::trim)
            .ok_or_else(|| {
                Error::InvalidResponse(format!("invalid Git marker {}", marker.display()))
            })?;
        repository_root.join(path)
    };

    let common_dir_file = git_dir.join("commondir");
    let common_dir = match fs::read_to_string(&common_dir_file) {
        Ok(path) => git_dir.join(path.trim()),
        Err(error) if error.kind() == io::ErrorKind::NotFound => git_dir.clone(),
        Err(source) => {
            return Err(Error::Io {
                path: common_dir_file,
                source,
            });
        }
    };

    let head = git_dir.join("HEAD");
    let mut inputs = vec![
        head.clone(),
        git_dir.join("shallow"),
        common_dir.join("packed-refs"),
        common_dir.join("refs").join("tags"),
    ];
    collect_files(&common_dir.join("refs").join("tags"), &mut inputs)?;
    if let Ok(contents) = fs::read_to_string(&head) {
        if let Some(reference) = contents.strip_prefix("ref:").map(str::trim) {
            inputs.push(common_dir.join(reference));
        }
    }
    inputs.sort();
    inputs.dedup();
    Ok(inputs)
}

fn collect_files(directory: &Path, files: &mut Vec<PathBuf>) -> Result<(), Error> {
    let entries = match fs::read_dir(directory) {
        Ok(entries) => entries,
        Err(error) if error.kind() == io::ErrorKind::NotFound => return Ok(()),
        Err(source) => {
            return Err(Error::Io {
                path: directory.to_owned(),
                source,
            });
        }
    };
    for entry in entries {
        let entry = entry.map_err(|source| Error::Io {
            path: directory.to_owned(),
            source,
        })?;
        let path = entry.path();
        if path.is_dir() {
            files.push(path.clone());
            collect_files(&path, files)?;
        } else {
            files.push(path);
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    const VALID: &str = r#"{
        "schemaVersion": 1,
        "semVer2": "1.2.3-beta.1+build",
        "version": "1.2.3.4",
        "versionHeight": 7,
        "gitCommitId": "0123456789abcdef",
        "gitCommitIdShort": "01234567",
        "gitCommitDate": "2026-08-09T21:59:55Z",
        "publicRelease": false,
        "futureProperty": {"ignored": true}
    }"#;

    #[test]
    fn parses_valid_response_and_ignores_unknown_properties() {
        let version = parse_adapter_json(VALID.as_bytes()).unwrap();
        assert_eq!(version.sem_ver2, "1.2.3-beta.1+build");
        assert_eq!(version.git_commit_id_short.as_deref(), Some("01234567"));
    }

    #[test]
    fn accepts_absent_git_information() {
        let json = VALID
            .replace("\"0123456789abcdef\"", "null")
            .replace("\"01234567\"", "null")
            .replace("\"2026-08-09T21:59:55Z\"", "null");
        let version = parse_adapter_json(json.as_bytes()).unwrap();
        assert_eq!(version.git_commit_id, None);
    }

    #[test]
    fn accepts_independently_null_git_information() {
        let without_date = VALID.replace("\"2026-08-09T21:59:55Z\"", "null");
        let version = parse_adapter_json(without_date.as_bytes()).unwrap();
        assert_eq!(version.git_commit_id.as_deref(), Some("0123456789abcdef"));
        assert_eq!(version.git_commit_date, None);

        let only_date = VALID
            .replace("\"0123456789abcdef\"", "null")
            .replace("\"01234567\"", "null");
        let version = parse_adapter_json(only_date.as_bytes()).unwrap();
        assert_eq!(
            version.git_commit_date.as_deref(),
            Some("2026-08-09T21:59:55Z")
        );
    }

    #[test]
    fn accepts_empty_git_commit_id_short() {
        let json = VALID.replace(
            "\"gitCommitIdShort\": \"01234567\"",
            "\"gitCommitIdShort\": \"\"",
        );
        let version = parse_adapter_json(json.as_bytes()).unwrap();
        assert_eq!(version.git_commit_id_short.as_deref(), Some(""));
    }

    #[test]
    fn rejects_unsupported_schema_before_other_fields() {
        let error = parse_adapter_json(br#"{"schemaVersion":2,"semVer2":false}"#).unwrap_err();
        assert!(error.to_string().contains("unsupported schemaVersion 2"));
    }

    #[test]
    fn rejects_missing_and_wrong_typed_fields() {
        let missing = VALID.replace("\"publicRelease\": false,", "");
        assert!(parse_adapter_json(missing.as_bytes()).is_err());
        let missing_nullable = VALID.replace("\"gitCommitDate\": \"2026-08-09T21:59:55Z\",", "");
        assert!(parse_adapter_json(missing_nullable.as_bytes()).is_err());
        let wrong_type = VALID.replace("\"versionHeight\": 7", "\"versionHeight\": \"7\"");
        assert!(parse_adapter_json(wrong_type.as_bytes()).is_err());
    }

    #[test]
    fn rejects_invalid_documented_formats() {
        let bad_semver = VALID.replace("1.2.3-beta.1+build", "01.2.3");
        assert!(parse_adapter_json(bad_semver.as_bytes()).is_err());
        let bad_date = VALID.replace("2026-08-09T21:59:55Z", "2026-08-09T21:59:55.1Z");
        assert!(parse_adapter_json(bad_date.as_bytes()).is_err());
    }
}
