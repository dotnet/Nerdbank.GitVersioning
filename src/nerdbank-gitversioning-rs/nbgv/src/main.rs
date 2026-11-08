// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

use std::env;
use std::fs;
use std::io::{self, BufRead, Write};
use std::path::{Path, PathBuf};
use std::process::ExitCode;

use clap::{ArgAction, Args, Parser, Subcommand};
use git2::{ErrorCode, Status};
use nerdbank_gitversioning::{
    CloudCommand, CloudCommandError, CloudCommandOptions, GitContext, GitEngine,
    PrepareReleaseOptions, ReleaseManager, ReleaseManagerOutputMode, ReleasePreparationError,
    SemanticVersion, Version, VersionFile, VersionFileRequirements, VersionOptions, VersionOracle,
    format_release_tag_name, get_commits_from_version,
};

const DEFAULT_REF: &str = "HEAD";

#[repr(u8)]
#[derive(Clone, Copy)]
enum ExitCodes {
    Ok = 0,
    NoGitRepo = 1,
    InvalidVersionSpec = 2,
    BadCloudVariable = 3,
    DuplicateCloudVariable = 4,
    NoCloudBuildEnvDetected = 5,
    UnsupportedFormat = 6,
    NoMatchingVersion = 7,
    BadGitRef = 8,
    NoVersionJsonFound = 9,
    TagConflict = 10,
    NoCloudBuildProviderMatch = 11,
    BadVariable = 12,
    UncommittedChanges = 13,
    InvalidBranchNameSetting = 14,
    BranchAlreadyExists = 15,
    UserNotConfigured = 16,
    DetachedHead = 17,
    InvalidVersionIncrementSetting = 18,
    InvalidParameters = 19,
    InvalidVersionIncrement = 20,
    // 21 and 22 belong to the excluded install command.
    ShallowClone = 23,
    InternalError = 24,
    InvalidTagNameSetting = 25,
    InvalidUnformattedCommitMessage = 26,
    // 27 belongs to the excluded path-filters command.
}

#[derive(Parser)]
#[command(
    name = "nbgv",
    version,
    about = "Nerdbank.GitVersioning Rust CLI",
    long_about = "Nerdbank.GitVersioning Rust CLI package"
)]
struct Cli {
    #[command(subcommand)]
    command: Command,
}

#[derive(Subcommand)]
enum Command {
    /// Gets the version information for a project.
    GetVersion(GetVersionArgs),
    /// Updates the version stamp that is applied to a project.
    SetVersion(SetVersionArgs),
    /// Creates a git tag to mark a version.
    Tag(TagArgs),
    /// Gets the commit(s) that match a given version.
    GetCommits(GetCommitsArgs),
    /// Communicates with the ambient cloud build.
    Cloud(CloudArgs),
    /// Creates a release branch and adjusts versions.
    PrepareRelease(PrepareReleaseArgs),
}

#[derive(Args)]
struct GetVersionArgs {
    /// The path to the project or project directory.
    #[arg(short, long)]
    project: Option<PathBuf>,
    /// Adds identifiers to the build metadata.
    #[arg(long, num_args = 1.., action = ArgAction::Append)]
    metadata: Vec<String>,
    /// Output format: text or json.
    #[arg(short, long)]
    format: Option<String>,
    /// Print one version property as raw text.
    #[arg(short, long)]
    variable: Option<String>,
    /// Override PublicRelease (a bare option means true).
    #[arg(long, num_args = 0..=1, default_missing_value = "true", value_parser = clap::value_parser!(bool))]
    public_release: Option<bool>,
    /// Commit or ref to inspect.
    #[arg(default_value = DEFAULT_REF)]
    commit_ish: String,
}

#[derive(Args)]
struct SetVersionArgs {
    #[arg(short, long)]
    project: Option<PathBuf>,
    /// The version to set.
    version: String,
}

#[derive(Args)]
struct TagArgs {
    #[arg(short, long)]
    project: Option<PathBuf>,
    /// Version or git ref to tag.
    #[arg(default_value = DEFAULT_REF)]
    version_or_ref: String,
    /// Print the tag without creating it.
    #[arg(long)]
    what_if: bool,
}

