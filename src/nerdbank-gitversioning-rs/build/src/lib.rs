//! In-process Cargo build-script integration for Nerdbank.GitVersioning.
//!
//! Call [`emit!`] from `build.rs` to expose the `NBGV_*` environment variables.
//! [`emit_and_write_module`] also writes `nbgv.rs` to `OUT_DIR`; include it with
//! `include!(concat!(env!("OUT_DIR"), "/nbgv.rs"));`.

use nerdbank_gitversioning::{
    AdapterVersionInfo, GitContext, GitEngine, VersionOracle, effective_git_engine,
};
use std::env;
use std::fmt;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

/// The native, serializable version information shared with `cargo nbgv`.
pub type Version = AdapterVersionInfo;

const STAMP_FILE: &str = ".nbgv-version.json";

const ENVIRONMENT_INPUTS: &[&str] = &[
    "NBGV_GitEngine",
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
    "GIT_DIR",
    "GIT_WORK_TREE",
    "GIT_COMMON_DIR",
    "GIT_CEILING_DIRECTORIES",
    "GIT_DISCOVERY_ACROSS_FILESYSTEM",
];

/// Configures in-process version calculation for a Cargo package.
#[derive(Clone, Debug, Default)]
pub struct Config {
    project_dir: Option<PathBuf>,
}

impl Config {
    /// Creates a configuration that versions `CARGO_MANIFEST_DIR`.
    pub fn new() -> Self {
        Self::default()
    }

    /// Selects the project directory to version instead of `CARGO_MANIFEST_DIR`.
    pub fn project_dir(mut self, project_dir: impl AsRef<Path>) -> Self {
        self.project_dir = Some(project_dir.as_ref().to_owned());
        self
    }

    /// Calculates the version using the native library and active cloud provider.
    ///
    /// Outside a repository, reads the package's `.nbgv-version.json` stamp
    /// produced by `cargo nbgv`. A missing or invalid stamp is an error, not a
    /// fallback to a version without history. Explicitly disabled Git bypasses
    /// the stamp and uses native disabled-Git behavior.
    pub fn get_version(&self) -> Result<Version, Error> {
        self.version_with_context().map(|(version, _)| version)
    }

    /// Calculates the version and emits Cargo environment and rebuild instructions.
    ///
    /// Recalculates on every build in a live checkout, including with Git disabled,
    /// so new unstaged version files and dirty-tree changes cannot leave stale stamps.
    /// This intentionally reruns the build script and may recompile its package.
    /// Published archives watch only their precomputed stamp and remain incremental.
    pub fn emit(&self) -> Result<Version, Error> {
        let (version, context) = self.version_with_context()?;
        for name in ENVIRONMENT_INPUTS {
            println!("cargo::rerun-if-env-changed={name}");
        }
        if self.project_dir.is_none() {
            println!("cargo::rerun-if-env-changed=CARGO_MANIFEST_DIR");
        }
        println!(
            "cargo::rerun-if-changed={}",
            rerun_input(&context)?.display()
        );
        emit_environment(&version);
        Ok(version)
    }

    /// Calls [`Config::emit`] and writes the typed version constant to `OUT_DIR/nbgv.rs`.
    ///
    /// Include the file at crate scope with
    /// `include!(concat!(env!("OUT_DIR"), "/nbgv.rs"));`.
    pub fn emit_and_write_module(&self) -> Result<Version, Error> {
        let out_dir = env::var_os("OUT_DIR").ok_or(Error::MissingEnvironment("OUT_DIR"))?;
        let version = self.emit()?;
        write_module(&PathBuf::from(out_dir).join("nbgv.rs"), &version)?;
        Ok(version)
    }

