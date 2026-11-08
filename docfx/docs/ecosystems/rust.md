# Rust and Cargo

Nerdbank.GitVersioning provides a native Rust version calculation library, `nbgv` CLI, Cargo build helper, and `cargo-nbgv` packaging command.
Cargo integrations calculate versions in-process using that library; they do not require .NET or an external `nbgv` executable.
All four crates are distributed through crates.io.

The `nbgv` crate provides the CLI.
Use `nerdbank-gitversioning-build`, not `nbgv`, as a build dependency.

## Install

From your crate directory, add the build helper:

```console
cargo add --build nerdbank-gitversioning-build
```

Install the packaging subcommand:

```console
cargo install --locked cargo-nbgv
```

These commands acquire packages from crates.io; your project does not need a checkout of Nerdbank.GitVersioning.
Commit the resulting build dependency in `Cargo.toml` and follow your project's lockfile policy.
Add a [version.json](../versionJson.md) to your repository to define its base version and release settings.
See the [native workspace README][native-readme] for library and CLI usage.

## Stamp a build

Call `nerdbank_gitversioning_build::emit!()` from `build.rs`.
The helper emits `NBGV_*` compile-time environment variables and writes a typed `nbgv.rs` module under `OUT_DIR`.
Use `env!("NBGV_SEMVER2")` in application code, or include the generated module to read typed version height, release state, and optional commit information.
The version is calculated from the crate directory using the same version files, Git engine policy, and cloud provider logic as the native CLI.
In a live checkout, the helper recalculates on every Cargo build, including when Git is disabled.
This detects commits, newly created or edited version files, dirty-tree changes, and release/environment overrides without requiring staging or touching `build.rs`.
Cargo has no selective watch for creating an absent file, so correctness takes priority over incremental-build performance: the build script reruns and Cargo may recompile its package even when the version is unchanged.
The helper does not recursively watch build outputs, and it leaves the generated module untouched when its contents are unchanged.
Published archives remain incremental and rebuild only when their precomputed stamp changes.

## Package with the calculated version

Cargo reads its package version before running build scripts, so a build script cannot change the publication identity.
`cargo-nbgv` lets Cargo prepare and normalize a package, then changes only the staged manifest and the root package version in its staged lockfile:

```console
cargo nbgv package --manifest-path path/to/Cargo.toml
cargo nbgv publish --manifest-path path/to/Cargo.toml -- --dry-run
```

The source manifest is not edited.
Publishing a non-public version is rejected unless `--allow-non-public` is explicitly supplied.
Cargo's dirty-source check is preserved; pass `--allow-dirty` after `--` only to opt out explicitly.
Normalized contents retain external README/license files, workspace-inherited package metadata and dependencies, and generated lockfiles.
Forwarded `--locked`, `--frozen`, and `--offline` constraints also apply during preparation.

The staged package contains `.nbgv-version.json` with the complete calculated stamp.
The build helper reads that package-local stamp when rebuilding a published archive, including when it is vendored inside another Git repository, preserving its original version and commit identity.
Malformed or unsupported stamps fail the build instead of silently assigning a public version.
Explicit disabled-Git behavior remains distinct from rebuilding a stamped archive.

## Migrate an existing crate

1. Keep an ordinary `package.version`, which may inherit from `[workspace.package]`, rather than rewriting the source manifest during builds.
2. Add `nerdbank-gitversioning-build` and replace tag-based build scripts with `nerdbank_gitversioning_build::emit!()`.
3. Use `NBGV_*` variables or the generated constants in application code.
4. Replace release-time source edits with `cargo nbgv package` or `publish`.
5. Verify a dry-run package before the first release.

`cargo-nbgv` packages one selected manifest; it does not silently rewrite sibling dependency versions for coordinated multi-crate publishing.

## Shared tooling

The native CLI supports the same [adapter JSON contract](../adapter-json.md) as the .NET CLI, allowing Go and Java adapters to use it without .NET.
Install the native CLI with `cargo install --locked nbgv` when you need those commands.
The CLI is optional for the Cargo integrations.

[native-readme]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/main/src/nerdbank-gitversioning-rs/README.md
