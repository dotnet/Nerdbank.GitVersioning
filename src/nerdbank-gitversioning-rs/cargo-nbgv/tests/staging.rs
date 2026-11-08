// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

use flate2::read::GzDecoder;
use serde_json::Value;
use std::fs::{self, File};
use std::io::Read;
use std::path::{Path, PathBuf};
use std::process::{Command, Output};
use tempfile::TempDir;

fn fixture() -> TempDir {
    tempfile::Builder::new()
        .prefix("cargo-nbgv-test-")
        .tempdir_in(env!("CARGO_MANIFEST_DIR"))
        .unwrap()
}

fn git(root: &Path, args: &[&str]) {
    let output = Command::new("git")
        .args(args)
        .current_dir(root)
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "git failed: {}",
        String::from_utf8_lossy(&output.stderr)
    );
}

fn commit_fixture(root: &Path) {
    git(root, &["init", "--quiet"]);
    git(root, &["add", "."]);
    git(
        root,
        &[
            "-c",
            "user.name=NB.GV Test",
            "-c",
            "user.email=nbgv@example.invalid",
            "commit",
            "--quiet",
            "-m",
            "initial",
        ],
    );
}

fn cargo(root: &Path, args: &[&str]) {
    let output = Command::new(env!("CARGO"))
        .args(args)
        .current_dir(root)
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "cargo failed: {}",
        String::from_utf8_lossy(&output.stderr)
    );
}

fn run(manifest: &Path, args: &[&str]) -> Output {
    Command::new(env!("CARGO_BIN_EXE_cargo-nbgv"))
        .arg("package")
        .arg("--manifest-path")
        .arg(manifest)
        .args(args)
        .env_remove("NBGV_GitEngine")
        .env_remove("PublicRelease")
        .output()
        .unwrap()
}

fn archive_file(path: &Path, name: &str) -> String {
    let mut archive = tar::Archive::new(GzDecoder::new(File::open(path).unwrap()));
    for entry in archive.entries().unwrap() {
        let mut entry = entry.unwrap();
        if entry.path().unwrap().ends_with(name) {
            let mut contents = String::new();
            entry.read_to_string(&mut contents).unwrap();
            return contents;
        }
    }
    panic!("{name} missing from {}", path.display());
}

fn create_package(root: &Path, manifest: &str) -> PathBuf {
    fs::create_dir_all(root.join("src")).unwrap();
    fs::write(root.join("Cargo.toml"), manifest).unwrap();
    fs::write(
        root.join("src").join("lib.rs"),
        "pub const VALUE: u8 = 1;\n",
    )
    .unwrap();
    fs::write(
        root.join("version.json"),
        r#"{"version":"2.3.4","publicReleaseRefSpec":[".*"]}"#,
    )
    .unwrap();
    root.join("Cargo.toml")
}

#[test]
fn package_stamps_archive_and_preserves_source_with_locked() {
    let fixture = fixture();
    let root = fixture.path();
    let original = r#"[package]
name = "nbgv-staging-test"
version = "0.0.1"
edition = "2021"
include = ["src/**", ".hidden"]

[workspace]
"#;
    let manifest = create_package(root, original);
    fs::write(root.join(".hidden"), "included hidden file").unwrap();
    cargo(root, &["generate-lockfile", "--offline"]);
    let original_lock = fs::read(root.join("Cargo.lock")).unwrap();
    commit_fixture(root);
    let output = run(&manifest, &["--", "--locked", "--offline", "--no-verify"]);
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    assert_eq!(fs::read_to_string(&manifest).unwrap(), original);
    assert_eq!(fs::read(root.join("Cargo.lock")).unwrap(), original_lock);
    assert!(!root.join(".nbgv-version.json").exists());
    let archive = root
        .join("target")
        .join("package")
        .join("nbgv-staging-test-2.3.4.crate");
    let stamp: Value = serde_json::from_str(&archive_file(&archive, ".nbgv-version.json")).unwrap();
    assert_eq!(stamp["schemaVersion"], 1);
    assert_eq!(stamp["semVer2"], "2.3.4");
    assert_eq!(stamp["publicRelease"], true);
    assert!(stamp["gitCommitId"].as_str().is_some_and(|s| s.len() == 40));
    let staged_manifest = archive_file(&archive, "Cargo.toml");
    assert_eq!(archive_file(&archive, ".hidden"), "included hidden file");
    assert!(staged_manifest.contains("version = \"2.3.4\""));
    let lock = archive_file(&archive, "Cargo.lock");
    assert!(lock.contains("name = \"nbgv-staging-test\"\nversion = \"2.3.4\""));
}

