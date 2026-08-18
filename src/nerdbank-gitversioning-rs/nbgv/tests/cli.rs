use std::fs;
use std::path::Path;

use assert_cmd::Command;
use git2::{IndexAddOption, Oid, Repository, Signature};
use nerdbank_gitversioning::{MAXIMUM_VERSION_COMPONENT, truncated_commit_id};
use predicates::prelude::*;
use tempfile::TempDir;

struct TestRepository {
    directory: TempDir,
}

impl TestRepository {
    fn new(version_json: &str) -> Self {
        let directory = tempfile::tempdir().unwrap();
        let repository = Repository::init(directory.path()).unwrap();
        {
            let mut config = repository.config().unwrap();
            config.set_str("user.name", "Rust CLI Test").unwrap();
            config.set_str("user.email", "test@example.com").unwrap();
            config.set_bool("commit.gpgSign", false).unwrap();
        }
        fs::write(directory.path().join("version.json"), version_json).unwrap();
        commit_all(&repository, "initial");
        Self { directory }
    }

    fn path(&self) -> &Path {
        self.directory.path()
    }

    fn repository(&self) -> Repository {
        Repository::open(self.path()).unwrap()
    }

    fn commit(&self, name: &str) -> Oid {
        fs::write(self.path().join(name), name).unwrap();
        commit_all(&self.repository(), name)
    }

    fn head(&self) -> Oid {
        self.repository()
            .head()
            .unwrap()
            .peel_to_commit()
            .unwrap()
            .id()
    }
}

fn commit_all(repository: &Repository, message: &str) -> Oid {
    let mut index = repository.index().unwrap();
    index.add_all(["*"], IndexAddOption::DEFAULT, None).unwrap();
    index.write().unwrap();
    let tree_id = index.write_tree().unwrap();
    let tree = repository.find_tree(tree_id).unwrap();
    let signature = Signature::now("Rust CLI Test", "test@example.com").unwrap();
    let parents = repository
        .head()
        .ok()
        .and_then(|head| head.peel_to_commit().ok())
        .into_iter()
        .collect::<Vec<_>>();
    let parent_refs = parents.iter().collect::<Vec<_>>();
    repository
        .commit(
            Some("HEAD"),
            &signature,
            &signature,
            message,
            &tree,
            &parent_refs,
        )
        .unwrap()
}

fn nbgv() -> Command {
    Command::new(assert_cmd::cargo::cargo_bin!("nbgv"))
}

#[test]
fn root_help_and_version_identify_rust_and_exclude_unimplemented_commands() {
    nbgv()
        .arg("--help")
        .assert()
        .success()
        .stdout(predicate::str::contains("Rust CLI"))
        .stdout(predicate::str::contains("get-version"))
        .stdout(predicate::str::contains("prepare-release"))
        .stdout(predicate::str::contains("install").not())
        .stdout(predicate::str::contains("path-filters").not());
    nbgv()
        .arg("--version")
        .assert()
        .success()
        .stdout(predicate::str::starts_with("nbgv "));
    nbgv().arg("install").assert().code(1);
    nbgv().arg("set-version").assert().code(1);
}

