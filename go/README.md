# Nerdbank.GitVersioning for Go

> This module is currently a source preview. Install it from a repository
> checkout until a release announcement confirms an immutable Go module tag.

This Go module contains:

- `nbgv-go`, an opt-in wrapper for `go build`, `go test`, and `go install`.
- `version`, a tiny runtime package that reads the embedded version.

`nbgv-go` delegates to the real Go command; it does not replace or intercept a
plain `go build`.

## Install from source

Install `nbgv` separately, then install the wrapper:

```console
cd Nerdbank.GitVersioning/go
go install ./cmd/nbgv-go
```

After the first module release, the intended command is:

```console
go install github.com/dotnet/Nerdbank.GitVersioning/go/cmd/nbgv-go@v0.1.0
```

Check the release notes before using that version.

Your program must import and use the runtime package so the linker retains it:

```go
import nbgvversion "github.com/dotnet/Nerdbank.GitVersioning/go/version"

func main() {
	info := nbgvversion.Current()
	fmt.Println(info.SemVer)
}
```

## Build, test, and install

Run the wrapper from the module to version:

```console
nbgv-go build ./...
nbgv-go test -ldflags="-s -w" ./...
nbgv-go install ./cmd/mytool
nbgv-go build -C ./submodule ./...
```

The wrapper obtains the version with `nbgv get-version --format adapter`, then
appends linker `-X` values without discarding effective `-ldflags` from either
`GOFLAGS` or the command line. Repeated and package-pattern-specific linker
settings are retained, and each setting is augmented so a later pattern match
does not suppress the version stamp. It starts both executables directly,
without a shell.

A leading Go `-C dir` or `-C=dir` flag is retained as the first build, test, or
install argument. `nbgv-go` calculates the version from that effective
directory, matching the directory in which the Go command operates.

The wrapper resolves `nbgv` in this order:

1. `--nbgv-executable`
2. `NBGV_EXECUTABLE`
3. `nbgv` through `PATH`

Each configured value is one executable path or command name, never a command
plus arguments. Selection of the real Go command is similar:
`--go-executable`, `NBGV_GO_EXECUTABLE`, then `go` through `PATH`.
`--project-dir` controls the working directory for both commands.

When no linker or generated stamp exists, `version.Current` falls back to Go's
`runtime/debug.BuildInfo`. The fallback can report the main module version and
VCS revision, time, and modified state, but it cannot reconstruct all NBGV
fields.

## Generated source

Ordinary `go install` does not invoke the wrapper. For workflows that require
it, generate a deterministic file in a package that is linked into the program
(normally the `main` package), and commit it or generate it before installation:

```console
nbgv-go generate --output cmd/mytool/nbgv_version_generated.go --package main
go install ./cmd/mytool
```

The generated file calls `version.RegisterGenerated` during initialization.
Regenerating from the same adapter JSON produces byte-for-byte identical
source and does not rewrite an unchanged file. Linker stamps take precedence
over generated source.

## Go module versions and tags

Go major-version rules still apply. Versions `v0` and `v1` use this module
path:

```text
github.com/dotnet/Nerdbank.GitVersioning/go
```

Because this module lives in the repository's `go` subdirectory, its release
tags are prefixed with that directory, for example `go/v1.2.3`. A `v2` module
must use the module/import path
`github.com/dotnet/Nerdbank.GitVersioning/go/v2` and a `go/v2.0.0` tag. Do not
publish a `v2` tag while retaining the `v1` module path.