#[derive(Args)]
struct GetCommitsArgs {
    #[arg(short, long)]
    project: Option<PathBuf>,
    #[arg(short, long)]
    quiet: bool,
    /// Numeric a.b.c[.d] version.
    version: String,
}

#[derive(Args)]
struct CloudArgs {
    #[arg(short, long)]
    project: Option<PathBuf>,
    #[arg(long, num_args = 1.., action = ArgAction::Append)]
    metadata: Vec<String>,
    #[arg(short = 'v', long)]
    version: Option<String>,
    #[arg(short = 's', long = "ci-system")]
    ci_system: Option<String>,
    #[arg(short = 'a', long = "all-vars")]
    all_vars: bool,
    #[arg(short = 'c', long = "common-vars")]
    common_vars: bool,
    #[arg(long = "skip-cloud-build-number")]
    skip_cloud_build_number: bool,
    #[arg(short = 'd', long = "define", num_args = 1.., action = ArgAction::Append)]
    define: Vec<String>,
}

#[derive(Args)]
struct PrepareReleaseArgs {
    #[arg(short, long)]
    project: Option<PathBuf>,
    #[arg(long = "nextVersion")]
    next_version: Option<String>,
    #[arg(long = "versionIncrement")]
    version_increment: Option<String>,
    #[arg(short, long)]
    format: Option<String>,
    #[arg(long = "commit-message-pattern")]
    commit_message_pattern: Option<String>,
    #[arg(long)]
    what_if: bool,
    #[arg(long)]
    no_merge: bool,
    /// Prerelease tag for the release branch.
    tag: Option<String>,
}

fn main() -> ExitCode {
    let code = match Cli::try_parse() {
        Ok(cli) => run(cli),
        Err(error) => {
            let code = if error.use_stderr() { 1 } else { 0 };
            let _ = error.print();
            code
        }
    };
    ExitCode::from(code as u8)
}

fn run(cli: Cli) -> i32 {
    let result = match cli.command {
        Command::GetVersion(args) => get_version(args),
        Command::SetVersion(args) => set_version(args),
        Command::Tag(args) => tag(args),
        Command::GetCommits(args) => get_commits(args),
        Command::Cloud(args) => cloud(args),
        Command::PrepareRelease(args) => prepare_release(args),
    };
    match result {
        Ok(code) => code as i32,
        Err(error) => {
            eprintln!("ERROR: {error}");
            if error.to_string().to_ascii_lowercase().contains("shallow") {
                ExitCodes::ShallowClone as i32
            } else {
                ExitCodes::InternalError as i32
            }
        }
    }
}

type CommandResult = Result<ExitCodes, Box<dyn std::error::Error>>;

fn search_path(project: Option<&Path>) -> Result<PathBuf, io::Error> {
    let path = project.unwrap_or_else(|| Path::new("."));
    if path.is_absolute() {
        Ok(path.to_path_buf())
    } else {
        Ok(env::current_dir()?.join(path))
    }
}