#[test]
fn package_materializes_workspace_values_and_external_files() {
    let fixture = fixture();
    let root = fixture.path();
    let member = root.join("member");
    fs::create_dir_all(&member).unwrap();
    fs::write(
        root.join("Cargo.toml"),
        r#"[workspace]
members = ["member"]
resolver = "2"
[workspace.package]
version = "0.0.1"
edition = "2021"
authors = ["Workspace Author"]
license-file = "LICENSE"
readme = "README.md"
repository = "https://example.invalid/repository"
"#,
    )
    .unwrap();
    fs::write(root.join("README.md"), "external readme").unwrap();
    fs::write(root.join("LICENSE"), "external license").unwrap();
    let original = r#"[package]
name = "nbgv-workspace-staging-test"
include = ["**"]
version.workspace = true
edition.workspace = true
authors.workspace = true
license-file.workspace = true
readme.workspace = true
repository.workspace = true
"#;
    let manifest = create_package(&member, original);
    cargo(root, &["generate-lockfile", "--offline"]);
    let original_lock = fs::read(root.join("Cargo.lock")).unwrap();
    commit_fixture(root);
    let output = run(&manifest, &["--", "--locked", "--offline", "--no-verify"]);
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    assert_eq!(fs::read_to_string(manifest).unwrap(), original);
    assert_eq!(fs::read(root.join("Cargo.lock")).unwrap(), original_lock);
    let archive = root
        .join("target")
        .join("package")
        .join("nbgv-workspace-staging-test-2.3.4.crate");
    assert_eq!(archive_file(&archive, "README.md"), "external readme");
    assert_eq!(archive_file(&archive, "LICENSE"), "external license");
    let manifest = archive_file(&archive, "Cargo.toml");
    assert!(manifest.contains("edition = \"2021\""));
    assert!(manifest.contains("authors = [\"Workspace Author\"]"));
    assert!(manifest.contains("repository = \"https://example.invalid/repository\""));
    assert!(!manifest.contains(".workspace = true"));
    let lock = archive_file(&archive, "Cargo.lock");
    assert!(lock.contains("name = \"nbgv-workspace-staging-test\"\nversion = \"2.3.4\""));
}