    fn version_with_context(&self) -> Result<(Version, GitContext), Error> {
        let path = self.effective_project_dir()?;
        // Require an existing directory, rather than silently discovering its parent.
        fs::read_dir(&path)?;
        let engine = effective_git_engine(GitEngine::ReadOnly);
        let context = GitContext::create(&path, None, engine)?;
        if !context.is_repository() && engine != GitEngine::Disabled {
            let stamp = context.absolute_project_directory().join(STAMP_FILE);
            let bytes = match fs::read(&stamp) {
                Ok(bytes) => bytes,
                Err(error) if error.kind() == io::ErrorKind::NotFound => {
                    return Err(nerdbank_gitversioning::Error::InvalidOperation(format!(
                        "No Git repository or precomputed version stamp found at {}. \
                         Use cargo nbgv to prepare the package archive.",
                        stamp.display()
                    ))
                    .into());
                }
                Err(error) => return Err(error.into()),
            };
            let version: Version =
                serde_json::from_slice(&bytes).map_err(nerdbank_gitversioning::Error::from)?;
            if version.schema_version != 1 {
                return Err(nerdbank_gitversioning::Error::InvalidFormat(format!(
                    "Unsupported version stamp schemaVersion {}; expected 1.",
                    version.schema_version
                ))
                .into());
            }
            return Ok((version, context));
        }

        let mut oracle =
            VersionOracle::new(&context, nerdbank_gitversioning::cloud::active().as_deref())?;
        if let Ok(value) = env::var("PublicRelease") {
            if value.trim().eq_ignore_ascii_case("true") {
                oracle.public_release = true;
            } else if value.trim().eq_ignore_ascii_case("false") {
                oracle.public_release = false;
            }
        }
        Ok((Version::from(&oracle), context))
    }

    fn effective_project_dir(&self) -> Result<PathBuf, Error> {
        self.project_dir.clone().map_or_else(
            || {
                env::var_os("CARGO_MANIFEST_DIR")
                    .map(PathBuf::from)
                    .ok_or(Error::MissingEnvironment("CARGO_MANIFEST_DIR"))
            },
            Ok,
        )
    }
}

/// Calculates the package version and emits Cargo instructions.
pub fn emit() -> Result<Version, Error> {
    Config::new().emit()
}

/// Calculates the package version, emits Cargo instructions, and writes `nbgv.rs`.
pub fn emit_and_write_module() -> Result<Version, Error> {
    Config::new().emit_and_write_module()
}

/// Emits Cargo instructions and a typed module, panicking with context on failure.
///
/// With no argument uses [`Config::new`]; an expression may supply a custom [`Config`].
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

/// An error from Cargo configuration, filesystem access, or native version calculation.
#[derive(Debug)]
pub enum Error {
    /// A required Cargo environment variable was missing.
    MissingEnvironment(&'static str),
    /// A filesystem operation failed.
    Io(io::Error),
    /// The native library could not calculate or deserialize the version.
    Native(nerdbank_gitversioning::Error),
}

impl fmt::Display for Error {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::MissingEnvironment(name) => {
                write!(formatter, "required environment variable {name} is not set")
            }
            Self::Io(error) => error.fmt(formatter),
            Self::Native(error) => error.fmt(formatter),
        }
    }
}

impl std::error::Error for Error {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Self::MissingEnvironment(_) => None,
            Self::Io(error) => Some(error),
            Self::Native(error) => Some(error),
        }
    }
}

impl From<io::Error> for Error {
    fn from(error: io::Error) -> Self {
        Self::Io(error)
    }
}

impl From<nerdbank_gitversioning::Error> for Error {
    fn from(error: nerdbank_gitversioning::Error) -> Self {
        Self::Native(error)
    }
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
    /// The numeric version.
    pub version: &'static str,
    /// The Git version height.
    pub version_height: u32,
    /// The full Git commit ID, when available.
    pub git_commit_id: Option<&'static str>,
    /// The abbreviated Git commit ID, when available.
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
    match fs::read(path) {
        Ok(existing) if existing == contents.as_bytes() => return Ok(()),
        Ok(_) => {}
        Err(error) if error.kind() == io::ErrorKind::NotFound => {}
        Err(error) => return Err(error.into()),
    }
    fs::write(path, contents)?;
    Ok(())
}

fn option_literal(value: Option<&str>) -> String {
    value.map_or_else(|| "None".to_owned(), |value| format!("Some({value:?})"))
}

fn rerun_input(context: &GitContext) -> Result<PathBuf, Error> {
    let project = context.absolute_project_directory();
    if !context.is_repository() && context.engine() != GitEngine::Disabled {
        return Ok(project.join(STAMP_FILE));
    }
    // Cargo has no always-run directive. A deliberately absent input requests
    // recalculation without recursively watching the checkout and its outputs.
    let sentinel = project.join(".nbgv-always-recalculate");
    match fs::symlink_metadata(&sentinel) {
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(sentinel),
        Err(error) => Err(error.into()),
        Ok(_) => Err(nerdbank_gitversioning::Error::InvalidOperation(format!(
            "{} is reserved for Cargo change detection and must not exist.",
            sentinel.display()
        ))
        .into()),
    }
}
