//! Git-based semantic version calculation compatible with Nerdbank.GitVersioning.
//!
//! This crate computes deterministic versions from `version.json` or `version.txt`
//! files and Git history. It intentionally does not provide MSBuild integration.
//! It supports current stable Rust on Windows, Linux, and macOS.
//!
//! # Example
//!
//! ```no_run
//! use nerdbank_gitversioning::{GitContext, GitEngine, VersionOracle};
//!
//! fn main() -> Result<(), nerdbank_gitversioning::Error> {
//!     let context = GitContext::create(".", None, GitEngine::ReadOnly)?;
//!     let version = VersionOracle::new(&context, None)?.sem_ver2();
//!     println!("{version}");
//!     Ok(())
//! }
//! ```

#![warn(missing_docs)]

pub mod cloud;
pub mod cloud_build_services;
mod cloud_command;
mod error;
mod filter_path;
mod git_context;
mod history;
mod release_manager;
mod semantic_version;
mod version;
mod version_file;
mod version_options;
mod version_oracle;

pub use cloud_command::{
    CloudCommand, CloudCommandError, CloudCommandException, CloudCommandOptions,
};

pub use error::{Error, Result};
pub use filter_path::FilterPath;
pub use git_context::{CommitDates, GitContext, GitContextKind, GitEngine, effective_git_engine};
pub use history::{
    MAXIMUM_VERSION_COMPONENT, VersionHeightCalculation, calculate_version_height, encode_version,
    get_commit_from_version, get_commits_from_version, get_height, get_id_as_version,
    get_version_height, truncated_commit_id,
};
pub use release_manager::{
    PrepareReleaseOptions, PreparedRelease, ReleaseBranchInfo, ReleaseInfo, ReleaseManager,
    ReleaseManagerOutputMode, ReleasePreparationError, ReleasePreparationException,
    ReleasePreparationResult, format_release_branch_name, format_release_tag_name,
};
pub use semantic_version::{SemanticVersion, VersionPosition};
pub use version::Version;
pub use version_file::{
    VERSION_JSON_FILE_NAME, VERSION_SCHEMA, VERSION_TXT_FILE_NAME, VersionFile,
    VersionFileLocations, VersionFileRequirements,
};
pub use version_options::{
    AssemblyVersionOptions, CloudBuildNumberCommitIdOptions, CloudBuildNumberCommitWhen,
    CloudBuildNumberCommitWhere, CloudBuildNumberOptions, CloudBuildOptions,
    NuGetPackageVersionOptions, ReleaseOptions, ReleaseVersionIncrement, VersionOptions,
    VersionPrecision,
};
pub use version_oracle::VersionOracle;
