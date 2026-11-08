// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

use cargo_metadata::MetadataCommand;
use flate2::read::GzDecoder;
use nerdbank_gitversioning::{AdapterVersionInfo, GitContext, GitEngine, VersionOracle, cloud};
use std::env;
use std::ffi::OsString;
use std::fs::{self, File};
use std::path::{Path, PathBuf};
use std::process::{self, Command};
use std::time::{SystemTime, UNIX_EPOCH};
use toml_edit::{DocumentMut, value};

fn main() {
    if let Err(error) = run(env::args_os().skip(1).collect()) {
        eprintln!("cargo-nbgv: {error}");
        process::exit(1);
    }
}

fn run(mut args: Vec<OsString>) -> Result<(), String> {
    if args.first().is_some_and(|arg| arg == "nbgv") {
        args.remove(0);
    }
    if args
        .first()
        .is_some_and(|arg| arg == "--help" || arg == "-h")
    {
        println!("{}", usage());
        return Ok(());
    }
    let operation = args
        .first()
        .and_then(|arg| arg.to_str())
        .ok_or_else(usage)?
        .to_owned();
    if operation != "package" && operation != "publish" {
        return Err(usage());
    }
    args.remove(0);

    let mut manifest_path = PathBuf::from("Cargo.toml");
    let mut allow_non_public = false;
    let mut cargo_args = Vec::new();
    let mut passthrough = false;
    let mut index = 0;
    while index < args.len() {
        if passthrough {
            cargo_args.push(args[index].clone());
        } else if args[index] == "--" {
            passthrough = true;
        } else if args[index] == "--allow-non-public" {
            allow_non_public = true;
        } else if args[index] == "--manifest-path" {
            index += 1;
            manifest_path = args
                .get(index)
                .map(PathBuf::from)
                .ok_or_else(|| "--manifest-path requires a value".to_owned())?;
        } else {
            return Err(format!(
                "unknown cargo-nbgv option {:?}; Cargo options belong after --",
                args[index]
            ));
        }
        index += 1;
    }
    if cargo_args.iter().any(|argument| {
        let argument = argument.to_string_lossy();
        argument == "--manifest-path" || argument.starts_with("--manifest-path=")
    }) {
        return Err(
            "--manifest-path is managed by cargo-nbgv and cannot be passed after --".into(),
        );
    }

    let manifest_path = fs::canonicalize(&manifest_path)
        .map_err(|error| format!("cannot find {}: {error}", manifest_path.display()))?;
    let manifest_path = cargo_path(manifest_path);
    let package_dir = manifest_path
        .parent()
        .ok_or_else(|| "manifest has no parent directory".to_owned())?;
    let allow_dirty = cargo_args
        .iter()
        .any(|argument| argument == "--allow-dirty");
    let safety_args: Vec<_> = cargo_args
        .iter()
        .filter(|argument| {
            *argument == "--locked" || *argument == "--frozen" || *argument == "--offline"
        })
        .cloned()
        .collect();
    let mut metadata_command = MetadataCommand::new();
    metadata_command.manifest_path(&manifest_path).no_deps();
    metadata_command.other_options(
        safety_args
            .iter()
            .map(|arg| arg.to_string_lossy().into_owned())
            .collect::<Vec<_>>(),
    );
    let metadata = metadata_command
        .exec()
        .map_err(|error| format!("cargo metadata failed: {error}"))?;
    let package = metadata
        .packages
        .iter()
        .find(|package| package.manifest_path.as_std_path() == manifest_path)
        .ok_or_else(|| "the selected manifest does not describe a package".to_owned())?;

    let version = get_version(package_dir)?;
    if operation == "publish" && !version.public_release && !allow_non_public {
        return Err(
            "refusing to publish a non-public version; use --allow-non-public to override"
                .to_owned(),
        );
    }

    let nonce = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_err(|error| error.to_string())?
        .as_nanos();
    let stage_container = metadata
        .target_directory
        .as_std_path()
        .join("nbgv-stage")
        .join(format!("{}-{}-{nonce}", package.name, process::id()));
    let cleanup = Cleanup(stage_container.clone());
    let prepared_archive = prepare_package(
        &manifest_path,
        metadata.target_directory.as_std_path(),
        &package.name,
        &package.version.to_string(),
        allow_dirty,
        &safety_args,
    )?;
    let stage_dir = extract_prepared_package(
        &prepared_archive,
        &stage_container,
        &package.name,
        &package.version.to_string(),
    )?;
    for generated_file in ["Cargo.toml.orig", ".cargo_vcs_info.json"] {
        let path = stage_dir.join(generated_file);
        if let Err(error) = fs::remove_file(&path)
            && error.kind() != std::io::ErrorKind::NotFound
        {
            return Err(format!("cannot remove {}: {error}", path.display()));
        }
    }
    let staged_manifest = stage_dir.join("Cargo.toml");
    let mut document = fs::read_to_string(&staged_manifest)
        .map_err(|error| format!("cannot read {}: {error}", staged_manifest.display()))?
        .parse::<DocumentMut>()
        .map_err(|error| format!("invalid prepared Cargo.toml: {error}"))?;
    document["package"]["version"] = value(&version.sem_ver2);
    // The prepared archive has already applied the source manifest's include/exclude rules.
    // Include all staged files plus the hidden stamp, regardless of those original rules.
    document["package"]
        .as_table_mut()
        .ok_or_else(|| "prepared manifest has no package table".to_owned())?
        .remove("exclude");
    let mut include = toml_edit::Array::new();
    include.push("**");
    include.push(".nbgv-version.json");
    document["package"]["include"] = value(include);
    document["workspace"] = toml_edit::table();
    fs::write(&staged_manifest, document.to_string())
        .map_err(|error| format!("cannot write staged Cargo.toml: {error}"))?;
    let stamp_path = stage_dir.join(".nbgv-version.json");
    let stamp = serde_json::to_vec_pretty(&version)
        .map_err(|error| format!("cannot serialize version stamp: {error}"))?;
    fs::write(&stamp_path, stamp)
        .map_err(|error| format!("cannot write {}: {error}", stamp_path.display()))?;
    update_staged_lock(
        &stage_dir.join("Cargo.lock"),
        &package.name,
        &package.version.to_string(),
        &version.sem_ver2,
    )?;

    let has_target_dir = cargo_args.iter().any(|argument| {
        let argument = argument.to_string_lossy();
        argument == "--target-dir" || argument.starts_with("--target-dir=")
    });
    let mut command = Command::new(env::var_os("CARGO").unwrap_or_else(|| OsString::from("cargo")));
    command
        .arg(&operation)
        .arg("--manifest-path")
        .arg(&staged_manifest);
    // Source dirtiness was checked during preparation. The staged files themselves
    // are generated and can be untracked when the target directory is not ignored.
    if !allow_dirty {
        command.arg("--allow-dirty");
    }
    if !has_target_dir {
        command
            .arg("--target-dir")
            .arg(metadata.target_directory.as_std_path());
    }
    let status = command
        .args(cargo_args)
        .status()
        .map_err(|error| format!("failed to launch cargo {operation}: {error}"))?;
    if !status.success() {
        return Err(format!("cargo {operation} failed with {status}"));
    }
    drop(cleanup);
    println!("cargo-nbgv: {operation} used version {}", version.sem_ver2);
    Ok(())
}

