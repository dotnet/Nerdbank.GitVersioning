# Rust and Cargo

> [!IMPORTANT]
> The Rust crates are currently a source preview. The intended crate names,
> `nbgv` and `cargo-nbgv`, are not claimed by this documentation to be available
> from crates.io. Until a release announcement says otherwise, build them from
> this repository.

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

After the first crates.io release, the equivalent intended commands will be:

```toml
[build-dependencies]
nbgv = "0.1"
```

```console
cargo install cargo-nbgv --version 0.1.0
```

Check the release notes before using these registry commands. Both crates
require Rust 1.78 or newer.

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