fn get_version(args: GetVersionArgs) -> CommandResult {
    let path = search_path(args.project.as_deref())?;
    let mut context = GitContext::create(&path, None, GitEngine::ReadOnly)?;
    if !context.is_repository() {
        eprintln!("No git repo found at or above: \"{}\"", path.display());
        return Ok(ExitCodes::NoGitRepo);
    }
    if !context.try_select_commit(&args.commit_ish)? {
        eprintln!("rev-parse produced no commit for {}", args.commit_ish);
        return Ok(ExitCodes::BadGitRef);
    }
    let mut oracle = match VersionOracle::new(
        &context,
        nerdbank_gitversioning::cloud::active().as_deref(),
    ) {
        Ok(oracle) => oracle,
        Err(error) if context.is_shallow() => {
            eprintln!(
                "ERROR: Version calculation failed because the repository is a shallow clone: {error}"
            );
            return Ok(ExitCodes::ShallowClone);
        }
        Err(error) => return Err(error.into()),
    };
    warn_if_version_json_is_dirty(&context)?;
    oracle.build_metadata.extend(args.metadata);
    if let Some(value) = args.public_release {
        oracle.public_release = value;
    } else if let Ok(value) = env::var("PublicRelease") {
        if value.trim().eq_ignore_ascii_case("true") {
            oracle.public_release = true;
        } else if value.trim().eq_ignore_ascii_case("false") {
            oracle.public_release = false;
        }
    }

    let format = args.format.as_deref().unwrap_or("text");
    if let Some(variable) = args.variable {
        if !format.eq_ignore_ascii_case("text") {
            eprintln!("Format must be \"text\" when querying for an individual variable's value.");
            return Ok(ExitCodes::UnsupportedFormat);
        }
        let Some(value) = oracle_variable(&oracle, &variable) else {
            eprintln!("Variable \"{variable}\" not a version property.");
            return Ok(ExitCodes::BadVariable);
        };
        println!("{value}");
    } else if format.eq_ignore_ascii_case("text") {
        println!("Version:                      {}", oracle.version);
        println!(
            "AssemblyVersion:              {}",
            oracle.assembly_version()
        );
        println!(
            "AssemblyInformationalVersion: {}",
            oracle.assembly_informational_version()
        );
        println!(
            "NuGetPackageVersion:          {}",
            oracle.nuget_package_version()
        );
        println!(
            "NpmPackageVersion:            {}",
            oracle.npm_package_version()
        );
    } else if format.eq_ignore_ascii_case("json") {
        println!("{}", serde_json::to_string_pretty(&oracle)?);
    } else {
        eprintln!("Unsupported format: {format}");
        return Ok(ExitCodes::UnsupportedFormat);
    }
    Ok(ExitCodes::Ok)
}

fn oracle_variable(oracle: &VersionOracle, requested: &str) -> Option<String> {
    let name = requested.to_ascii_lowercase();
    let value = match name.as_str() {
        "cloudbuildnumber" => oracle.cloud_build_number(),
        "cloudbuildnumberenabled" => dotnet_bool(oracle.cloud_build_number_enabled()).to_owned(),
        "cloudbuildallvarsenabled" => dotnet_bool(oracle.cloud_build_all_vars_enabled()).to_owned(),
        "cloudbuildversionvarsenabled" => {
            dotnet_bool(oracle.cloud_build_version_vars_enabled()).to_owned()
        }
        "versionfilefound" => dotnet_bool(oracle.version_file_found()).to_owned(),
        "assemblyversion" => oracle.assembly_version().to_string(),
        "assemblyfileversion" | "version" => oracle.version.to_string(),
        "assemblyinformationalversion" => oracle.assembly_informational_version(),
        "publicrelease" => dotnet_bool(oracle.public_release).to_owned(),
        "prereleaseversion" => oracle.prerelease_version(),
        "prereleaseversionnoleadinghyphen" => oracle.prerelease_version_no_leading_hyphen(),
        "simpleversion" => oracle.simple_version().to_string(),
        "buildnumber" => oracle.build_number().to_string(),
        "versionrevision" => oracle.version_revision().to_string(),
        "majorminorversion" => oracle.major_minor_version().to_string(),
        "versionmajor" => oracle.version.major.to_string(),
        "versionminor" => oracle.version.minor.to_string(),
        "gitcommitid" => oracle.git_commit_id.clone().unwrap_or_default(),
        "gitcommitidshort" => oracle.git_commit_id_short.clone().unwrap_or_default(),
        "gitcommitdate" => oracle
            .git_commit_date()
            .map(|date| date.format("%Y-%m-%dT%H:%M:%S%:z").to_string())
            .unwrap_or_default(),
        "gitcommitauthordate" => oracle
            .git_commit_author_date()
            .map(|date| date.format("%Y-%m-%dT%H:%M:%S%:z").to_string())
            .unwrap_or_default(),
        "versionheight" => oracle.version_height.to_string(),
        "versionheightoffset" => oracle.version_height_offset().to_string(),
        "buildingref" => oracle.building_ref.clone().unwrap_or_default(),
        "buildmetadatafragment" => oracle.build_metadata_fragment(),
        "nugetpackageversion" => oracle.nuget_package_version(),
        "chocolateypackageversion" => oracle.chocolatey_package_version(),
        "npmpackageversion" => oracle.npm_package_version(),
        "semver1" => oracle.sem_ver1(),
        "semver2" => oracle.sem_ver2(),
        "semver1numericidentifierpadding" => {
            oracle.sem_ver1_numeric_identifier_padding().to_string()
        }
        _ => return None,
    };
    Some(value)
}