#[test]
fn get_version_supports_formats_variables_precedence_dates_and_dirty_warning() {
    let repo = TestRepository::new(
        r#"{"version":"1.2-beta","publicReleaseRefSpec":["^refs/heads/never$"]}"#,
    );
    nbgv()
        .current_dir(repo.path())
        .args(["get-version", "--public-release=false"])
        .env("PublicRelease", "TRUE")
        .assert()
        .success()
        .stdout(predicate::str::contains("Version:"))
        .stdout(predicate::str::contains("NpmPackageVersion:"));
    nbgv()
        .current_dir(repo.path())
        .args([
            "get-version",
            "--format",
            "json",
            "--public-release",
            "--metadata",
            "one",
        ])
        .assert()
        .success()
        .stdout(predicate::str::contains("\"PublicRelease\": true"))
        .stdout(predicate::str::contains("\"BuildMetadata\""));
    nbgv()
        .current_dir(repo.path())
        .args(["get-version", "--variable", "gitcommitdate"])
        .assert()
        .success()
        .stdout(predicate::str::is_match(r"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d[+-]\d\d:\d\d").unwrap());
    nbgv()
        .current_dir(repo.path())
        .args([
            "get-version",
            "--format",
            "TEXT",
            "--variable",
            "VersionMajor",
        ])
        .assert()
        .success()
        .stdout("1\n");

    fs::write(repo.path().join("version.json"), r#"{"version":"9.0"}"#).unwrap();
    nbgv()
        .current_dir(repo.path())
        .args(["get-version", "-v", "Version"])
        .assert()
        .success()
        .stderr(predicate::str::contains("Warning: Dirty version.json"));
    nbgv()
        .current_dir(repo.path())
        .args(["get-version", "-f", "JSON", "-v", "Version"])
        .assert()
        .code(6);
    nbgv()
        .current_dir(repo.path())
        .args(["get-version", "-v", "notAProperty"])
        .assert()
        .code(12);
}

#[test]
fn get_version_exact_output_and_diagnostics_match_managed_contract() {
    let repo = TestRepository::new(r#"{"version":"1.2"}"#);
    let revision = u32::from(truncated_commit_id(repo.head())).min(MAXIMUM_VERSION_COMPONENT);
    nbgv()
        .current_dir(repo.path())
        .args(["get-version", "--variable", "Version"])
        .assert()
        .success()
        .stdout(format!("1.2.1.{revision}\n"))
        .stderr("");

    nbgv()
        .current_dir(repo.path())
        .args(["get-version", "--format", "JSON", "--variable", "Version"])
        .assert()
        .code(6)
        .stdout("")
        .stderr("Format must be \"text\" when querying for an individual variable's value.\n");
    nbgv()
        .current_dir(repo.path())
        .args(["get-version", "--variable", "notAProperty"])
        .assert()
        .code(12)
        .stdout("")
        .stderr("Variable \"notAProperty\" not a version property.\n");
}

#[test]
fn get_version_exposes_cloud_build_enabled_scalar_variables_case_insensitively() {
    let repo = TestRepository::new(
        r#"{"version":"1.2","cloudBuild":{"setAllVariables":false,"setVersionVariables":false}}"#,
    );
    for variable in [
        "CloudBuildAllVarsEnabled",
        "cLoUdBuIlDaLlVaRsEnAbLeD",
        "CloudBuildVersionVarsEnabled",
        "cLoUdBuIlDvErSiOnVaRsEnAbLeD",
    ] {
        nbgv()
            .current_dir(repo.path())
            .args(["get-version", "--variable", variable])
            .assert()
            .success()
            .stdout("False\n")
            .stderr("");
    }
}

#[test]
fn get_version_dirty_warning_follows_the_version_file_search_chain() {
    const WARNING: &str = "Warning: Dirty version.json files must be committed before their changes will be applied.\n";

    let inherited = TestRepository::new(r#"{"version":"1.0"}"#);
    fs::create_dir(inherited.path().join("src")).unwrap();
    fs::write(
        inherited.path().join("src/version.json"),
        r#"{"inherit":true}"#,
    )
    .unwrap();
    commit_all(&inherited.repository(), "inheriting version");
    fs::write(
        inherited.path().join("version.json"),
        r#"{"version":"2.0"}"#,
    )
    .unwrap();
    nbgv()
        .current_dir(inherited.path())
        .args([
            "get-version",
            "--project",
            "src",
            "--variable",
            "VersionMajor",
        ])
        .assert()
        .success()
        .stdout("2\n")
        .stderr(WARNING);

    let shadowed = TestRepository::new(r#"{"version":"1.0"}"#);
    fs::create_dir(shadowed.path().join("src")).unwrap();
    fs::write(
        shadowed.path().join("src/version.json"),
        r#"{"version":"2.0"}"#,
    )
    .unwrap();
    commit_all(&shadowed.repository(), "child version");
    fs::write(shadowed.path().join("version.json"), r#"{"version":"3.0"}"#).unwrap();
    nbgv()
        .current_dir(shadowed.path())
        .args([
            "get-version",
            "--project",
            "src",
            "--variable",
            "VersionMajor",
        ])
        .assert()
        .success()
        .stdout("2\n")
        .stderr("");

    fs::write(
        shadowed.path().join("src/version.json"),
        r#"{"version":"4.0"}"#,
    )
    .unwrap();
    {
        let repository = shadowed.repository();
        let mut index = repository.index().unwrap();
        index.add_path(Path::new("src/version.json")).unwrap();
        index.write().unwrap();
    }
    nbgv()
        .current_dir(shadowed.path())
        .args([
            "get-version",
            "--project",
            "src",
            "--variable",
            "VersionMajor",
        ])
        .assert()
        .success()
        .stdout("4\n")
        .stderr(WARNING);
}

#[test]
fn get_version_reports_shallow_history_with_dedicated_exit_code() {
    let repo = TestRepository::new(r#"{"version":"1.0"}"#);
    let parent = repo.head();
    repo.commit("child.txt");
    let repository = repo.repository();
    fs::write(repository.path().join("shallow"), format!("{parent}\n")).unwrap();
    let parent_path = repository
        .path()
        .join("objects")
        .join(&parent.to_string()[..2])
        .join(&parent.to_string()[2..]);
    drop(repository);
    fs::remove_file(parent_path).unwrap();

    nbgv()
        .current_dir(repo.path())
        .args(["get-version", "--variable", "Version"])
        .assert()
        .code(23)
        .stdout("")
        .stderr(
            predicate::str::starts_with(
                "ERROR: Version calculation failed because the repository is a shallow clone:",
            )
            .and(predicate::str::contains("object"))
            .or(predicate::str::contains("commit")),
        );
}

#[test]
fn get_version_reports_repository_and_ref_failures() {
    let directory = tempfile::tempdir().unwrap();
    nbgv()
        .current_dir(directory.path())
        .arg("get-version")
        .assert()
        .code(1)
        .stderr(predicate::str::contains("No git repo found"));
    let repo = TestRepository::new(r#"{"version":"1.0"}"#);
    nbgv()
        .current_dir(repo.path())
        .args(["get-version", "missing-ref"])
        .assert()
        .code(8)
        .stderr(predicate::str::contains(
            "rev-parse produced no commit for missing-ref",
        ));
}

#[test]
fn set_version_writes_preserves_options_and_stages_file() {
    let repo = TestRepository::new(r#"{"version":"1.0","gitCommitIdPrefix":"c"}"#);
    nbgv()
        .current_dir(repo.path())
        .args(["set-version", "2.3-rc"])
        .assert()
        .success();
    let json = fs::read_to_string(repo.path().join("version.json")).unwrap();
    assert!(json.contains("\"version\": \"2.3-rc\""));
    assert!(json.contains("\"gitCommitIdPrefix\": \"c\""));
    assert!(
        repo.repository()
            .status_file(Path::new("version.json"))
            .unwrap()
            .is_index_new()
            || repo
                .repository()
                .status_file(Path::new("version.json"))
                .unwrap()
                .is_index_modified()
    );
    nbgv()
        .current_dir(repo.path())
        .args(["set-version", "not-version"])
        .assert()
        .code(2);
}

#[test]
fn tag_supports_what_if_creation_conflicts_and_interactive_selection() {
    let repo = TestRepository::new(r#"{"version":"1.2","release":{"tagName":"rel/{version}"}}"#);
    nbgv()
        .current_dir(repo.path())
        .args(["tag", "--what-if"])
        .assert()
        .success()
        .stdout(predicate::str::starts_with("rel/1.2."));
    nbgv()
        .current_dir(repo.path())
        .arg("tag")
        .assert()
        .success()
        .stdout(predicate::str::contains("tag created at"));
    assert!(
        repo.repository()
            .references_glob("refs/tags/rel/*")
            .unwrap()
            .next()
            .is_some()
    );

    let ambiguous = TestRepository::new(r#"{"version":"1.2.3.4"}"#);
    ambiguous.commit("second.txt");
    nbgv()
        .current_dir(ambiguous.path())
        .args(["tag", "1.2.3.4"])
        .write_stdin("1\n")
        .assert()
        .success()
        .stdout(predicate::str::contains("Enter selection:"));

    let invalid = TestRepository::new(r#"{"version":"1.0","release":{"tagName":"fixed"}}"#);
    nbgv()
        .current_dir(invalid.path())
        .arg("tag")
        .assert()
        .code(25);
}

#[test]
fn get_commits_prints_quiet_and_detailed_results_and_rejects_bad_versions() {
    let repo = TestRepository::new(r#"{"version":"3.4.5.6"}"#);
    let head = repo
        .repository()
        .head()
        .unwrap()
        .peel_to_commit()
        .unwrap()
        .id();
    nbgv()
        .current_dir(repo.path())
        .args(["get-commits", "--quiet", "3.4.5.6"])
        .assert()
        .success()
        .stdout(predicate::str::contains(head.to_string()));
    nbgv()
        .current_dir(repo.path())
        .args(["get-commits", "3.4.5.6"])
        .assert()
        .success()
        .stdout(predicate::str::contains(" initial"));
    nbgv()
        .current_dir(repo.path())
        .args(["get-commits", "3.4-beta"])
        .assert()
        .code(2);
}

#[test]
fn cloud_emits_provider_commands_and_stable_failures() {
    let repo = TestRepository::new(r#"{"version":"1.0"}"#);
    nbgv()
        .current_dir(repo.path())
        .args([
            "cloud",
            "--ci-system",
            "visualstudioteamservices",
            "--version",
            "7.8.9",
            "--define",
            "Name=Value",
        ])
        .assert()
        .success()
        .stdout(predicate::str::contains(
            "##vso[build.updatebuildnumber]7.8.9",
        ))
        .stdout(predicate::str::contains("variable=Name;"));
    nbgv()
        .current_dir(repo.path())
        .args(["cloud", "--ci-system", "missing"])
        .assert()
        .code(11);
    nbgv()
        .current_dir(repo.path())
        .args(["cloud", "--define", "bad"])
        .assert()
        .code(3);
    nbgv()
        .current_dir(repo.path())
        .args(["cloud", "-d", "Name=1", "Name=2"])
        .assert()
        .code(4);
}

#[test]
fn prepare_release_supports_json_dry_run_mutations_and_failure_codes() {
    let repo = TestRepository::new(
        r#"{"version":"1.2-beta","release":{"branchName":"release/v{version}","versionIncrement":"minor","firstUnstableTag":"alpha"}}"#,
    );
    nbgv()
        .current_dir(repo.path())
        .args(["prepare-release", "--what-if", "--format", "json"])
        .assert()
        .success()
        .stdout(predicate::str::contains("\"CurrentBranch\""))
        .stdout(predicate::str::contains("\"NewBranch\""));
    nbgv()
        .current_dir(repo.path())
        .args(["prepare-release", "--no-merge", "rc"])
        .assert()
        .success()
        .stdout(predicate::str::contains("release/v1.2 branch now tracks"));
    let repository = repo.repository();
    assert!(
        repository
            .find_branch("release/v1.2", git2::BranchType::Local)
            .is_ok()
    );
    let json = fs::read_to_string(repo.path().join("version.json")).unwrap();
    assert!(json.contains("1.3-alpha"));

    let dirty = TestRepository::new(r#"{"version":"1.0-beta"}"#);
    fs::write(dirty.path().join("dirty.txt"), "dirty").unwrap();
    nbgv()
        .current_dir(dirty.path())
        .arg("prepare-release")
        .assert()
        .code(13)
        .stdout("")
        .stderr(
            predicate::str::contains(
                "No uncommitted changes are allowed, but 1 are present in directory '",
            )
            .and(predicate::str::contains(
                "- dirty.txt changed with FileStatus NewInWorkdir",
            )),
        );
    nbgv()
        .current_dir(dirty.path())
        .args([
            "prepare-release",
            "--nextVersion",
            "2.0",
            "--versionIncrement",
            "major",
        ])
        .assert()
        .code(19);
    nbgv()
        .current_dir(dirty.path())
        .args(["prepare-release", "--versionIncrement", "invalid"])
        .assert()
        .code(20);
    nbgv()
        .current_dir(dirty.path())
        .args(["prepare-release", "--commit-message-pattern", "{1}"])
        .assert()
        .code(26);
}
