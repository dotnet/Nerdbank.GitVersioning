# Go

The module path is:

```text
github.com/dotnet/Nerdbank.GitVersioning/go
```

It contains the `nbgv-go` command and the `version` runtime package. The command
is an opt-in wrapper around `go build`, `go test`, and `go install`; it never
intercepts a plain Go command.

Install the native Rust or .NET `nbgv` CLI separately.
Both support the [adapter JSON contract](../adapter-json.md), so the wrapper can use native `nbgv` without a .NET runtime.

## Install

Install the wrapper, then add the runtime package to your module:

```console
go install github.com/dotnet/Nerdbank.GitVersioning/go/cmd/nbgv-go@v0.1.0
go get github.com/dotnet/Nerdbank.GitVersioning/go@v0.1.0
```

Use the same released version for the wrapper and runtime package.

## Stamp and read a version

Run the wrapper from the module being versioned:

```console
nbgv-go build ./...
nbgv-go test -ldflags="-s -w" ./...
```

Read the retained values in the program:

```go
import nbgvversion "github.com/dotnet/Nerdbank.GitVersioning/go/version"

info := nbgvversion.Current()
fmt.Println(info.SemVer)
```

The wrapper appends `-X` linker values without discarding existing `-ldflags`.
For an ordinary `go install`, use `nbgv-go generate` before installation. See
the [Go README][go-readme] for command selection, generated source, and
`runtime/debug.BuildInfo` fallback behavior.

## Migrate an existing module

1. Remove duplicate tag-, date-, or branch-based linker stamping.
2. Retain unrelated linker flags and run the same Go operation through
   `nbgv-go`.
3. Import and call `version.Current`; an unused import may be removed by the
   compiler and cannot retain the linker targets.
4. Choose either wrapper stamping or generated source as the primary path.
5. Keep Go's major-version suffix rules. A future v2 module must use
   `/go/v2` and a repository tag such as `go/v2.0.0`.

Because the module is below the repository's `go` directory, v0/v1 release tags
are directory-prefixed (for example, `go/v0.1.0`). There is no separate registry
upload: Go proxies acquire the module from an immutable Git tag.

[go-readme]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/main/go/README.md
