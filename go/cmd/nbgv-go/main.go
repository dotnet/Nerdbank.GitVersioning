// Command nbgv-go runs Go commands with version information calculated by
// Nerdbank.GitVersioning.
package main

import (
	"os"

	"github.com/dotnet/Nerdbank.GitVersioning/go/internal/command"
)

func main() {
	os.Exit(command.Run(os.Args[1:], os.Stderr))
}