const fn dotnet_bool(value: bool) -> &'static str {
    if value { "True" } else { "False" }
}

fn warn_if_version_json_is_dirty(context: &GitContext) -> Result<(), Box<dyn std::error::Error>> {
    if !context.is_head()? {
        return Ok(());
    }
    let repository = context.repository().expect("repository context");
    let head_tree = repository.head()?.peel_to_tree()?;
    let mut directory = context.absolute_project_directory();
    loop {
        let candidate = directory.join("version.json");
        let version_txt = directory.join("version.txt");
        let relative = candidate.strip_prefix(context.working_tree_path())?;
        let txt_relative = version_txt.strip_prefix(context.working_tree_path())?;
        if version_txt.exists() {
            break;
        }

        let json = if candidate.exists() {
            Some(fs::read(&candidate)?)
        } else if head_tree.get_path(txt_relative).is_ok() {
            break;
        } else if let Ok(entry) = head_tree.get_path(relative) {
            Some(
                entry
                    .to_object(repository)?
                    .peel_to_blob()?
                    .content()
                    .to_vec(),
            )
        } else {
            None
        };
        if let Some(json) = json {
            let status = repository.status_file(relative).unwrap_or(Status::CURRENT);
            if status != Status::CURRENT {
                eprintln!(
                    "Warning: Dirty version.json files must be committed before their changes will be applied."
                );
                break;
            }
            let inherits = serde_json::from_slice::<serde_json::Value>(&json)
                .ok()
                .and_then(|value| value.get("inherit").and_then(serde_json::Value::as_bool))
                .unwrap_or(false);
            if !inherits {
                break;
            }
        }
        if directory == context.working_tree_path() || !directory.pop() {
            break;
        }
    }
    Ok(())
}

fn set_version(args: SetVersionArgs) -> CommandResult {
    let version = match args.version.parse::<SemanticVersion>() {
        Ok(version) => version,
        Err(_) => {
            eprintln!(
                "\"{}\" is not a semver-compliant version spec.",
                args.version
            );
            return Ok(ExitCodes::InvalidVersionSpec);
        }
    };
    let path = search_path(args.project.as_deref())?;
    let context = GitContext::create(&path, None, GitEngine::ReadWrite)?;
    let requirements = VersionFileRequirements::NON_MERGED_RESULT
        | VersionFileRequirements::VERSION_SPECIFIED
        | VersionFileRequirements::ACCEPT_INHERITING_FILE;
    let (existing, locations) = VersionFile::new(&context).get_version(requirements)?;
    let mut options = VersionOptions {
        version: Some(version.clone()),
        ..Default::default()
    };
    let destination = if let (Some(existing), Some(directory)) =
        (existing, locations.version_specifying_version_directory)
    {
        options = existing;
        options.version = Some(version);
        directory
    } else if args.project.is_none() {
        if !context.is_repository() {
            eprintln!(
                "No version file and no git repo found at or above: \"{}\"",
                path.display()
            );
            return Ok(ExitCodes::NoGitRepo);
        }
        context.working_tree_path().to_path_buf()
    } else {
        path
    };
    let version_path = VersionFile::new(&context).set_version(destination, &options, true)?;
    if context.is_repository() {
        context.stage(version_path)?;
    }
    Ok(ExitCodes::Ok)
}

