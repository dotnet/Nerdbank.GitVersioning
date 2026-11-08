# Rust and Cargo

The Rust workspace at `src/nerdbank-gitversioning-rs` provides the native version calculation library, `nbgv` CLI, Cargo build helper, and `cargo-nbgv` packaging command.
Cargo integrations calculate versions in-process using that library; they do not require .NET or an external `nbgv` executable.
All crates use the native workspace's shared version and Rust toolchain policy.

> [!IMPORTANT]
> The build helper and packaging command are source-preview integrations.
> The source examples below do not imply that those packages are already available on crates.io.
> The `nbgv` crate name belongs to the native CLI, not the build helper.

## Acquire from source

Add `nerdbank-gitversioning-build` from the checkout as a build dependency:

```toml
[build-dependencies]
nerdbank-gitversioning-build = { path = "../Nerdbank.GitVersioning/src/nerdbank-gitversioning-rs/build" }
```

Install the Cargo subcommand from that checkout:

```console
cargo install --path Nerdbank.GitVersioning/src/nerdbank-gitversioning-rs/cargo-nbgv
```

The helper uses `GitContext` and `VersionOracle` from the existing `nerdbank-gitversioning` library.
See the [native workspace README][native-readme] for library and CLI usage.

## Stamp a build

Call `nerdbank_gitversioning_build::emit!()` from `build.rs`.
The helper emits `NBGV_*` compile-time environment variables and writes a typed `nbgv.rs` module under `OUT_DIR`.
Use `env!("NBGV_SEMVER2")` in application code, or include the generated module to read typed version height, release state, and optional commit information.
The version is calculated from the crate directory using the same version files, Git engine policy, and cloud provider logic as the native CLI.
Build inputs include version files, Git metadata, and release/environment overrides so commits and configuration changes invalidate the stamp.

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
The build helper reads that stamp when rebuilding a published archive without a Git repository, preserving its version and commit identity.
Malformed or unsupported stamps fail the build instead of silently assigning a public version.
Explicit disabled-Git behavior remains distinct from rebuilding a stamped archive.

## Migrate an existing crate

1. Keep an ordinary `package.version`, which may inherit from `[workspace.package]`, rather than rewriting the source manifest during builds.
2. Add `nerdbank-gitversioning-build` and replace tag-based build scripts with `nerdbank_gitversioning_build::emit!()`.
3. Use `NBGV_*` variables or the generated constants in application code.
4. Replace release-time source edits with `cargo nbgv package` or `publish`.
5. Verify a dry-run package before the first release.

The obsolete separate `rust/` workspace is no longer used.
If you previously used its `nbgv` build dependency, switch to `nerdbank-gitversioning-build` and remove the external-executable configuration.
`cargo-nbgv` packages one selected manifest; it does not silently rewrite sibling dependency versions for coordinated multi-crate publishing.

## Shared tooling

The native CLI supports the same [adapter JSON contract](../adapter-json.md) as the .NET CLI, allowing Go and Java adapters to use it without .NET.
The shared real-Git fixtures compare native and .NET results before testing ecosystem consumers.
CI and release publishing reuse the native workspace's version stamping and Trusted Publishing configuration, rather than maintaining a separate Rust release pipeline.

[native-readme]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/main/src/nerdbank-gitversioning-rs/README.md
