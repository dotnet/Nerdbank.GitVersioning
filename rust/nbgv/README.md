# `nbgv` for Cargo

> This crate is currently a source preview. Use a path dependency until a
> release announcement confirms that it is available from crates.io.

This crate is a build dependency that invokes the first-party `nbgv` tool and
exposes its version to Rust compilation.

```toml
[build-dependencies]
nbgv = { path = "../Nerdbank.GitVersioning/rust/nbgv" }
```

After the first crates.io release, replace the path with the announced `0.1`
version.

```rust
// build.rs
fn main() {
    nbgv::emit!();
}
```

`emit!` writes `NBGV_SEMVER2`, `NBGV_VERSION`, `NBGV_VERSION_HEIGHT`,
`NBGV_GIT_COMMIT_ID`, `NBGV_GIT_COMMIT_ID_SHORT`, `NBGV_GIT_COMMIT_DATE`, and
`NBGV_PUBLIC_RELEASE` for `env!`. It also writes a typed module:

```rust
include!(concat!(env!("OUT_DIR"), "/nbgv.rs"));

const VERSION: &str = NBGV_VERSION.sem_ver2;
```

Each nullable Git environment variable is empty, and its generated field is
`None`, when the adapter reports that value as unavailable. These fields are
independently nullable; for example a cloud build may provide commit IDs while
the selected Git engine cannot provide the commit date.

Build scripts watch `NBGV_EXECUTABLE`, `NBGV_GitEngine`, `PublicRelease`, and
other environment inputs, applicable ancestor `version.json` paths, and the
repository's HEAD, current ref, tags, packed refs, and shallow-clone state. The
adapter contract does not report its input paths, so discovery stops at the
nearest `.git` marker; without one, all ancestor `version.json` paths are
watched and Git inputs are omitted.

The executable resolution order is an explicit
`Config::executable(...)`, `NBGV_EXECUTABLE`, then `nbgv` on `PATH`. Values are
launched directly and cannot contain arguments. For a workspace, call the
adapter from each package build script; the default project directory is that
package's `CARGO_MANIFEST_DIR`, so `nbgv` discovers the applicable inherited
`version.json` files.
