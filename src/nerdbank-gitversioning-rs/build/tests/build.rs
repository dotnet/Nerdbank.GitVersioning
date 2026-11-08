use git2::{Repository, Signature};
use nerdbank_gitversioning::{AdapterVersionInfo, GitContext, GitEngine, VersionOracle};
use nerdbank_gitversioning_build::{Config, Error, Version};
use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::{Command, Output};
use tempfile::TempDir;

const DETECTION_VARIABLES: &[&str] = &[
    "NBGV_GitEngine",
    "PublicRelease",
    "DEPENDABOT",
    "GITHUB_ACTOR",
    "APPVEYOR",
    "SYSTEM_TEAMPROJECTID",
    "GITHUB_ACTIONS",
    "BUILD_VCS_NUMBER",
    "bamboo.buildKey",
    "JENKINS_URL",
    "GITLAB_CI",
    "TRAVIS",
    "JB_SPACE_GIT_REVISION",
    "BITBUCKET_PIPELINE_UUID",
    "BITBUCKET_STEP_UUID",
    "BITBUCKET_STEP_TRIGGERER_UUID",
    "GIT_DIR",
    "GIT_WORK_TREE",
    "GIT_COMMON_DIR",
];

fn fixture() -> TempDir {
    let directory = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("target")
        .join("test-repositories");
    fs::create_dir_all(&directory).unwrap();
    tempfile::tempdir_in(directory).unwrap()
}

fn archive_fixture() -> TempDir {
    let directory = fixture();
    // A dangling gitdir stops discovery of the enclosing checkout. Libgit2
    // reports NotFound, the same no-repository context as an extracted archive.
    fs::write(directory.path().join(".git"), "gitdir: missing.git\n").unwrap();
    let context = GitContext::create(directory.path(), None, GitEngine::ReadOnly).unwrap();
    assert!(!context.is_repository());
    directory
}

fn commit(root: &Path) {
    let repository = Repository::open(root).unwrap();
    let mut index = repository.index().unwrap();
    index
        .add_all(["*"], git2::IndexAddOption::DEFAULT, None)
        .unwrap();
    index.write().unwrap();
    let tree_id = index.write_tree().unwrap();
    let tree = repository.find_tree(tree_id).unwrap();
    let signature = Signature::now("Build test", "build@example.com").unwrap();
    let parent = repository
        .head()
        .ok()
        .and_then(|head| head.peel_to_commit().ok());
    let parents: Vec<_> = parent.iter().collect();
    repository
        .commit(
            Some("HEAD"),
            &signature,
            &signature,
            "version",
            &tree,
            &parents,
        )
        .unwrap();
}

fn repository(version_json: &str) -> TempDir {
    let directory = fixture();
    Repository::init(directory.path()).unwrap();
    fs::write(directory.path().join("version.json"), version_json).unwrap();
    commit(directory.path());
    directory
}

fn worker_command(root: &Path, mode: &str) -> Command {
    let mut command = Command::new(env::current_exe().unwrap());
    command.args(["--exact", "build_worker", "--ignored", "--nocapture"]);
    command
        .env("NBGV_TEST_MODE", mode)
        .env("CARGO_MANIFEST_DIR", root);
    // Each child has an isolated environment, without mutating the test process.
    for name in DETECTION_VARIABLES {
        command.env_remove(name);
    }
    command.env("GIT_CEILING_DIRECTORIES", root.parent().unwrap());
    command.env_remove("OUT_DIR");
    command
}