fn tag(args: TagArgs) -> CommandResult {
    let path = search_path(args.project.as_deref())?;
    let mut context = GitContext::create(&path, None, GitEngine::ReadWrite)?;
    if !context.is_repository() {
        eprintln!("No git repo found at or above: \"{}\"", path.display());
        return Ok(ExitCodes::NoGitRepo);
    }
    let (options, _) = VersionFile::new(&context).get_version(Default::default())?;
    let Some(options) = options else {
        eprintln!(
            "Failed to load version file for directory '{}'.",
            path.display()
        );
        return Ok(ExitCodes::NoVersionJsonFound);
    };
    let release_options = options.release_or_default();
    if !release_options.tag_name_or_default().contains("{version}") {
        let setting = release_options.tag_name_or_default();
        eprintln!(
            "Invalid 'tagName' setting '{setting}'. Missing version placeholder '{{version}}'."
        );
        return Ok(ExitCodes::InvalidTagNameSetting);
    }
    if !context.try_select_commit(&args.version_or_ref)? {
        let version = match args.version_or_ref.parse::<Version>() {
            Ok(version) => version,
            Err(_) => {
                eprintln!(
                    "\"{}\" is not a simple a.b.c[.d] version spec or git reference.",
                    args.version_or_ref
                );
                return Ok(ExitCodes::InvalidVersionSpec);
            }
        };
        let commits = get_commits_from_version(&context, &version)?;
        if commits.is_empty() {
            eprintln!("No commit with that version found.");
            return Ok(ExitCodes::NoMatchingVersion);
        }
        let selected = if commits.len() == 1 {
            commits[0]
        } else {
            print_commits(&mut context, &commits, false, true)?;
            read_selection(commits.len()).map(|index| commits[index - 1])?
        };
        context.try_select_commit(&selected.to_string())?;
    }
    let mut oracle =
        VersionOracle::new(&context, nerdbank_gitversioning::cloud::active().as_deref())?;
    if !oracle.version_file_found() {
        eprintln!(
            "No version.json file found in or above \"{}\" in commit {}.",
            path.display(),
            context.git_commit_id().expect("selected commit")
        );
        return Ok(ExitCodes::NoVersionJsonFound);
    }
    oracle.public_release = true;
    let semantic: SemanticVersion = oracle.sem_ver2().parse()?;
    let tag_name = format_release_tag_name(&options.release_or_default(), &semantic)
        .map_err(|error| Box::new(error) as Box<dyn std::error::Error>)?;
    if args.what_if {
        println!("{tag_name}");
        return Ok(ExitCodes::Ok);
    }
    match context.apply_tag(&tag_name) {
        Ok(_) => {}
        Err(nerdbank_gitversioning::Error::Git(error)) if error.code() == ErrorCode::Exists => {
            let repository = context.repository().expect("repository context");
            let tagged = repository
                .revparse_single(&format!("refs/tags/{tag_name}"))
                .and_then(|object| object.peel_to_commit())?
                .id();
            let expected = context.git_commit_id().expect("selected commit");
            if tagged == expected {
                eprintln!("The tag {tag_name} is already defined (to the right commit).");
                return Ok(ExitCodes::Ok);
            }
            eprintln!(
                "The tag {tag_name} is already defined (expected {expected} but was on {tagged})."
            );
            return Ok(ExitCodes::TagConflict);
        }
        Err(error) => return Err(Box::new(error)),
    }
    let commit = context.git_commit_id().expect("selected commit");
    println!("{tag_name} tag created at {commit}.");
    println!("Remember to push to a remote: git push origin {tag_name}");
    Ok(ExitCodes::Ok)
}

fn read_selection(count: usize) -> Result<usize, io::Error> {
    let stdin = io::stdin();
    let mut lines = stdin.lock().lines();
    loop {
        print!("Enter selection: ");
        io::stdout().flush()?;
        if let Some(line) = lines.next() {
            if let Ok(selection) = line?.parse::<usize>()
                && (1..=count).contains(&selection)
            {
                return Ok(selection);
            }
        } else {
            return Err(io::Error::new(io::ErrorKind::UnexpectedEof, "no selection"));
        }
    }
}