#[test]
fn dirty_source_requires_explicit_opt_in() {
    let fixture = fixture();
    let root = fixture.path();
    let manifest = create_package(
        root,
        "[package]\nname = \"nbgv-dirty-test\"\nversion = \"0.1.0\"\nedition = \"2021\"\ninclude = [\"src/**\"]\n[workspace]\n",
    );
    cargo(root, &["generate-lockfile", "--offline"]);
    commit_fixture(root);
    fs::write(
        root.join("src").join("lib.rs"),
        "pub const VALUE: u8 = 2;\n",
    )
    .unwrap();
    let status = Command::new("git")
        .args(["status", "--porcelain"])
        .current_dir(root)
        .output()
        .unwrap();
    assert!(
        String::from_utf8_lossy(&status.stdout).contains("src/lib.rs"),
        "{}",
        String::from_utf8_lossy(&status.stdout)
    );
    let direct = Command::new(env!("CARGO"))
        .args(["package", "--no-verify", "--offline"])
        .current_dir(root)
        .output()
        .unwrap();
    assert!(
        !direct.status.success(),
        "direct cargo accepted dirty tree: {}",
        String::from_utf8_lossy(&direct.stderr)
    );
    let output = run(&manifest, &["--", "--offline", "--no-verify"]);
    assert!(
        !output.status.success(),
        "stdout: {}\nstderr: {}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    assert!(
        String::from_utf8_lossy(&output.stderr).contains("contain changes"),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let output = run(
        &manifest,
        &["--", "--allow-dirty", "--offline", "--no-verify"],
    );
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
}

#[test]
fn removed_executable_option_and_nonpublic_publish_are_rejected() {
    let fixture = fixture();
    let root = fixture.path();
    let manifest = create_package(
        root,
        "[package]\nname = \"nbgv-publish-test\"\nversion = \"0.1.0\"\nedition = \"2021\"\n[workspace]\n",
    );
    cargo(root, &["generate-lockfile", "--offline"]);
    commit_fixture(root);
    let output = run(&manifest, &["--nbgv-executable", "fake"]);
    assert!(!output.status.success());
    assert!(String::from_utf8_lossy(&output.stderr).contains("unknown cargo-nbgv option"));
    let output = Command::new(env!("CARGO_BIN_EXE_cargo-nbgv"))
        .arg("publish")
        .arg("--manifest-path")
        .arg(&manifest)
        .env_remove("NBGV_GitEngine")
        .env("PublicRelease", "false")
        .output()
        .unwrap();
    assert!(!output.status.success());
    assert!(
        String::from_utf8_lossy(&output.stderr)
            .contains("refusing to publish a non-public version")
    );
    let output = Command::new(env!("CARGO_BIN_EXE_cargo-nbgv"))
        .arg("publish")
        .arg("--manifest-path")
        .arg(&manifest)
        .args([
            "--allow-non-public",
            "--",
            "--offline",
            "--not-a-cargo-option",
        ])
        .env_remove("NBGV_GitEngine")
        .env("PublicRelease", "false")
        .output()
        .unwrap();
    assert!(!output.status.success());
    let stderr = String::from_utf8_lossy(&output.stderr);
    assert!(!stderr.contains("refusing to publish"), "{stderr}");
    assert!(stderr.contains("--not-a-cargo-option"), "{stderr}");
}

#[test]
fn packaged_crate_builds_from_stamp_without_repository() {
    let source = tempfile::tempdir().unwrap();
    let extracted = tempfile::tempdir().unwrap();
    let root = source.path();
    let native = fs::canonicalize(Path::new(env!("CARGO_MANIFEST_DIR")).join("..")).unwrap();
    let helper = native.join("build");
    let toml_path = |path: &Path| serde_json::to_string(&path.to_string_lossy()).unwrap();

    fs::create_dir_all(root.join(".cargo")).unwrap();
    let config = root.join(".cargo").join("config.toml");
    fs::write(
        &config,
        format!(
            "[patch.crates-io]\nnerdbank-gitversioning = {{ path = {} }}\nnerdbank-gitversioning-build = {{ path = {} }}\n",
            toml_path(&native),
            toml_path(&helper),
        ),
    )
    .unwrap();
    let original = r#"[package]
name = "nbgv-archive-consumer"
version = "0.0.1"
edition = "2021"
include = ["src/**", "build.rs"]

[build-dependencies]
nerdbank-gitversioning-build = "0.1.0"

[workspace]
"#;
    let manifest = create_package(root, original);
    fs::remove_file(root.join("src").join("lib.rs")).unwrap();
    fs::write(
        root.join("build.rs"),
        "fn main() { nerdbank_gitversioning_build::emit!(); }\n",
    )
    .unwrap();
    fs::write(
        root.join("src").join("main.rs"),
        r#"include!(concat!(env!("OUT_DIR"), "/nbgv.rs"));
fn main() {
    println!("{}", NBGV_VERSION.sem_ver2);
    println!("{}", NBGV_VERSION.version);
    println!("{}", NBGV_VERSION.version_height);
    println!("{}", NBGV_VERSION.git_commit_id.unwrap());
    println!("{}", NBGV_VERSION.git_commit_id_short.unwrap());
    println!("{}", NBGV_VERSION.git_commit_date.unwrap());
    println!("{}", NBGV_VERSION.public_release);
}
"#,
    )
    .unwrap();
    cargo(root, &["generate-lockfile", "--offline"]);
    let original_lock = fs::read(root.join("Cargo.lock")).unwrap();
    commit_fixture(root);

    let missing_tool = root.join("nonexistent-nbgv");
    let output = Command::new(env!("CARGO_BIN_EXE_cargo-nbgv"))
        .current_dir(root)
        .arg("package")
        .arg("--manifest-path")
        .arg(&manifest)
        .args(["--", "--offline", "--no-verify"])
        .env_remove("NBGV_GitEngine")
        .env_remove("PublicRelease")
        .env_remove("GIT_DIR")
        .env_remove("GIT_WORK_TREE")
        .env("CARGO_TARGET_DIR", root.join("target"))
        .env("NBGV_EXECUTABLE", &missing_tool)
        .env("DOTNET_HOST_PATH", root.join("nonexistent-dotnet"))
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "packaging failed:\n{}",
        String::from_utf8_lossy(&output.stderr)
    );
    assert_eq!(fs::read_to_string(&manifest).unwrap(), original);
    assert_eq!(fs::read(root.join("Cargo.lock")).unwrap(), original_lock);
    assert!(!root.join(".nbgv-version.json").exists());
    let archive_path = root
        .join("target")
        .join("package")
        .join("nbgv-archive-consumer-2.3.4.crate");
    let stamp: Value =
        serde_json::from_str(&archive_file(&archive_path, ".nbgv-version.json")).unwrap();
    assert_eq!(stamp["schemaVersion"], 1);
    assert_eq!(stamp["semVer2"], "2.3.4");
    assert_eq!(stamp["publicRelease"], true);
    assert!(
        stamp["gitCommitId"]
            .as_str()
            .is_some_and(|id| id.len() == 40)
    );
    assert!(stamp["gitCommitIdShort"].as_str().is_some());
    assert!(stamp["gitCommitDate"].as_str().is_some());
    assert!(stamp["versionHeight"].as_u64().is_some());
    tar::Archive::new(GzDecoder::new(File::open(&archive_path).unwrap()))
        .unpack(extracted.path())
        .unwrap();
    let package_dir = extracted.path().join("nbgv-archive-consumer-2.3.4");
    assert!(!package_dir.join(".git").exists());
    let error = git2::Repository::discover(&package_dir).err().unwrap();
    assert_eq!(error.code(), git2::ErrorCode::NotFound);

    let output = Command::new(env!("CARGO"))
        .current_dir(&package_dir)
        .args(["build", "--offline", "--config"])
        .arg(&config)
        .env_remove("NBGV_GitEngine")
        .env_remove("PublicRelease")
        .env_remove("GIT_DIR")
        .env_remove("GIT_WORK_TREE")
        .env("CARGO_TARGET_DIR", package_dir.join("target"))
        .env("NBGV_EXECUTABLE", &missing_tool)
        .env("DOTNET_HOST_PATH", root.join("nonexistent-dotnet"))
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "archive build failed:\n{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let binary = package_dir.join("target").join("debug").join(format!(
        "nbgv-archive-consumer{}",
        std::env::consts::EXE_SUFFIX
    ));
    let output = Command::new(binary).output().unwrap();
    assert!(
        output.status.success(),
        "archive executable failed:\n{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let stdout = String::from_utf8(output.stdout).unwrap();
    let actual: Vec<_> = stdout.lines().collect();
    let expected = vec![
        stamp["semVer2"].as_str().unwrap().to_owned(),
        stamp["version"].as_str().unwrap().to_owned(),
        stamp["versionHeight"].as_u64().unwrap().to_string(),
        stamp["gitCommitId"].as_str().unwrap().to_owned(),
        stamp["gitCommitIdShort"].as_str().unwrap().to_owned(),
        stamp["gitCommitDate"].as_str().unwrap().to_owned(),
        stamp["publicRelease"].as_bool().unwrap().to_string(),
    ];
    assert_eq!(actual, expected);
}
