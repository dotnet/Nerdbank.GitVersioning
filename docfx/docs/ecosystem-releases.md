# Ecosystem package release status

Rust, Go, and Java integrations are built, tested, and packaged on ordinary pull requests without registry credentials.
A successful build uploads an `ecosystem-deployables` artifact; the release workflow attaches the verified files to a GitHub Release.
Rust publishing uses the existing native workspace's protected Trusted Publishing flow.
Java publication signs and publishes Maven artifacts to Maven Central, waits for them to be published, and then uploads the Gradle plugin to the Plugin Portal.
Go acquisition uses immutable repository tags rather than a registry upload.

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
Configure the verified Maven Central namespace, Plugin Portal account, and signing key before enabling Java publication.
Do not expose these credentials to pull-request jobs.

1. Repeat collision/ownership checks for `nerdbank-gitversioning-build`, `cargo-nbgv`,
   `com.microsoft.nerdbank-gitversioning`, and the matching Gradle plugin ID.
2. Use the existing `cargo publish --workspace` release step for `nerdbank-gitversioning`, `nbgv`, `nerdbank-gitversioning-build`, and `cargo-nbgv`, publishing the shared library before its dependents.
   Do not publish a build helper under the native CLI's `nbgv` name.
3. Create and push the signed Go tag `go/v0.1.0`; Go modules have no upload
   command.
4. Configure the Java publishing secrets described below; the release workflow publishes signed Java artifacts through the Central Portal and waits for the `PUBLISHED` state.
5. The workflow then invokes `:gradle-plugin:publishPlugins`; the first Plugin Portal submission requires manual approval by Gradle before consumers can acquire it.
6. Verify every registry response and test the documented acquisition commands from clean caches before publishing the documentation.

## Java publishing configuration

Set these secrets in the protected `Publishing` environment:

| Secret | Value |
| --- | --- |
| `MAVEN_CENTRAL_USERNAME` | Central Portal user-token username, not the account login. |
| `MAVEN_CENTRAL_PASSWORD` | Central Portal user-token password. |
| `MAVEN_SIGNING_KEY` | ASCII-armored private PGP signing key; distribute its public key as required by Maven Central. |
| `MAVEN_SIGNING_PASSWORD` | Signing key passphrase. |
| `GRADLE_PUBLISH_KEY` | Plugin Portal API key. |
| `GRADLE_PUBLISH_SECRET` | Plugin Portal API secret. |

The workflow checks credentials and verifies that the checkout matches the selected successful build before registry publication begins.
It reads the integration version from that build's `packages.json`, rather than substituting the .NET release version.
The Vanniktech Maven publishing base plugin uses the existing publications, signs with an in-memory PGP key, and uploads through the Central Portal API.
Normal pull-request builds continue to stage unsigned packages without credentials.

For a manual workflow dispatch, `publish_java` controls both Java registry steps.
Disable it when intentionally shipping other ecosystems only, or when Java's independently versioned packages have already been published.
Registry failures fail the workflow; configuring these steps does not create accounts, claim namespaces, or bypass first-publication approval.