// Cargo's Git status checks do not recognize Windows verbatim paths.
#[cfg(windows)]
fn cargo_path(path: PathBuf) -> PathBuf {
    use std::path::{Component, Prefix};

    let mut components = path.components();
    let root = match components.next() {
        Some(Component::Prefix(prefix)) => match prefix.kind() {
            Prefix::VerbatimDisk(drive) => PathBuf::from(format!("{}:\\", drive as char)),
            Prefix::VerbatimUNC(server, share) => {
                let mut root = PathBuf::from(r"\\");
                root.push(server);
                root.push(share);
                root
            }
            _ => return path,
        },
        _ => return path,
    };
    let mut result = root;
    for component in components {
        if !matches!(component, Component::RootDir) {
            result.push(component.as_os_str());
        }
    }
    result
}

#[cfg(not(windows))]
fn cargo_path(path: PathBuf) -> PathBuf {
    path
}

fn get_version(project_dir: &Path) -> Result<AdapterVersionInfo, String> {
    let context = GitContext::create_with_environment(project_dir, None, GitEngine::ReadOnly)
        .map_err(|error| format!("cannot open Git context: {error}"))?;
    let mut oracle = VersionOracle::new(&context, cloud::active().as_deref())
        .map_err(|error| format!("cannot calculate version: {error}"))?;
    if let Ok(value) = env::var("PublicRelease") {
        if value.trim().eq_ignore_ascii_case("true") {
            oracle.public_release = true;
        } else if value.trim().eq_ignore_ascii_case("false") {
            oracle.public_release = false;
        }
    }
    Ok(AdapterVersionInfo::from(&oracle))
}

