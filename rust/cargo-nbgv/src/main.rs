use cargo_metadata::MetadataCommand;
use flate2::read::GzDecoder;
use serde_json::Value;
use std::env;
use std::ffi::OsString;
use std::fs::{self, File};
use std::path::{Path, PathBuf};
use std::process::{self, Command};
use std::time::{SystemTime, UNIX_EPOCH};
use toml_edit::{value, DocumentMut};

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
    let mut executable = None;
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
        } else if args[index] == "--nbgv-executable" {
            index += 1;
            executable = Some(
                args.get(index)
                    .cloned()
                    .ok_or_else(|| "--nbgv-executable requires a value".to_owned())?,
            );
        } else {
            return Err(format!(
                "unknown cargo-nbgv option {:?}; Cargo options belong after --",
                args[index]
            ));
        }
        index += 1;
    }

    let manifest_path = fs::canonicalize(&manifest_path)
        .map_err(|error| format!("cannot find {}: {error}", manifest_path.display()))?;
    let package_dir = manifest_path
        .parent()
        .ok_or_else(|| "manifest has no parent directory".to_owned())?;
    let allow_dirty = cargo_args
        .iter()
        .any(|argument| argument == "--allow-dirty");
    let preparation_safety_args: Vec<_> = cargo_args
        .iter()
        .filter(|argument| {
            *argument == "--locked" || *argument == "--frozen" || *argument == "--offline"
        })
        .cloned()
        .collect();
    let mut metadata_command = MetadataCommand::new();
    metadata_command.manifest_path(&manifest_path).no_deps();
    metadata_command.other_options(
        preparation_safety_args
            .iter()
            .map(|argument| argument.to_string_lossy().into_owned())
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

    let version = get_version(executable, package_dir)?;
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
        .join(format!("{}-{nonce}", package.name));
    let cleanup = Cleanup(stage_container.clone());
    let prepared_archive = prepare_package(
        &manifest_path,
        metadata.target_directory.as_std_path(),
        &package.name,
        &package.version.to_string(),
        allow_dirty,
        &preparation_safety_args,
    )?;
    let stage_dir = extract_prepared_package(
        &prepared_archive,
        &stage_container,
        &package.name,
        &package.version.to_string(),
    )?;
    for generated_file in ["Cargo.toml.orig", ".cargo_vcs_info.json"] {
        let path = stage_dir.join(generated_file);
        if let Err(error) = fs::remove_file(&path) {
            if error.kind() != std::io::ErrorKind::NotFound {
                return Err(format!("cannot remove {}: {error}", path.display()));
            }
        }
    }

    let staged_manifest = stage_dir.join("Cargo.toml");
    let mut document = fs::read_to_string(&staged_manifest)
        .map_err(|error| format!("cannot read {}: {error}", staged_manifest.display()))?
        .parse::<DocumentMut>()
        .map_err(|error| format!("invalid prepared Cargo.toml: {error}"))?;
    document["package"]["version"] = value(&version.sem_ver2);
    document["workspace"] = toml_edit::table();
    fs::write(&staged_manifest, document.to_string())
        .map_err(|error| format!("cannot write staged Cargo.toml: {error}"))?;
    update_staged_lock(
        &stage_dir.join("Cargo.lock"),
        &package.name,
        &package.version.to_string(),
        &version.sem_ver2,
    )?;

    if cargo_args.iter().any(|argument| {
        let argument = argument.to_string_lossy();
        argument == "--manifest-path" || argument.starts_with("--manifest-path=")
    }) {
        return Err(
            "--manifest-path is managed by cargo-nbgv and cannot be passed after --".into(),
        );
    }
    let has_target_dir = cargo_args.iter().any(|argument| {
        let argument = argument.to_string_lossy();
        argument == "--target-dir" || argument.starts_with("--target-dir=")
    });
    let mut command = Command::new(env::var_os("CARGO").unwrap_or_else(|| OsString::from("cargo")));
    command
        .arg(&operation)
        .arg("--manifest-path")
        .arg(&staged_manifest);
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

struct CalculatedVersion {
    sem_ver2: String,
    public_release: bool,
}

fn get_version(
    explicit_executable: Option<OsString>,
    project_dir: &Path,
) -> Result<CalculatedVersion, String> {
    let executable = explicit_executable
        .or_else(|| env::var_os("NBGV_EXECUTABLE"))
        .unwrap_or_else(|| OsString::from("nbgv"));
    let output = Command::new(&executable)
        .args(["get-version", "--format", "adapter"])
        .current_dir(project_dir)
        .output()
        .map_err(|error| format!("cannot launch {:?}: {error}", executable))?;
    if !output.status.success() {
        return Err(format!(
            "{:?} failed with {}: {}",
            executable,
            output.status,
            String::from_utf8_lossy(&output.stderr).trim()
        ));
    }

    let document: Value = serde_json::from_slice(&output.stdout)
        .map_err(|error| format!("nbgv returned invalid adapter JSON: {error}"))?;
    if document.get("schemaVersion").and_then(Value::as_u64) != Some(1) {
        return Err("nbgv returned an unsupported adapter schemaVersion".to_owned());
    }

    let sem_ver2 = document
        .get("semVer2")
        .and_then(Value::as_str)
        .ok_or_else(|| "nbgv adapter JSON has no string semVer2".to_owned())?;
    semver::Version::parse(sem_ver2)
        .map_err(|error| format!("nbgv returned an invalid semVer2: {error}"))?;
    let public_release = document
        .get("publicRelease")
        .and_then(Value::as_bool)
        .ok_or_else(|| "nbgv adapter JSON has no boolean publicRelease".to_owned())?;

    Ok(CalculatedVersion {
        sem_ver2: sem_ver2.to_owned(),
        public_release,
    })
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
        .arg(target_dir);
    if allow_dirty {
        command.arg("--allow-dirty");
    }
    command.args(safety_args);
    let output = command
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
        let is_root = package.get("name").and_then(|item| item.as_str()) == Some(package_name)
            && package.get("version").and_then(|item| item.as_str()) == Some(old_version)
            && package.get("source").is_none();
        if is_root {
            package["version"] = value(new_version);
            matches += 1;
        }
    }
    if matches != 1 {
        return Err(format!(
            "prepared Cargo.lock contains {matches} source package entries for \
             {package_name} {old_version}; expected exactly one"
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
    let mut archive = tar::Archive::new(GzDecoder::new(archive_file));
    archive
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
    "usage: cargo nbgv <package|publish> [--manifest-path PATH] \
     [--nbgv-executable PATH] [--allow-non-public] [-- CARGO_OPTIONS...]"
        .to_owned()
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
    fn rejects_unknown_operation() {
        let error = run(vec![OsString::from("install")]).unwrap_err();
        assert!(error.starts_with("usage:"));
    }

    #[test]
    fn package_uses_staged_manifest_without_modifying_source() {
        let root = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("..")
            .join("target")
            .join("cargo-nbgv-tests")
            .join("staged-package");
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(root.join("src")).unwrap();
        let manifest = root.join("Cargo.toml");
        let original_manifest = r#"[package]
name = "nbgv-staging-test"
version = "0.0.1"
edition = "2021"
include = ["src/**"]

[workspace]
"#;
        fs::write(&manifest, original_manifest).unwrap();
        fs::write(root.join("src/lib.rs"), "pub const VALUE: u8 = 1;").unwrap();

        let helper_source = root.join("fake_nbgv.rs");
        let helper_executable = root.join(if cfg!(windows) {
            "fake_nbgv.exe"
        } else {
            "fake_nbgv"
        });
        fs::write(
            &helper_source,
            r##"fn main() {
    print!("{}", r#"{"schemaVersion":1,"semVer2":"2.3.4","version":"2.3.4","versionHeight":0,"gitCommitId":null,"gitCommitIdShort":null,"gitCommitDate":null,"publicRelease":true}"#);
}"##,
        )
        .unwrap();
        assert!(Command::new("rustc")
            .arg(&helper_source)
            .arg("-o")
            .arg(&helper_executable)
            .status()
            .unwrap()
            .success());

        run(vec![
            OsString::from("package"),
            OsString::from("--manifest-path"),
            manifest.clone().into_os_string(),
            OsString::from("--nbgv-executable"),
            helper_executable.into_os_string(),
            OsString::from("--"),
            OsString::from("--allow-dirty"),
            OsString::from("--no-verify"),
        ])
        .unwrap();

        assert_eq!(fs::read_to_string(&manifest).unwrap(), original_manifest);
        assert!(root
            .join("target/package/nbgv-staging-test-2.3.4.crate")
            .is_file());
    }

    #[test]
    fn package_materializes_workspace_fields_and_external_files() {
        let root = test_root("workspace-inheritance");
        let member = root.join("member");
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(member.join("src")).unwrap();
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
        let manifest = member.join("Cargo.toml");
        fs::write(
            &manifest,
            r#"[package]
name = "nbgv-workspace-staging-test"
version.workspace = true
edition.workspace = true
authors.workspace = true
license-file.workspace = true
readme.workspace = true
repository.workspace = true
"#,
        )
        .unwrap();
        fs::write(member.join("src/lib.rs"), "pub const VALUE: u8 = 1;").unwrap();
        let helper_executable = compile_fake_nbgv(&root);
        assert!(
            Command::new(env::var_os("CARGO").unwrap_or_else(|| OsString::from("cargo")))
                .arg("generate-lockfile")
                .arg("--manifest-path")
                .arg(&manifest)
                .status()
                .unwrap()
                .success()
        );

        run(vec![
            OsString::from("package"),
            OsString::from("--manifest-path"),
            manifest.into_os_string(),
            OsString::from("--nbgv-executable"),
            helper_executable.into_os_string(),
            OsString::from("--"),
            OsString::from("--allow-dirty"),
            OsString::from("--locked"),
            OsString::from("--no-verify"),
        ])
        .unwrap();

        let archive = root.join("target/package/nbgv-workspace-staging-test-2.3.4.crate");
        let entries = archive_entries(&archive);
        assert!(entries.iter().any(|path| path.ends_with("/README.md")));
        assert!(entries.iter().any(|path| path.ends_with("/LICENSE")));
        assert!(entries.iter().any(|path| path.ends_with("/Cargo.lock")));
        let manifest = archive_file(&archive, "Cargo.toml");
        let lock = archive_file(&archive, "Cargo.lock");
        assert!(manifest.contains("edition = \"2021\""));
        assert!(manifest.contains("authors = [\"Workspace Author\"]"));
        assert!(manifest.contains("repository = \"https://example.invalid/repository\""));
        assert!(!manifest.contains(".workspace = true"));
        assert!(lock.contains("name = \"nbgv-workspace-staging-test\"\nversion = \"2.3.4\""));
    }

    #[test]
    fn package_preparation_honors_dirty_source_protection() {
        let root = test_root("dirty-source");
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(root.join("src")).unwrap();
        let manifest = root.join("Cargo.toml");
        fs::write(
            &manifest,
            r#"[package]
name = "nbgv-dirty-test"
version = "0.1.0"
edition = "2021"

[workspace]
"#,
        )
        .unwrap();
        fs::write(root.join("src/lib.rs"), "pub const VALUE: u8 = 1;").unwrap();
        run_git(&root, &["init", "--quiet"]);
        run_git(&root, &["add", "Cargo.toml", "src/lib.rs"]);
        run_git(
            &root,
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
        fs::write(root.join("src/lib.rs"), "pub const VALUE: u8 = 2;").unwrap();

        let target = root.join("target");
        let error = prepare_package(&manifest, &target, "nbgv-dirty-test", "0.1.0", false, &[])
            .unwrap_err();
        assert!(error.contains("contain changes"), "{error}");
        assert!(
            prepare_package(&manifest, &target, "nbgv-dirty-test", "0.1.0", true, &[])
                .unwrap()
                .is_file()
        );
    }

    #[test]
    fn staged_lock_updates_only_the_root_package() {
        let root = test_root("staged-lock");
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).unwrap();
        let lock_path = root.join("Cargo.lock");
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
        assert!(lock.contains(
            "name = \"selected\"\nversion = \"0.1.0\"\nsource = \"registry+https://example.invalid/index\""
        ));
        assert!(lock.contains(
            "dependencies = [\"selected 0.1.0 (registry+https://example.invalid/index)\"]"
        ));
    }

    #[test]
    fn package_uses_real_nbgv_and_shared_dotnet_oracle() {
        let Some(manifest_path) = env::var_os("NBGV_PARITY_MANIFEST") else {
            eprintln!("NBGV_PARITY_MANIFEST is not set; shared parity fixture was not requested");
            return;
        };
        let fixture: serde_json::Value =
            serde_json::from_slice(&fs::read(manifest_path).unwrap()).unwrap();
        let executable = fixture["executable"].as_str().unwrap();
        let scenario = &fixture["scenarios"][0];
        let fixture_repository = PathBuf::from(scenario["directory"].as_str().unwrap());
        let oracle: serde_json::Value =
            serde_json::from_slice(&fs::read(scenario["oracle"].as_str().unwrap()).unwrap())
                .unwrap();
        let sem_ver2 = oracle["semVer2"].as_str().unwrap();
        let package_root = fixture_repository.join("cargo-parity");
        let _ = fs::remove_dir_all(&package_root);
        fs::create_dir_all(package_root.join("src")).unwrap();
        let manifest = package_root.join("Cargo.toml");
        let original_manifest = r#"[package]
name = "cargo-parity"
version = "0.0.1"
edition = "2021"

[workspace]
"#;
        fs::write(&manifest, original_manifest).unwrap();
        fs::write(
            package_root.join("src/lib.rs"),
            "pub const VALUE: u8 = 1;\n",
        )
        .unwrap();

        let previous_git_engine = env::var_os("NBGV_GitEngine");
        env::set_var("NBGV_GitEngine", scenario["gitEngine"].as_str().unwrap());
        run(vec![
            OsString::from("package"),
            OsString::from("--manifest-path"),
            manifest.clone().into_os_string(),
            OsString::from("--nbgv-executable"),
            OsString::from(executable),
            OsString::from("--"),
            OsString::from("--allow-dirty"),
            OsString::from("--no-verify"),
        ])
        .unwrap();
        match previous_git_engine {
            Some(value) => env::set_var("NBGV_GitEngine", value),
            None => env::remove_var("NBGV_GitEngine"),
        }

        assert_eq!(fs::read_to_string(&manifest).unwrap(), original_manifest);
        assert!(package_root
            .join(format!("target/package/cargo-parity-{sem_ver2}.crate"))
            .is_file());
    }

    fn test_root(name: &str) -> PathBuf {
        PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("..")
            .join("target")
            .join("cargo-nbgv-tests")
            .join(name)
    }

    fn compile_fake_nbgv(root: &Path) -> PathBuf {
        let helper_source = root.join("fake_nbgv.rs");
        let helper_executable = root.join(if cfg!(windows) {
            "fake_nbgv.exe"
        } else {
            "fake_nbgv"
        });
        fs::write(
            &helper_source,
            r##"fn main() {
    print!("{}", r#"{"schemaVersion":1,"semVer2":"2.3.4","version":"2.3.4","versionHeight":0,"gitCommitId":null,"gitCommitIdShort":"","gitCommitDate":null,"publicRelease":true}"#);
}"##,
        )
        .unwrap();
        assert!(Command::new("rustc")
            .arg(&helper_source)
            .arg("-o")
            .arg(&helper_executable)
            .status()
            .unwrap()
            .success());
        helper_executable
    }

    fn archive_entries(path: &Path) -> Vec<String> {
        let file = File::open(path).unwrap();
        tar::Archive::new(GzDecoder::new(file))
            .entries()
            .unwrap()
            .map(|entry| {
                entry
                    .unwrap()
                    .path()
                    .unwrap()
                    .to_string_lossy()
                    .into_owned()
            })
            .collect()
    }

    fn archive_file(path: &Path, file_name: &str) -> String {
        use std::io::Read;

        let file = File::open(path).unwrap();
        let mut archive = tar::Archive::new(GzDecoder::new(file));
        for entry in archive.entries().unwrap() {
            let mut entry = entry.unwrap();
            if entry.path().unwrap().ends_with(file_name) {
                let mut contents = String::new();
                entry.read_to_string(&mut contents).unwrap();
                return contents;
            }
        }
        panic!("{file_name} not found in {}", path.display());
    }

    fn run_git(directory: &Path, arguments: &[&str]) {
        assert!(Command::new("git")
            .args(arguments)
            .current_dir(directory)
            .status()
            .unwrap()
            .success());
    }
}
