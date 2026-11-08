# Rust and Cargo

## Revised integration plan

The repository now includes an in-process Rust version calculation library, `nerdbank-gitversioning`, and the native `nbgv` CLI in [`src/nerdbank-gitversioning-rs`][native-readme].
Automatic Cargo stamping should build on that implementation, not launch .NET or introduce another Rust engine.

The planned consolidation is:

1. Move the Cargo build-script helper into the native workspace as `nerdbank-gitversioning-build`, using its `GitContext` and `VersionOracle` directly to emit compile-time values and generated constants.
2. Move `cargo-nbgv` into that workspace and retain its normalized staging, workspace inheritance, dirty-source protection, and locked-package behavior.
3. Reserve the `nbgv` crate name for the existing native CLI and remove the separate `rust/` prototype once migrated.
4. Add the [adapter JSON contract](../adapter-json.md) to the native CLI before allowing Go or Java adapters to select it.
   Its current `json` output is not the versioned adapter format.
5. Compare the native engine with the .NET oracle in the shared Git fixtures, then reuse the native workspace's toolchain, cross-platform CI, crate version stamping, and Trusted Publishing release flow for the new packages.

The native library and CLI already exist; this consolidation and the build helper API are planned, not yet implemented.
The remaining sections describe the current CLI-backed prototype.

## Current prototype

> [!IMPORTANT]
> The separate `rust/` workspace is a source preview.
> Do not publish its build helper as `nbgv`: that name is used by the native CLI.
> Build the prototype from this repository until it is consolidated.

Both integrations launch the first-party `nbgv` executable and consume the
versioned [adapter JSON contract](../adapter-json.md). Install `nbgv` as a .NET
tool before using either integration.

## Acquire from source

Use a checkout as a build dependency:

```toml
[build-dependencies]
nbgv = { path = "../Nerdbank.GitVersioning/rust/nbgv" }
```

Install the Cargo subcommand from that checkout:

```console
cargo install --path Nerdbank.GitVersioning/rust/cargo-nbgv
```

These source-only examples do not establish the final registry package names or versions.
The prototype requires Rust 1.78; the consolidated integration will follow the native workspace's toolchain and minimum supported Rust policy.

## Stamp a build

Call the adapter from `build.rs`:

```rust
fn main() {
    nbgv::emit!();
}
```

The macro emits `NBGV_*` values for `env!` and writes a typed `nbgv.rs` module
to `OUT_DIR`. See the [crate README][crate-readme] for the full list, executable
resolution, and workspace behavior.

## Package with the calculated version

Cargo requires a literal version in `Cargo.toml`, while Nerdbank.GitVersioning
calculates the release version from Git. `cargo-nbgv` resolves that mismatch by
having Cargo prepare and normalize the package into an ignored staging
directory and changing only the staged manifest:

```console
cargo nbgv package --manifest-path path/to/Cargo.toml
cargo nbgv publish --manifest-path path/to/Cargo.toml -- --dry-run
```

The source manifest is not edited. Publishing a non-public version is rejected
unless `--allow-non-public` is explicitly supplied. Cargo's normal dirty-source
check is preserved; pass `--allow-dirty` after `--` only to opt out explicitly.
Cargo-prepared contents preserve normalized external README/license files,
workspace-inherited package metadata and dependencies, and generated lock
files. Forwarded `--locked`, `--frozen`, and `--offline` constraints also apply
during preparation. The staged lockfile is updated only for the selected root
package version, leaving resolved dependency versions unchanged.

## Migrate an existing crate

1. Keep an ordinary placeholder `package.version` (which may be inherited from
   `[workspace.package]`); stop scripts from rewriting the checked-in manifest.
2. Remove other build scripts that derive a version from tags or branch names.
3. Add `nbgv::emit!()` and replace old compile-time variables with `NBGV_*`.
4. Replace release-time manifest edits with `cargo nbgv package` or `publish`.
5. Compare a dry-run package with the previous crate before the first release.

The initial adapter packages one explicit manifest and materializes its
workspace inheritance through Cargo. Coordinated multi-crate publishing is not
silently rewritten.

[crate-readme]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/main/rust/nbgv/README.md
[native-readme]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/main/src/nerdbank-gitversioning-rs/README.md