fn prepare_package(
    manifest_path: &Path,
    target_dir: &Path,
    package_name: &str,
    package_version: &str,
    allow_dirty: bool,
    safety_args: &[OsString],
) -> Result<PathBuf, String> {
    let mut command = Command::new(env::var_os("CARGO").unwrap_or_else(|| OsString::from("cargo")));
    command
        .args(["package", "--no-verify", "--manifest-path"])
        .arg(manifest_path)
        .arg("--target-dir")
        .arg(target_dir)
        .current_dir(
            manifest_path
                .parent()
                .ok_or_else(|| "manifest has no parent directory".to_owned())?,
        );
    if allow_dirty {
        command.arg("--allow-dirty");
    }
    let output = command
        .args(safety_args)
        .output()
        .map_err(|error| format!("failed to prepare Cargo package: {error}"))?;
    if !output.status.success() {
        return Err(format!(
            "cargo package preparation failed: {}",
            String::from_utf8_lossy(&output.stderr).trim()
        ));
    }
    let archive = target_dir
        .join("package")
        .join(format!("{package_name}-{package_version}.crate"));
    if !archive.is_file() {
        return Err(format!(
            "Cargo did not create expected prepared package {}",
            archive.display()
        ));
    }
    Ok(archive)
}

fn update_staged_lock(
    lock_path: &Path,
    package_name: &str,
    old_version: &str,
    new_version: &str,
) -> Result<(), String> {
    if !lock_path.is_file() {
        return Ok(());
    }
    let mut document = fs::read_to_string(lock_path)
        .map_err(|error| format!("cannot read {}: {error}", lock_path.display()))?
        .parse::<DocumentMut>()
        .map_err(|error| format!("invalid prepared Cargo.lock: {error}"))?;
    let packages = document["package"]
        .as_array_of_tables_mut()
        .ok_or_else(|| "prepared Cargo.lock has no package array".to_owned())?;
    let mut matches = 0;
    for package in packages.iter_mut() {
        if package.get("name").and_then(|item| item.as_str()) == Some(package_name)
            && package.get("version").and_then(|item| item.as_str()) == Some(old_version)
            && package.get("source").is_none()
        {
            package["version"] = value(new_version);
            matches += 1;
        }
    }
    if matches != 1 {
        return Err(format!(
            "prepared Cargo.lock contains {matches} source package entries for {package_name} {old_version}; expected exactly one"
        ));
    }
    fs::write(lock_path, document.to_string())
        .map_err(|error| format!("cannot write {}: {error}", lock_path.display()))
}

fn extract_prepared_package(
    archive_path: &Path,
    destination: &Path,
    package_name: &str,
    package_version: &str,
) -> Result<PathBuf, String> {
    fs::create_dir_all(destination)
        .map_err(|error| format!("cannot create {}: {error}", destination.display()))?;
    let archive_file = File::open(archive_path)
        .map_err(|error| format!("cannot open {}: {error}", archive_path.display()))?;
    tar::Archive::new(GzDecoder::new(archive_file))
        .unpack(destination)
        .map_err(|error| format!("cannot extract {}: {error}", archive_path.display()))?;
    let package_dir = destination.join(format!("{package_name}-{package_version}"));
    if !package_dir.join("Cargo.toml").is_file() {
        return Err(format!(
            "prepared package has no manifest at {}",
            package_dir.display()
        ));
    }
    Ok(package_dir)
}

fn usage() -> String {
    "usage: cargo nbgv <package|publish> [--manifest-path PATH] [--allow-non-public] [-- CARGO_OPTIONS...]".to_owned()
}

struct Cleanup(PathBuf);
impl Drop for Cleanup {
    fn drop(&mut self) {
        let _ = fs::remove_dir_all(&self.0);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn staged_lock_updates_only_root_package() {
        let fixture = tempfile::Builder::new()
            .prefix("cargo-nbgv-lock-")
            .tempdir_in(env!("CARGO_MANIFEST_DIR"))
            .unwrap();
        let lock_path = fixture.path().join("Cargo.lock");
        fs::write(
            &lock_path,
            r#"version = 3

[[package]]
name = "selected"
version = "0.1.0"
dependencies = ["selected 0.1.0 (registry+https://example.invalid/index)"]

[[package]]
name = "selected"
version = "0.1.0"
source = "registry+https://example.invalid/index"
checksum = "0000000000000000000000000000000000000000000000000000000000000000"
"#,
        )
        .unwrap();
        update_staged_lock(&lock_path, "selected", "0.1.0", "2.3.4").unwrap();
        let lock = fs::read_to_string(lock_path).unwrap();
        assert!(lock.contains("name = \"selected\"\nversion = \"2.3.4\""));
        assert!(lock.contains("name = \"selected\"\nversion = \"0.1.0\"\nsource = \"registry+https://example.invalid/index\""));
        assert!(lock.contains(
            "dependencies = [\"selected 0.1.0 (registry+https://example.invalid/index)\"]"
        ));
    }

    #[test]
    fn rejects_unknown_operation() {
        assert!(
            run(vec![OsString::from("install")])
                .unwrap_err()
                .starts_with("usage:")
        );
    }
}
