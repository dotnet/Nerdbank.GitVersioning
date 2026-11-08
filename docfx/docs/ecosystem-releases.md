# Ecosystem package release status

Rust, Go, and Java integrations are built, tested, and packaged on ordinary
pull requests without registry credentials. They are **not automatically
published**. A successful build uploads an `ecosystem-deployables` artifact;
the release workflow attaches the same verified files to a GitHub Release.

The initial integration version is `0.1.0` across Rust and Java. The Go module
uses the corresponding `go/v0.1.0` Git tag. Integration versions are independent
of the .NET package version and must be deliberately advanced in their package
manifests.

## What CI verifies

`tools/Build-EcosystemAdapters.ps1`:

1. runs the shared .NET/Rust/Go/Java adapter parity fixtures;
2. builds and verifies both `.crate` files;
3. tests the Go module and cross-compiles `nbgv-go`;
4. tests and validates the Gradle plugin;
5. stages Maven POM, JAR, sources, Javadoc, module, and plugin-marker files.

Build outputs remain ignored and are copied to a dedicated CI artifact with a
SHA-256 checksum list. On release, Windows `nbgv-go` executables are signed by
the existing protected signing job and the checksum list is regenerated before
the files are attached.

## First-publication checklist

Registry publication remains a manual or follow-up operation until project
owners approve namespaces, accounts, signing, and protected release secrets.
Do not expose these credentials to pull-request jobs.

1. Repeat collision/ownership checks for `nbgv`, `cargo-nbgv`,
   `com.microsoft.nerdbank-gitversioning`, and the matching Gradle plugin ID.
2. Publish the `nbgv` crate before `cargo-nbgv` so the documented build
   dependency is resolvable before its companion packaging command is offered.
3. Create and push the signed Go tag `go/v0.1.0`; Go modules have no upload
   command.
4. Sign Maven artifacts and publish `nbgv-adapter` and
   `nbgv-maven-extension` through the approved Maven Central namespace.
5. Publish the Gradle plugin only after its Maven dependencies resolve from a
   clean repository.
6. Verify every registry response, then update the acquisition docs from
   “source preview” to released instructions.

If publication is automated later, gate it on the existing protected
`release: published` workflow/environment. Use clearly scoped secrets such as
`CARGO_REGISTRY_TOKEN`, `MAVEN_CENTRAL_USERNAME`,
`MAVEN_CENTRAL_PASSWORD`, `MAVEN_SIGNING_KEY`,
`MAVEN_SIGNING_PASSWORD`, `GRADLE_PUBLISH_KEY`, and
`GRADLE_PUBLISH_SECRET`. Their names here are a design contract, not evidence
that the secrets or registry accounts already exist.
