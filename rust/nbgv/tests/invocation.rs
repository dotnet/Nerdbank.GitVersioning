use nbgv::Config;
use serde::Deserialize;
use std::env;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command;

#[test]
fn directly_launches_configured_executable_in_project_directory() {
    let test_root = PathBuf::from(env!("CARGO_TARGET_TMPDIR")).join("direct-invocation");
    let _ = fs::remove_dir_all(&test_root);
    fs::create_dir_all(&test_root).unwrap();

    let helper_source = test_root.join("fake_nbgv.rs");
    let helper_executable = test_root.join(if cfg!(windows) {
        "fake_nbgv.exe"
    } else {
        "fake_nbgv"
    });
    fs::write(
        &helper_source,
        r##"
use std::{env, fs};
fn main() {
    let args: Vec<_> = env::args().skip(1).collect();
    assert_eq!(args, ["get-version", "--format", "adapter"]);
    fs::write("invoked", "yes").unwrap();
    print!("{}", r#"{"schemaVersion":1,"semVer2":"2.3.4","version":"2.3.4","versionHeight":0,"gitCommitId":null,"gitCommitIdShort":null,"gitCommitDate":null,"publicRelease":true}"#);
}
"##,
    )
    .unwrap();
    let status = Command::new("rustc")
        .args(["--edition=2021"])
        .arg(&helper_source)
        .arg("-o")
        .arg(&helper_executable)
        .status()
        .unwrap();
    assert!(status.success());

    let version = Config::new()
        .executable(&helper_executable)
        .project_dir(&test_root)
        .get_version()
        .unwrap();
    assert_eq!(version.sem_ver2, "2.3.4");
    assert!(version.public_release);
    assert_eq!(
        fs::read_to_string(test_root.join("invoked")).unwrap(),
        "yes"
    );
}

#[derive(Deserialize)]
struct ParityManifest {
    executable: PathBuf,
    scenarios: Vec<ParityScenario>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ParityScenario {
    name: String,
    directory: PathBuf,
    oracle: PathBuf,
    git_engine: String,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct OracleVersion {
    schema_version: u64,
    sem_ver2: String,
    version: String,
    version_height: i64,
    git_commit_id: Option<String>,
    git_commit_id_short: Option<String>,
    git_commit_date: Option<String>,
    public_release: bool,
}

#[test]
fn shared_git_fixture_matches_dotnet_oracle_byte_for_byte() {
    let Some(manifest_path) = env::var_os("NBGV_PARITY_MANIFEST") else {
        eprintln!("NBGV_PARITY_MANIFEST is not set; shared parity fixture was not requested");
        return;
    };
    let manifest: ParityManifest =
        serde_json::from_slice(&fs::read(manifest_path).unwrap()).unwrap();
    let previous_git_engine = env::var_os("NBGV_GitEngine");

    for scenario in manifest.scenarios {
        assert!(
            find_git_marker(&scenario.directory).is_some(),
            "{} is not backed by a real Git repository",
            scenario.name
        );
        env::set_var("NBGV_GitEngine", &scenario.git_engine);

        let oracle: OracleVersion =
            serde_json::from_slice(&fs::read(&scenario.oracle).unwrap()).unwrap();
        assert_eq!(oracle.schema_version, 1, "{}", scenario.name);
        let actual = Config::new()
            .executable(&manifest.executable)
            .project_dir(&scenario.directory)
            .get_version()
            .unwrap_or_else(|error| panic!("{}: {error}", scenario.name));

        assert_eq!(
            canonical_bytes(
                &actual.sem_ver2,
                &actual.version,
                actual.version_height,
                actual.git_commit_id.as_deref(),
                actual.git_commit_id_short.as_deref(),
                actual.git_commit_date.as_deref(),
                actual.public_release,
            ),
            canonical_bytes(
                &oracle.sem_ver2,
                &oracle.version,
                oracle.version_height,
                oracle.git_commit_id.as_deref(),
                oracle.git_commit_id_short.as_deref(),
                oracle.git_commit_date.as_deref(),
                oracle.public_release,
            ),
            "{}",
            scenario.name
        );
    }

    match previous_git_engine {
        Some(value) => env::set_var("NBGV_GitEngine", value),
        None => env::remove_var("NBGV_GitEngine"),
    }
}

fn canonical_bytes(
    sem_ver2: &str,
    version: &str,
    version_height: i64,
    git_commit_id: Option<&str>,
    git_commit_id_short: Option<&str>,
    git_commit_date: Option<&str>,
    public_release: bool,
) -> Vec<u8> {
    format!(
        "{sem_ver2}\0{version}\0{version_height}\0{}\0{}\0{}\0{public_release}",
        git_commit_id.unwrap_or("<null>"),
        git_commit_id_short.unwrap_or("<null>"),
        git_commit_date.unwrap_or("<null>")
    )
    .into_bytes()
}

fn find_git_marker(directory: &Path) -> Option<PathBuf> {
    directory
        .ancestors()
        .map(|ancestor| ancestor.join(".git"))
        .find(|marker| marker.exists())
}
