# Go

> [!IMPORTANT]
> The Go module and `nbgv-go` command are currently a source preview. Do not
> assume that a stable module tag has been published. Until a release
> announcement says otherwise, install from a repository checkout.

The module path is:

```text
github.com/dotnet/Nerdbank.GitVersioning/go
```

It contains the `nbgv-go` command and the `version` runtime package. The command
is an opt-in wrapper around `go build`, `go test`, and `go install`; it never
intercepts a plain Go command.

Install the native Rust or .NET `nbgv` CLI separately.
Both support the [adapter JSON contract](../adapter-json.md), so the wrapper can use native `nbgv` without a .NET runtime.

## Acquire from source

```console
git clone https://github.com/dotnet/Nerdbank.GitVersioning
cd Nerdbank.GitVersioning/go
go install ./cmd/nbgv-go
```

After the first tagged module release, the intended equivalent is:

```console
go install github.com/dotnet/Nerdbank.GitVersioning/go/cmd/nbgv-go@v0.1.0
go get github.com/dotnet/Nerdbank.GitVersioning/go@v0.1.0
```

Confirm the version in the release notes before using these commands.

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
