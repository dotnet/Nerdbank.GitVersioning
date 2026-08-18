use std::env;
use std::hint::black_box;
use std::path::{Path, PathBuf};
use std::time::Duration;

use criterion::{BenchmarkId, Criterion, criterion_group, criterion_main};
use nerdbank_gitversioning::{GitContext, GitEngine, VersionOracle};

const REPOSITORIES: [&str; 4] = ["xunit", "Cuemon", "SuperSocket", "Nerdbank.GitVersioning"];
const REPOSITORY_ROOT_ENV: &str = "NBGV_BENCH_REPOSITORY_ROOT";

fn repository_root() -> PathBuf {
    if let Some(root) = env::var_os(REPOSITORY_ROOT_ENV) {
        return PathBuf::from(root);
    }

    let home_variable = if cfg!(windows) { "USERPROFILE" } else { "HOME" };
    let home = env::var_os(home_variable).unwrap_or_else(|| {
        panic!(
            "{home_variable} is not set; set {REPOSITORY_ROOT_ENV} to the directory containing the benchmark repositories"
        )
    });

    if cfg!(windows) {
        PathBuf::from(home).join("Source").join("Repos")
    } else {
        PathBuf::from(home).join("git")
    }
}

fn require_repository(root: &Path, repository: &str) -> PathBuf {
    let path = root.join(repository);
    assert!(
        path.is_dir(),
        "benchmark repository '{}' was not found; clone {repository} there or set {REPOSITORY_ROOT_ENV}",
        path.display()
    );
    path
}

fn get_version_benchmarks(criterion: &mut Criterion) {
    let root = repository_root();
    let repositories: Vec<_> = REPOSITORIES
        .into_iter()
        .map(|name| (name, require_repository(&root, name)))
        .collect();
    let mut group = criterion.benchmark_group("get_version_read_write");

    for (name, path) in repositories {
        group.bench_with_input(BenchmarkId::from_parameter(name), &path, |bencher, path| {
            bencher.iter(|| {
                let context = GitContext::create(path, None, GitEngine::ReadWrite)
                    .expect("failed to create read-write Git context");
                let oracle =
                    VersionOracle::new(&context, None).expect("failed to calculate version");
                black_box(oracle.version);
                drop(oracle);
                drop(context);
            });
        });
    }

    group.finish();
}

criterion_group! {
    name = benches;
    config = Criterion::default()
        .sample_size(30)
        .warm_up_time(Duration::from_secs(3))
        .measurement_time(Duration::from_secs(10));
    targets = get_version_benchmarks
}
criterion_main!(benches);
