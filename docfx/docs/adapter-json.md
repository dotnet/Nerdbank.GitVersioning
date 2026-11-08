# Build adapter JSON contract

External build adapters for ecosystems such as Rust, Go, and Java can obtain version information without depending on the broad, .NET-oriented output of `nbgv get-version --format json`:

```console
nbgv get-version --format adapter
```

The `adapter` format is an opt-in, narrow, versioned contract. The existing `json` format is unchanged and remains available for existing consumers; it is not an alias for the adapter contract.

Both the native Rust and .NET CLIs support this format.
Cargo build scripts use the native library directly; Go and Java adapters can launch either CLI.

## Schema version 1

Property names use the exact lower camel case shown below. All properties are always present and are emitted in this order.

```json
{
  "schemaVersion": 1,
  "semVer2": "1.2.3-beta.g0123456789",
  "version": "1.2.3.4567",
  "versionHeight": 3,
  "gitCommitId": "0123456789abcdef0123456789abcdef01234567",
  "gitCommitIdShort": "0123456789",
  "gitCommitDate": "2026-08-09T21:59:55Z",
  "publicRelease": false
}
```

| Property | JSON type | Meaning |
| --- | --- | --- |
| `schemaVersion` | integer | Contract version. This document defines version `1`. |
| `semVer2` | string | The complete SemVer 2.0 version, including prerelease and build metadata. |
| `version` | string | The calculated numeric `System.Version` representation. It has two to four dot-separated non-negative integer components. |
| `versionHeight` | integer | The Git version height before applying `versionHeightOffset`. |
| `gitCommitId` | string or null | The full lowercase hexadecimal Git object ID, with `-dirty` when configured for a dirty working tree. |
| `gitCommitIdShort` | string or null | The abbreviated Git object ID using the configured length policy (possibly empty), with the same optional `-dirty` suffix. |
| `gitCommitDate` | string or null | The committer timestamp normalized to UTC, formatted as RFC 3339 whole seconds (`yyyy-MM-ddTHH:mm:ssZ`). |
| `publicRelease` | boolean | Whether public release mode is active after command-line, environment, and ref rules are applied. |

The three Git properties are `null` only when the selected Git engine cannot supply commit information. They are not omitted.

Consumers must check `schemaVersion` before using other properties and reject unsupported versions with a clear error. Consumers should ignore unknown properties so compatible additions do not break them. A change to a property's name, JSON type, nullability, format, or meaning requires a new schema version.

The command writes one JSON object to standard output. Diagnostics go to standard error, and a nonzero exit code means no contract value was produced. Adapters should reject malformed JSON, missing required properties, and values with the wrong JSON type.

## Resolving the `nbgv` executable

Build adapters should apply this common resolution order:

1. An executable explicitly configured by the user in the adapter.
2. The value of the `NBGV_EXECUTABLE` environment variable.
3. The command name `nbgv`, resolved through the process `PATH` (and the platform's normal executable extension rules).

An explicit value or `NBGV_EXECUTABLE` is one executable path or command name, not a shell command and not a command plus arguments. Adapters should launch it directly without a shell, set the working directory to the project being versioned, and pass `get-version --format adapter` as separate arguments.

Adapters must not search unrelated installation directories, silently fall through after an explicitly selected executable fails, or download/install `nbgv` as a side effect. Local .NET tool invocation may be exposed by an adapter as an explicit configuration, but is not an implicit fallback in this policy.

## Cross-ecosystem parity tests

With .NET, Rust, Go 1.22 or newer, Java 8 or newer, and Gradle 8.8 or newer installed, run the shared end-to-end fixture from the repository root:

```powershell
pwsh test/Nerdbank.GitVersioning.Tests/AdapterParity.ps1
```

The fixture creates one deterministic Git history plus worktree and shallow-clone variants under the ignored `obj` directory.
It compares the newly built native and .NET CLI adapter output, then verifies the in-process Cargo helper and native-CLI-backed Go, Gradle, and Maven consumers against those values.