fn get_commits(args: GetCommitsArgs) -> CommandResult {
    let version = match args.version.parse::<Version>() {
        Ok(version) => version,
        Err(_) => {
            eprintln!(
                "\"{}\" is not a simple a.b.c[.d] version spec.",
                args.version
            );
            return Ok(ExitCodes::InvalidVersionSpec);
        }
    };
    let path = search_path(args.project.as_deref())?;
    let mut context = GitContext::create(&path, None, GitEngine::ReadWrite)?;
    if !context.is_repository() {
        eprintln!("No git repo found at or above: \"{}\"", path.display());
        return Ok(ExitCodes::NoGitRepo);
    }
    let commits = get_commits_from_version(&context, &version)?;
    print_commits(&mut context, &commits, args.quiet, false)?;
    Ok(ExitCodes::Ok)
}

fn print_commits(
    context: &mut GitContext,
    commits: &[git2::Oid],
    quiet: bool,
    options: bool,
) -> Result<(), Box<dyn std::error::Error>> {
    for (index, commit_id) in commits.iter().enumerate() {
        if options {
            print!("{:3}. ", index + 1);
        }
        if quiet {
            println!("{commit_id}");
        } else {
            context.try_select_commit(&commit_id.to_string())?;
            let oracle = VersionOracle::new(context, None)?;
            let repository = context.repository().expect("repository");
            let commit = repository.find_commit(*commit_id)?;
            let message = commit.summary().ok().flatten().unwrap_or_default();
            println!("{commit_id} {} {message}", oracle.version);
        }
    }
    Ok(())
}

fn cloud(args: CloudArgs) -> CommandResult {
    let path = search_path(args.project.as_deref())?;
    if !path.is_dir() {
        eprintln!("\"{}\" is not an existing directory.", path.display());
        return Ok(ExitCodes::NoGitRepo);
    }
    let mut definitions = Vec::new();
    for definition in args.define {
        let Some((name, value)) = definition.split_once('=') else {
            eprintln!(
                "\"{definition}\" is not in the NAME=VALUE syntax required for cloud variables."
            );
            return Ok(ExitCodes::BadCloudVariable);
        };
        if definitions
            .iter()
            .any(|(existing, _): &(String, String)| existing == name)
        {
            eprintln!("Cloud build variable \"{name}\" specified more than once.");
            return Ok(ExitCodes::DuplicateCloudVariable);
        }
        definitions.push((name.to_owned(), value.to_owned()));
    }
    let options = CloudCommandOptions {
        metadata: args.metadata,
        version: args.version,
        ci_system: args.ci_system,
        all_vars: args.all_vars,
        common_vars: args.common_vars,
        cloud_build_number: !args.skip_cloud_build_number,
        additional_variables: definitions,
        always_use_libgit2: env::var("NBGV_GitEngine").as_deref() == Ok("LibGit2"),
    };
    let result =
        CloudCommand::new(&mut io::stdout(), &mut io::stderr()).set_build_variables(path, &options);
    match result {
        Ok(_) => Ok(ExitCodes::Ok),
        Err(error) => {
            eprintln!("{error}");
            Ok(match error.error {
                CloudCommandError::NoCloudBuildProviderMatch => {
                    ExitCodes::NoCloudBuildProviderMatch
                }
                CloudCommandError::DuplicateCloudVariable => ExitCodes::DuplicateCloudVariable,
                CloudCommandError::NoCloudBuildEnvDetected => ExitCodes::NoCloudBuildEnvDetected,
                CloudCommandError::OperationFailed => ExitCodes::InternalError,
            })
        }
    }
}

