# Nerdbank.GitVersioning for Rust

This workspace contains:

- `nerdbank-gitversioning`, a Rust library for calculating versions from
  `version.json`, `version.txt`, and Git history.
- `nbgv`, a command-line tool built on the library.
- `nerdbank-gitversioning-build`, an in-process Cargo build-script helper.
- `cargo-nbgv`, a packaging command that stamps a staged manifest without editing the source.

It supports Windows, Linux, and macOS and builds with current stable Rust
(Rust 1.95 or later).

The Rust implementation targets compatibility with the non-MSBuild behavior of
the .NET implementation in this repository. It uses libgit2 in-process for
normal repository operations. If Git commit signing is enabled,
`nbgv prepare-release` may invoke the `git` executable to create signed commits.

## Library usage

Add the library from this checkout:

```toml
[dependencies]
nerdbank-gitversioning = { path = "src/nerdbank-gitversioning-rs" }
```

Calculate a version from the repository containing the current directory:

```rust
use nerdbank_gitversioning::{GitContext, GitEngine, VersionOracle};

fn main() -> Result<(), nerdbank_gitversioning::Error> {
    let context = GitContext::create(".", None, GitEngine::ReadOnly)?;
    let oracle = VersionOracle::new(&context, None)?;
    println!("{}", oracle.sem_ver2());
    Ok(())
}
```

## CLI usage

Install the native CLI from this checkout:

```console
cargo install --path src/nerdbank-gitversioning-rs/nbgv
nbgv get-version
```

Cargo installs `nbgv.exe` in `%USERPROFILE%\.cargo\bin` on Windows and
`nbgv` in `$HOME/.cargo/bin` on Linux and macOS. Add that directory to
`PATH`, if necessary.

The Rust CLI includes exactly the six commands that do not require MSBuild:

- `get-version` calculates and prints version information.
- `set-version` updates the applicable version file.
- `tag` creates a version tag.
- `get-commits` finds commits that produced a version.
- `cloud` sets cloud-build numbers and variables.
- `prepare-release` creates a release branch and updates versions.

Run `nbgv --help` or `nbgv <command> --help` for all options.
`nbgv get-version --format adapter` emits the versioned adapter JSON contract
(schema version 1), including nullable Git information. The existing text and
JSON formats remain available unchanged.

## Cargo integrations

Use `nerdbank_gitversioning_build::emit!()` in `build.rs` to emit compile-time `NBGV_*` variables and a typed module in `OUT_DIR`.
The helper follows version files and Git metadata, including worktrees, and honors native cloud-build and release overrides.
It does not launch `nbgv` or require .NET.

`cargo nbgv package` and `cargo nbgv publish` let Cargo normalize the package before stamping the staged manifest and lockfile.
They preserve Cargo's dirty-source and lockfile checks and store the full calculated stamp in `.nbgv-version.json` for builds without Git.
See the [Cargo integration guide](../../docfx/docs/ecosystems/rust.md) for acquisition, migration, and publication details.

All four crates share the native workspace version, CI, and release publishing.
The build helper and packaging command remain source previews until their first published release.

## Preparing a release

`ReleaseManager` validates that the repository has an attached branch, a clean
working tree, a configured Git identity, and an applicable version file. It can
simulate a release, return text or JSON results, create and update the release
and development branches, and optionally merge the release branch. Version
files that inherit settings are edited without flattening their inheritance.

All ordinary repository work is performed in-process with libgit2. The `git`
executable is invoked only when `commit.gpgSign` is enabled, because libgit2
cannot create Git-compatible signed commits or merge commits.

## Excluded .NET integration

The Rust packages do not provide MSBuild props, targets, tasks, generated
assembly metadata, or package installation. Consequently, the .NET tool's
`install` and `path-filters` commands are not included. Use the .NET `nbgv`
tool for those commands.

## Development

Use current stable Rust:

```console
cargo fmt --all --check
cargo clippy --workspace --all-targets --all-features -- -D warnings
cargo test --workspace
RUSTDOCFLAGS="-D warnings" cargo doc --workspace --no-deps
```

In PowerShell, set the documentation flags with
`$env:RUSTDOCFLAGS = "-D warnings"` before running `cargo doc`.

## Benchmarks

Clone `xunit`, `Cuemon`, `SuperSocket`, and `Nerdbank.GitVersioning` under
`%USERPROFILE%\Source\Repos` on Windows or `~/git` elsewhere, then run:

```console
cargo bench --bench get_version
```

Set `NBGV_BENCH_REPOSITORY_ROOT` to use another directory. Missing repositories
produce an error. Criterion retains machine-readable results under
`target/criterion`.
