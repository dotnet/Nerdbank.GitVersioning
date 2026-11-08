# Rust and Cargo integration

> The `nbgv` and `cargo-nbgv` crates are currently source previews. This README
> does not claim that either name is already available on crates.io. Install
> from this checkout until a release announcement says otherwise.

The `nbgv` crate in this directory is the build-script adapter. See
[`nbgv/README.md`](nbgv/README.md) for setup and workspace guidance.

```console
cargo install --path cargo-nbgv
```

`cargo-nbgv` provides staged `cargo package` and `cargo publish` operations:

```console
cargo nbgv package --manifest-path path/to/Cargo.toml -- --allow-dirty
cargo nbgv publish --manifest-path path/to/Cargo.toml -- --dry-run
```

The command obtains `semVer2` from the same adapter contract, asks Cargo to
prepare the original package, extracts those normalized contents to a staging
directory below the workspace target directory, and changes only the staged
manifest. This preserves Cargo-normalized external README/license files,
workspace-inherited package metadata, and generated lock files. The checked-in
`Cargo.toml` is never modified. `publish` refuses non-public versions unless
`--allow-non-public` is explicitly supplied.

The initial command is deliberately package-scoped. Cargo materializes
workspace-inherited package fields before staging. Multi-package publishing
still requires coordinated dependency version rewrites that Cargo cannot
safely infer. Cargo performs its normal checks, including dirty-source
protection and rejection of unsuitable path dependencies. Pass
`--allow-dirty` after `--` only when intentionally opting out of that
protection. Arguments after `--` are passed to the underlying Cargo operation.
`--locked`, `--frozen`, and `--offline` are also applied while Cargo prepares
the source package; the staged lockfile's root package version is then updated
with the staged manifest so `--locked` remains valid without changing
dependency versions.

The intended initial registry versions are `nbgv` 0.1.0 and `cargo-nbgv` 0.1.0.
Repeat crates.io ownership checks before first publication. See the
[Rust acquisition and migration guide][rust-docs] and
[release checklist][release-docs].

[rust-docs]: https://dotnet.github.io/Nerdbank.GitVersioning/docs/ecosystems/rust.html
[release-docs]: https://dotnet.github.io/Nerdbank.GitVersioning/docs/ecosystem-releases.html