fn prepare_release(args: PrepareReleaseArgs) -> CommandResult {
    let path = search_path(args.project.as_deref())?;
    if !path.is_dir() {
        eprintln!("\"{}\" is not an existing directory.", path.display());
        return Ok(ExitCodes::NoGitRepo);
    }
    if args.next_version.is_some() && args.version_increment.is_some() {
        eprintln!("Options 'nextVersion' and 'versionIncrement' cannot be used at the same time.");
        return Ok(ExitCodes::InvalidParameters);
    }
    let next_version = if let Some(value) = args.next_version {
        match value.parse::<Version>() {
            Ok(version) => Some(version),
            Err(_) => {
                eprintln!("\"{value}\" is not a valid version spec.");
                return Ok(ExitCodes::InvalidVersionSpec);
            }
        }
    } else {
        None
    };
    let version_increment = if let Some(value) = args.version_increment {
        match value.to_ascii_lowercase().as_str() {
            "major" => Some(nerdbank_gitversioning::ReleaseVersionIncrement::Major),
            "minor" => Some(nerdbank_gitversioning::ReleaseVersionIncrement::Minor),
            "build" => Some(nerdbank_gitversioning::ReleaseVersionIncrement::Build),
            _ => {
                eprintln!("\"{value}\" is not a valid version increment");
                return Ok(ExitCodes::InvalidVersionIncrement);
            }
        }
    } else {
        None
    };
    let format = args.format.as_deref().unwrap_or("text");
    let output_mode = if format.eq_ignore_ascii_case("text") {
        ReleaseManagerOutputMode::Text
    } else if format.eq_ignore_ascii_case("json") {
        ReleaseManagerOutputMode::Json
    } else {
        eprintln!("Unsupported format: {format}");
        return Ok(ExitCodes::UnsupportedFormat);
    };
    if let Some(pattern) = &args.commit_message_pattern
        && !valid_commit_message_pattern(pattern)
    {
        eprintln!("Invalid commit message pattern: Input string was not in a correct format.");
        return Ok(ExitCodes::InvalidUnformattedCommitMessage);
    }
    let options = PrepareReleaseOptions {
        release_unstable_tag: args.tag,
        next_version,
        version_increment,
        output_mode,
        commit_message: args.commit_message_pattern,
        dry_run: args.what_if,
        merge_release_branch: !args.no_merge,
    };
    match ReleaseManager::new().prepare_release(path, &options) {
        Ok(result) => {
            println!("{}", result.output);
            Ok(ExitCodes::Ok)
        }
        Err(error) => {
            eprintln!("{error}");
            Ok(match error.error {
                ReleasePreparationError::NoGitRepo => ExitCodes::NoGitRepo,
                ReleasePreparationError::UncommittedChanges => ExitCodes::UncommittedChanges,
                ReleasePreparationError::InvalidBranchNameSetting => {
                    ExitCodes::InvalidBranchNameSetting
                }
                ReleasePreparationError::InvalidTagNameSetting => ExitCodes::InvalidTagNameSetting,
                ReleasePreparationError::NoVersionFile => ExitCodes::NoVersionJsonFound,
                ReleasePreparationError::VersionDecrement
                | ReleasePreparationError::NoVersionIncrement => ExitCodes::InvalidVersionSpec,
                ReleasePreparationError::BranchAlreadyExists => ExitCodes::BranchAlreadyExists,
                ReleasePreparationError::UserNotConfigured => ExitCodes::UserNotConfigured,
                ReleasePreparationError::DetachedHead => ExitCodes::DetachedHead,
                ReleasePreparationError::InvalidVersionIncrementSetting => {
                    ExitCodes::InvalidVersionIncrementSetting
                }
                ReleasePreparationError::GitCommandFailed
                | ReleasePreparationError::GitOperationFailed
                | ReleasePreparationError::VersionFileError => ExitCodes::InternalError,
            })
        }
    }
}

fn valid_commit_message_pattern(pattern: &str) -> bool {
    let bytes = pattern.as_bytes();
    let mut index = 0;
    while index < bytes.len() {
        match bytes[index] {
            b'{' => {
                if bytes.get(index + 1) == Some(&b'{') {
                    index += 2;
                } else if bytes.get(index..index + 3) == Some(b"{0}") {
                    index += 3;
                } else {
                    return false;
                }
            }
            b'}' => {
                if bytes.get(index + 1) == Some(&b'}') {
                    index += 2;
                } else {
                    return false;
                }
            }
            _ => index += 1,
        }
    }
    true
}