fn success(output: Output) -> String {
    assert!(
        output.status.success(),
        "{}\n{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    String::from_utf8(output.stdout).unwrap()
}

fn version_from(output: &str) -> Version {
    let json = output
        .lines()
        .find_map(|line| line.strip_prefix("VERSION:"))
        .unwrap();
    serde_json::from_str(json).unwrap()
}

fn cargo_package(root: &Path) {
    fs::create_dir_all(root.join("src")).unwrap();
    fs::write(
        root.join("Cargo.toml"),
        format!(
            "[package]\nname = \"nbgv-build-regression\"\nversion = \"0.0.0\"\nedition = \"2024\"\n\
             [workspace]\n[build-dependencies]\nnerdbank-gitversioning-build = {{ path = {} }}\n",
            serde_json::to_string(env!("CARGO_MANIFEST_DIR")).unwrap()
        ),
    )
    .unwrap();
    fs::write(
        root.join("build.rs"),
        "fn main() { nerdbank_gitversioning_build::emit!(); }\n",
    )
    .unwrap();
    fs::write(
        root.join("src").join("main.rs"),
        "include!(concat!(env!(\"OUT_DIR\"), \"/nbgv.rs\"));\n\
         fn main() { println!(\"{}|{}|{}\", NBGV_VERSION.sem_ver2, \
         NBGV_VERSION.version_height, NBGV_VERSION.git_commit_id.unwrap_or(\"\")); }\n",
    )
    .unwrap();
}

fn cargo_build(root: &Path, target: &Path, fresh: bool) -> String {
    cargo_build_with_engine(root, target, fresh, None)
}

fn cargo_build_with_engine(
    root: &Path,
    target: &Path,
    fresh: bool,
    engine: Option<&str>,
) -> String {
    let mut command = Command::new(env!("CARGO"));
    command
        .args(["build", "--offline", "--verbose"])
        .current_dir(root)
        .env("CARGO_TARGET_DIR", target)
        .env("GIT_CEILING_DIRECTORIES", root.parent().unwrap());
    for name in DETECTION_VARIABLES {
        command.env_remove(name);
    }
    if let Some(engine) = engine {
        command.env("NBGV_GitEngine", engine);
    }
    let output = command.output().unwrap();
    let stderr = String::from_utf8_lossy(&output.stderr).into_owned();
    success(output);
    let marker = if fresh {
        "Fresh nbgv-build-regression"
    } else {
        "Compiling nbgv-build-regression"
    };
    assert!(stderr.contains(marker), "Expected {marker}:\n{stderr}");
    if fresh {
        assert!(
            !stderr.contains("Compiling nbgv-build-regression"),
            "{stderr}"
        );
    }
    let executable = target.join("debug").join(if cfg!(windows) {
        "nbgv-build-regression.exe"
    } else {
        "nbgv-build-regression"
    });
    success(Command::new(executable).output().unwrap())
}

fn version_snapshot(version: &Version) -> String {
    format!(
        "{}|{}|{}\n",
        version.sem_ver2,
        version.version_height,
        version.git_commit_id.as_deref().unwrap_or("")
    )
}

#[test]
fn real_cargo_builds_recalculate_checkouts_and_cache_archives() {
    let directory = repository(r#"{"version":"1.0"}"#);
    fs::write(directory.path().join(".gitignore"), "target/\n").unwrap();
    let package = directory.path().join("package");
    cargo_package(&package);
    commit(directory.path());
    // Separate from the outer Cargo invocation's target to avoid its lock.
    let target = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("target")
        .join("cargo-build-regression");
    let check = || {
        let actual = cargo_build(&package, &target, false);
        let expected = version_from(&success(worker_command(&package, "get").output().unwrap()));
        assert_eq!(actual, version_snapshot(&expected));
    };
    check();
    check();

    fs::write(directory.path().join("committed.txt"), "new commit").unwrap();
    commit(directory.path());
    check();
    check();

    fs::write(
        directory.path().join("version.json"),
        r#"{"version":"2.0"}"#,
    )
    .unwrap();
    check();
    check();

    let nearer = package.join("version.json");
    fs::write(&nearer, r#"{"version":"3.0"}"#).unwrap();
    check();
    check();

    fs::remove_file(nearer).unwrap();
    check();
    check();

    let legacy = package.join("version.txt");
    fs::write(&legacy, "4.5\nbeta").unwrap();
    check();
    fs::remove_file(legacy).unwrap();
    check();

    fs::write(
        directory.path().join("version.json"),
        r#"{"version":"2.0","gitCommitIdIncludeDirty":true}"#,
    )
    .unwrap();
    commit(directory.path());
    check();
    fs::write(directory.path().join("committed.txt"), "dirty edit").unwrap();
    check();
    fs::write(directory.path().join("committed.txt"), "new commit").unwrap();
    check();

    let optional_metadata = directory.path().join(".git").join("config.worktree");
    assert!(!optional_metadata.exists());
    fs::write(&optional_metadata, "[nbgvtest]\nvalue = 1\n").unwrap();
    check();
    check();
    fs::remove_file(optional_metadata).unwrap();
    check();
    check();

    for version_file in [None, Some(r#"{"version":"6.7"}"#), None] {
        let nearer = package.join("version.json");
        if let Some(contents) = version_file {
            fs::write(&nearer, contents).unwrap();
        } else if nearer.exists() {
            fs::remove_file(&nearer).unwrap();
        }
        let actual = cargo_build_with_engine(&package, &target, false, Some("Disabled"));
        let expected = version_from(&success(
            worker_command(&package, "get")
                .env("NBGV_GitEngine", "Disabled")
                .output()
                .unwrap(),
        ));
        assert_eq!(actual, version_snapshot(&expected));
    }

    let archive = archive_fixture();
    cargo_package(archive.path());
    let stamp_path = archive.path().join(".nbgv-version.json");
    let mut version = stamp();
    fs::write(&stamp_path, serde_json::to_vec(&version).unwrap()).unwrap();
    assert_eq!(
        cargo_build(archive.path(), &target, false),
        version_snapshot(&version)
    );
    assert_eq!(
        cargo_build(archive.path(), &target, true),
        version_snapshot(&version)
    );
    version.sem_ver2 = "7.8.10".to_owned();
    fs::write(stamp_path, serde_json::to_vec(&version).unwrap()).unwrap();
    assert_eq!(
        cargo_build(archive.path(), &target, false),
        version_snapshot(&version)
    );
    assert_eq!(
        cargo_build(archive.path(), &target, true),
        version_snapshot(&version)
    );
}

#[test]
#[ignore = "subprocess entry point for isolated build-script environment"]
fn build_worker() {
    let mode = env::var("NBGV_TEST_MODE").unwrap();
    let config = Config::new();
    let version = match mode.as_str() {
        "emit" => config.emit(),
        "module" => config.emit_and_write_module(),
        "macro" => Ok(nerdbank_gitversioning_build::emit!()),
        "custom_macro" => Ok(nerdbank_gitversioning_build::emit!(
            Config::new().project_dir(env::var_os("CARGO_MANIFEST_DIR").unwrap())
        )),
        "missing_manifest" => {
            // SAFETY: This worker runs alone in a dedicated child process.
            unsafe { env::remove_var("CARGO_MANIFEST_DIR") };
            let error = config.get_version().unwrap_err();
            assert!(matches!(
                error,
                Error::MissingEnvironment("CARGO_MANIFEST_DIR")
            ));
            assert!(std::error::Error::source(&error).is_none());
            println!("ERROR:{error}");
            return;
        }
        "missing_out" => {
            let error = config.emit_and_write_module().unwrap_err();
            assert!(matches!(error, Error::MissingEnvironment("OUT_DIR")));
            println!("ERROR:{error}");
            return;
        }
        "emit_error" => {
            let error = config.emit().unwrap_err();
            println!("ERROR:{error}");
            return;
        }
        "error" => {
            let error = config.get_version().unwrap_err();
            assert!(std::error::Error::source(&error).is_some());
            println!("ERROR:{error}");
            return;
        }
        _ => config.get_version(),
    }
    .unwrap();
    println!("VERSION:{}", serde_json::to_string(&version).unwrap());
}

#[test]
fn computes_in_process_and_matches_native_oracle() {
    let directory = repository(r#"{"version":"2.3-beta","gitCommitIdShortFixedLength":8}"#);
    let output = success(worker_command(directory.path(), "emit").output().unwrap());
    let actual = version_from(&output);
    let context = GitContext::create(directory.path(), None, GitEngine::ReadOnly).unwrap();
    let expected = AdapterVersionInfo::from(&VersionOracle::new(&context, None).unwrap());
    assert_eq!(actual, expected);
    assert_eq!(actual.version_height, 1);
    for (name, value) in [
        ("NBGV_SEMVER2", actual.sem_ver2.clone()),
        ("NBGV_VERSION", actual.version.clone()),
        ("NBGV_VERSION_HEIGHT", "1".to_owned()),
        ("NBGV_GIT_COMMIT_ID", actual.git_commit_id.clone().unwrap()),
        (
            "NBGV_GIT_COMMIT_ID_SHORT",
            actual.git_commit_id_short.clone().unwrap(),
        ),
        (
            "NBGV_GIT_COMMIT_DATE",
            actual.git_commit_date.clone().unwrap(),
        ),
        ("NBGV_PUBLIC_RELEASE", "false".to_owned()),
    ] {
        assert!(output.contains(&format!("cargo::rustc-env={name}={value}")));
    }
    assert!(!output.contains("NBGV_EXECUTABLE"));
    assert!(!output.contains("rerun-if-env-changed=PATH"));
}

#[test]
fn shared_git_fixture_matches_dotnet_oracle_byte_for_byte() {
    let Some(manifest_path) = env::var_os("NBGV_PARITY_MANIFEST") else {
        eprintln!("NBGV_PARITY_MANIFEST is not set; shared parity fixture was not requested");
        return;
    };
    let manifest: serde_json::Value =
        serde_json::from_slice(&fs::read(&manifest_path).unwrap()).unwrap();
    let scenarios = manifest["scenarios"].as_array().unwrap();
    assert_eq!(
        scenarios.len(),
        6,
        "Shared parity requires all six scenarios"
    );
    for required in ["dirty-working-tree", "disabled-no-git-data"] {
        assert!(
            scenarios
                .iter()
                .any(|scenario| scenario["name"] == required),
            "Shared parity is missing {required}"
        );
    }
    for scenario in scenarios {
        let name = scenario["name"].as_str().unwrap();
        let directory = Path::new(scenario["directory"].as_str().unwrap());
        let context = GitContext::create(directory, None, GitEngine::ReadOnly).unwrap();
        assert!(
            context.is_repository(),
            "{name} must be backed by its real Git fixture"
        );
        let oracle: Version =
            serde_json::from_slice(&fs::read(scenario["oracle"].as_str().unwrap()).unwrap())
                .unwrap();
        assert_eq!(oracle.schema_version, 1, "{name}");
        let mut command = worker_command(directory, "get");
        command.env("NBGV_GitEngine", scenario["gitEngine"].as_str().unwrap());
        let actual = version_from(&success(command.output().unwrap()));
        assert_eq!(actual, oracle, "{name}");
        if name == "dirty-working-tree" {
            assert!(actual.git_commit_id.as_deref().unwrap().ends_with("-dirty"));
            assert!(
                actual
                    .git_commit_id_short
                    .as_deref()
                    .unwrap()
                    .ends_with("-dirty")
            );
        } else if name == "disabled-no-git-data" {
            assert_eq!(actual.git_commit_id, None);
            assert_eq!(actual.git_commit_id_short, None);
            assert_eq!(actual.git_commit_date, None);
        }
    }
}

#[test]
fn recalculates_checkout_without_recursive_or_ancestor_watches() {
    let directory = repository(r#"{"version":"1.0"}"#);
    fs::write(directory.path().join("version.txt"), "4.5\nbeta").unwrap();
    let child = directory.path().join("child");
    let nested = child.join("nested");
    fs::create_dir_all(&nested).unwrap();
    commit(directory.path());
    let output = success(
        worker_command(&nested.join("..").join("nested"), "emit")
            .output()
            .unwrap(),
    );
    assert!(version_from(&output).sem_ver2.starts_with("4.5.1-beta"));
    assert!(output.contains(&format!(
        "cargo::rerun-if-changed={}",
        nested.join(".nbgv-always-recalculate").display()
    )));
    assert_eq!(
        output
            .lines()
            .filter(|line| line.starts_with("cargo::rerun-if-changed="))
            .count(),
        1
    );
    assert!(
        !output
            .lines()
            .filter(|line| line.starts_with("cargo::"))
            .any(|line| line.contains("\\..\\"))
    );
    assert!(!output.contains(&format!(
        "cargo::rerun-if-changed={}",
        directory.path().parent().unwrap().join("version.json").display()
    )));
}

#[test]
fn recalculates_linked_worktrees_without_recursive_git_watches() {
    let directory = repository(r#"{"version":"1.2"}"#);
    let linked = directory.path().join("linked");
    let root_repo = Repository::open(directory.path()).unwrap();
    root_repo.worktree("linked", &linked, None).unwrap();
    let output = success(worker_command(&linked, "emit").output().unwrap());
    assert!(output.contains(&format!(
        "cargo::rerun-if-changed={}",
        linked.join(".nbgv-always-recalculate").display()
    )));
    for line in output
        .lines()
        .filter_map(|line| line.strip_prefix("cargo::rerun-if-changed="))
    {
        assert!(!Path::new(line).exists(), "{line}");
    }
    assert_eq!(version_from(&output).version_height, 1);
}

#[test]
fn preserves_dirty_commit_suffixes_without_duplicate_validation() {
    let directory = repository(r#"{"version":"1.2","gitCommitIdIncludeDirty":true}"#);
    fs::write(directory.path().join("dirty.txt"), "dirty").unwrap();
    let output = success(worker_command(directory.path(), "emit").output().unwrap());
    let actual = version_from(&output);
    assert!(actual.git_commit_id.unwrap().ends_with("-dirty"));
    assert!(actual.git_commit_id_short.unwrap().ends_with("-dirty"));
}

#[test]
fn honors_cloud_and_release_overrides_and_tracks_them() {
    let directory =
        repository(r#"{"version":"1.2","publicReleaseRefSpec":["^refs/heads/release$"]}"#);
    let mut command = worker_command(directory.path(), "emit");
    command
        .env("GITHUB_ACTIONS", "true")
        .env("GITHUB_EVENT_NAME", "push")
        .env("GITHUB_REF", "refs/heads/release");
    let output = success(command.output().unwrap());
    assert!(version_from(&output).public_release);
    for name in [
        "NBGV_GitEngine",
        "PublicRelease",
        "GITHUB_ACTOR",
        "DEPENDABOT",
        "GITHUB_REF",
        "IGNORE_GITHUB_REF",
        "CI_COMMIT_SHA",
    ] {
        assert!(output.contains(&format!("cargo::rerun-if-env-changed={name}")));
    }
    command.env("PublicRelease", " FaLsE ");
    assert!(!version_from(&success(command.output().unwrap())).public_release);
    command.env("PublicRelease", " TrUe ");
    assert!(version_from(&success(command.output().unwrap())).public_release);
}

fn stamp() -> Version {
    Version {
        schema_version: 1,
        sem_ver2: "7.8.9-beta.1+archive".to_owned(),
        version: "7.8.9.123".to_owned(),
        version_height: 42,
        git_commit_id: Some("0123456789abcdef0123456789abcdef01234567-dirty".to_owned()),
        git_commit_id_short: Some("01234567-dirty".to_owned()),
        git_commit_date: Some("2026-08-09T21:59:55Z".to_owned()),
        public_release: true,
    }
}

#[test]
fn archive_reads_the_shared_serialized_dto_and_tracks_only_package_inputs() {
    let directory = archive_fixture();
    let expected = stamp();
    fs::write(
        directory.path().join(".nbgv-version.json"),
        serde_json::to_vec(&expected).unwrap(),
    )
    .unwrap();
    fs::write(
        directory.path().join("Cargo.toml"),
        format!(
            "[package]\nname = \"archive\"\nversion = \"{}\"\n",
            expected.sem_ver2
        ),
    )
    .unwrap();
    let output = success(worker_command(directory.path(), "emit").output().unwrap());
    assert_eq!(version_from(&output), expected);
    assert!(output.contains(&format!(
        "cargo::rerun-if-changed={}",
        directory.path().join(".nbgv-version.json").display()
    )));
    assert!(!output.contains(&format!(
            "cargo::rerun-if-changed={}",
            directory
                .path()
                .parent()
                .unwrap()
                .join("version.json")
                .display()
        )));
    assert!(!output.contains("packed-refs"));
    assert_eq!(
        output
            .lines()
            .filter(|line| line.starts_with("cargo::rerun-if-changed="))
            .count(),
        1
    );
}

#[test]
fn archive_rejects_missing_corrupt_and_unsupported_stamps() {
    let directory = archive_fixture();
    fs::write(
        directory.path().join("version.json"),
        r#"{"version":"3.4"}"#,
    )
    .unwrap();
    let output = success(worker_command(directory.path(), "error").output().unwrap());
    assert!(output.contains("No Git repository or precomputed version stamp"));
    let path = directory.path().join(".nbgv-version.json");
    fs::write(&path, "not JSON").unwrap();
    let output = success(worker_command(directory.path(), "error").output().unwrap());
    assert!(output.contains("ERROR:"));
    let mut invalid = stamp();
    invalid.schema_version = 2;
    fs::write(path, serde_json::to_vec(&invalid).unwrap()).unwrap();
    let output = success(worker_command(directory.path(), "error").output().unwrap());
    assert!(output.contains("schemaVersion 2"));
    let wrong_type = serde_json::to_string(&stamp())
        .unwrap()
        .replace("\"versionHeight\":42", "\"versionHeight\":\"42\"");
    fs::write(directory.path().join(".nbgv-version.json"), wrong_type).unwrap();
    let output = success(worker_command(directory.path(), "error").output().unwrap());
    assert!(output.contains("invalid type"));
}

#[test]
fn archive_preserves_nullable_and_empty_native_fields_and_unknown_properties() {
    let directory = archive_fixture();
    let path = directory.path().join(".nbgv-version.json");
    let mut expected = stamp();
    for (full, short, date) in [
        (None, None, None),
        (Some("0123456789abcdef"), Some(""), None),
        (None, None, Some("2026-08-09T21:59:55Z")),
    ] {
        expected.git_commit_id = full.map(str::to_owned);
        expected.git_commit_id_short = short.map(str::to_owned);
        expected.git_commit_date = date.map(str::to_owned);
        let json = serde_json::to_string(&expected)
            .unwrap()
            .strip_suffix('}')
            .unwrap()
            .to_owned()
            + ",\"futureProperty\":{\"ignored\":true}}";
        fs::write(&path, json).unwrap();
        let output = success(worker_command(directory.path(), "emit").output().unwrap());
        assert_eq!(version_from(&output), expected);
        if expected.git_commit_id.is_none() {
            assert!(output.contains("cargo::rustc-env=NBGV_GIT_COMMIT_ID=\n"));
        }
    }
}

#[test]
fn archive_does_not_search_ancestor_stamps() {
    let directory = archive_fixture();
    fs::write(
        directory.path().join(".nbgv-version.json"),
        serde_json::to_vec(&stamp()).unwrap(),
    )
    .unwrap();
    let child = directory.path().join("other-package");
    fs::create_dir(&child).unwrap();
    let output = success(worker_command(&child, "error").output().unwrap());
    assert!(output.contains("No Git repository or precomputed version stamp"));
}

#[test]
fn uses_local_stamp_in_repository_but_not_with_git_disabled() {
    let directory = repository(r#"{"version":"3.4"}"#);
    let package = directory.path().join("vendor").join("package");
    fs::create_dir_all(&package).unwrap();
    fs::write(
        package.join(".nbgv-version.json"),
        serde_json::to_vec(&stamp()).unwrap(),
    )
    .unwrap();
    let output = success(worker_command(&package, "emit").output().unwrap());
    assert_eq!(version_from(&output), stamp());
    assert!(output.contains(&format!(
        "cargo::rerun-if-changed={}",
        package.join(".nbgv-version.json").display()
    )));
    fs::write(directory.path().join(".nbgv-version.json"), "invalid JSON").unwrap();
    let mut command = worker_command(directory.path(), "get");
    command.env("NBGV_GitEngine", "Disabled");
    assert_eq!(
        version_from(&success(command.output().unwrap())).version_height,
        0
    );

    let archive = archive_fixture();
    fs::write(archive.path().join("version.txt"), "5.6\nbeta").unwrap();
    fs::write(archive.path().join(".nbgv-version.json"), "invalid JSON").unwrap();
    let mut command = worker_command(archive.path(), "emit");
    command.env("NBGV_GitEngine", "Disabled");
    let output = success(command.output().unwrap());
    let actual = version_from(&output);
    assert_eq!(actual.version_height, 0);
    assert!(actual.sem_ver2.starts_with("5.6"));
    assert!(!output.contains(&format!(
        "cargo::rerun-if-changed={}",
        archive.path().join(".nbgv-version.json").display()
    )));
    assert!(output.contains(&format!(
        "cargo::rerun-if-changed={}",
        archive.path().join(".nbgv-always-recalculate").display()
    )));
}

#[test]
fn refuses_an_existing_always_recalculate_sentinel() {
    let directory = repository(r#"{"version":"1.2"}"#);
    fs::write(directory.path().join(".nbgv-always-recalculate"), "").unwrap();
    let output = success(
        worker_command(directory.path(), "emit_error")
            .output()
            .unwrap(),
    );
    assert!(output.contains("reserved for Cargo change detection"));
}

#[test]
fn missing_environment_and_filesystem_errors_remain_actionable() {
    let directory = fixture();
    assert!(
        success(
            worker_command(directory.path(), "missing_manifest")
                .output()
                .unwrap()
        )
        .contains("CARGO_MANIFEST_DIR")
    );
    assert!(
        success(
            worker_command(directory.path(), "missing_out")
                .output()
                .unwrap()
        )
        .contains("OUT_DIR")
    );
    let error = Config::new()
        .project_dir(directory.path().join("missing"))
        .get_version()
        .unwrap_err();
    assert!(matches!(error, Error::Io(_)));
    assert!(std::error::Error::source(&error).is_some());
}

#[test]
fn generated_module_and_both_macros_compile_without_runtime_dependencies() {
    let directory = archive_fixture();
    let mut expected = stamp();
    // Preserve escaping regressions without adding redundant native DTO validation.
    expected.git_commit_id_short = Some("quote\"slash\\newline\n".to_owned());
    fs::write(
        directory.path().join(".nbgv-version.json"),
        serde_json::to_vec(&expected).unwrap(),
    )
    .unwrap();
    for mode in ["module", "macro", "custom_macro"] {
        let out_dir = directory.path().join(mode);
        fs::create_dir(&out_dir).unwrap();
        let mut command = worker_command(directory.path(), mode);
        command.env("OUT_DIR", &out_dir);
        assert_eq!(version_from(&success(command.output().unwrap())), expected);
        let generated = out_dir.join("nbgv.rs");
        let modified = fs::metadata(&generated).unwrap().modified().unwrap();
        assert_eq!(version_from(&success(command.output().unwrap())), expected);
        assert_eq!(
            fs::metadata(&generated).unwrap().modified().unwrap(),
            modified
        );
        let source = out_dir.join("main.rs");
        fs::write(&source, format!(
            "include!(\"nbgv.rs\");\nfn main() {{ assert_eq!(NBGV_VERSION.sem_ver2, {:?}); assert_eq!(NBGV_VERSION.git_commit_id_short, Some({:?})); assert_eq!(NBGV_VERSION.version_height, 42u32); }}",
            expected.sem_ver2, expected.git_commit_id_short.as_deref().unwrap()
        )).unwrap();
        let executable = out_dir.join(if cfg!(windows) {
            "generated.exe"
        } else {
            "generated"
        });
        assert!(
            Command::new("rustc")
                .arg(&source)
                .arg("-o")
                .arg(&executable)
                .status()
                .unwrap()
                .success()
        );
        assert!(Command::new(executable).status().unwrap().success());
    }
}
