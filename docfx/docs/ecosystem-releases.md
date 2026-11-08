# Ecosystem package release status

Rust, Go, and Java integrations are built, tested, and packaged on ordinary pull requests without registry credentials.
A successful build uploads an `ecosystem-deployables` artifact; the release workflow attaches the verified files to a GitHub Release.
Rust publishing uses the existing native workspace's protected Trusted Publishing flow.
Go and Java publication remain manual or follow-up operations.

All Rust packages share the native workspace version, set by `tools/Set-CargoVersion.ps1`.
The initial Java integration version is `0.1.0`, and the Go module uses the corresponding `go/v0.1.0` Git tag.
Go and Java integration versions remain independent of the native/.NET version and must be deliberately advanced.

## What CI verifies

`tools/Build-EcosystemAdapters.ps1`:

1. runs the shared .NET/Rust/Go/Java adapter parity fixtures;
2. builds and verifies the native library, CLI, Cargo build helper, and packaging command as one Cargo workspace;
3. tests the Go module and cross-compiles `nbgv-go`;
4. tests and validates the Gradle plugin;
5. stages Maven POM, JAR, sources, Javadoc, module, and plugin-marker files.

Build outputs remain ignored and are copied to a dedicated CI artifact with a
SHA-256 checksum list. On release, Windows `nbgv-go` executables are signed by
the existing protected signing job and the checksum list is regenerated before
the files are attached.

## First-publication checklist

New Rust crate names must be authorized in the existing Trusted Publishing setup before their first release.
Go and Java registry publication remains a manual or follow-up operation until project owners approve namespaces, accounts, signing, and protected release secrets.
Do not expose these credentials to pull-request jobs.

1. Repeat collision/ownership checks for `nerdbank-gitversioning-build`, `cargo-nbgv`,
   `com.microsoft.nerdbank-gitversioning`, and the matching Gradle plugin ID.
2. Use the existing `cargo publish --workspace` release step for `nerdbank-gitversioning`, `nbgv`, `nerdbank-gitversioning-build`, and `cargo-nbgv`, publishing the shared library before its dependents.
   Do not publish a build helper under the native CLI's `nbgv` name.
3. Create and push the signed Go tag `go/v0.1.0`; Go modules have no upload
   command.
4. Sign Maven artifacts and publish `nbgv-adapter` and
   `nbgv-maven-extension` through the approved Maven Central namespace.
5. Publish the Gradle plugin only after its Maven dependencies resolve from a
   clean repository.
6. Verify every registry response and test the documented acquisition commands from clean caches before publishing the documentation.

If publication is automated later, gate it on the existing protected
`release: published` workflow/environment. Use clearly scoped secrets such as
`CARGO_REGISTRY_TOKEN`, `MAVEN_CENTRAL_USERNAME`,
`MAVEN_CENTRAL_PASSWORD`, `MAVEN_SIGNING_KEY`,
`MAVEN_SIGNING_PASSWORD`, `GRADLE_PUBLISH_KEY`, and
`GRADLE_PUBLISH_SECRET`. Their names here are a design contract, not evidence
that the secrets or registry accounts already exist.
