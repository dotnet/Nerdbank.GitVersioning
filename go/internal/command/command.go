// Package command implements the nbgv-go command line.
package command

import (
	"bytes"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"strings"

	"github.com/dotnet/Nerdbank.GitVersioning/go/internal/adapter"
	"github.com/dotnet/Nerdbank.GitVersioning/go/internal/stamp"
)

var packageNamePattern = regexp.MustCompile(`^[\pL_][\pL\pN_]*$`)

var goKeywords = map[string]bool{
	"break": true, "default": true, "func": true, "interface": true, "select": true,
	"case": true, "defer": true, "go": true, "map": true, "struct": true,
	"chan": true, "else": true, "goto": true, "package": true, "switch": true,
	"const": true, "fallthrough": true, "if": true, "range": true, "type": true,
	"continue": true, "for": true, "import": true, "return": true, "var": true,
}

// Run executes nbgv-go and returns its process exit code.
func Run(arguments []string, stderr io.Writer) int {
	flags := flag.NewFlagSet("nbgv-go", flag.ContinueOnError)
	flags.SetOutput(stderr)
	var nbgvExecutable string
	var goExecutable string
	var projectDirectory string
	flags.StringVar(&nbgvExecutable, "nbgv-executable", "", "nbgv executable path or command name")
	flags.StringVar(&goExecutable, "go-executable", "", "go executable path or command name")
	flags.StringVar(&projectDirectory, "project-dir", "", "project working directory (default: current directory)")
	flags.Usage = func() {
		fmt.Fprintln(stderr, "Usage: nbgv-go [options] <build|test|install> [go arguments]")
		fmt.Fprintln(stderr, "       nbgv-go [options] generate [--output file] [--package name]")
		fmt.Fprintln(stderr, "Options:")
		flags.PrintDefaults()
	}

	if err := flags.Parse(arguments); err != nil {
		if errors.Is(err, flag.ErrHelp) {
			return 0
		}

		return 2
	}

	remaining := flags.Args()
	if len(remaining) == 0 {
		flags.Usage()
		return 2
	}

	if projectDirectory == "" {
		var err error
		projectDirectory, err = os.Getwd()
		if err != nil {
			fmt.Fprintf(stderr, "nbgv-go: determine current directory: %v\n", err)
			return 1
		}
	}

	absoluteProjectDirectory, err := filepath.Abs(projectDirectory)
	if err != nil {
		fmt.Fprintf(stderr, "nbgv-go: resolve project directory: %v\n", err)
		return 1
	}

	switch remaining[0] {
	case "build", "test", "install":
		chdirArguments, chdir, commandArguments, chdirError := leadingChdir(remaining[1:])
		if chdirError != nil {
			fmt.Fprintf(stderr, "nbgv-go: %v\n", chdirError)
			return 2
		}

		versionDirectory := absoluteProjectDirectory
		if chdir != "" {
			if filepath.IsAbs(chdir) {
				versionDirectory = filepath.Clean(chdir)
			} else {
				versionDirectory = filepath.Join(absoluteProjectDirectory, chdir)
			}
		}

		versionInfo, getError := adapter.Get(adapter.Executable(nbgvExecutable), versionDirectory, stderr)
		if getError != nil {
			fmt.Fprintf(stderr, "nbgv-go: %v\n", getError)
			return 1
		}

		delegatedArguments, appendError := stamp.AppendLinkerFlags(commandArguments, os.Getenv("GOFLAGS"), stamp.LinkerFlags(versionInfo))
		if appendError != nil {
			fmt.Fprintf(stderr, "nbgv-go: %v\n", appendError)
			return 2
		}

		delegatedArguments = append(chdirArguments, delegatedArguments...)
		return runGo(resolveGoExecutable(goExecutable), absoluteProjectDirectory, remaining[0], delegatedArguments, stderr)
	case "generate":
		versionInfo, getError := adapter.Get(adapter.Executable(nbgvExecutable), absoluteProjectDirectory, stderr)
		if getError != nil {
			fmt.Fprintf(stderr, "nbgv-go: %v\n", getError)
			return 1
		}

		if err := generate(absoluteProjectDirectory, remaining[1:], versionInfo, stderr); err != nil {
			fmt.Fprintf(stderr, "nbgv-go: %v\n", err)
			return 1
		}

		return 0
	default:
		fmt.Fprintf(stderr, "nbgv-go: unsupported command %q; expected build, test, install, or generate\n", remaining[0])
		return 2
	}
}

func leadingChdir(arguments []string) (preserved []string, directory string, remaining []string, err error) {
	if len(arguments) == 0 {
		return nil, "", arguments, nil
	}

	switch argument := arguments[0]; {
	case argument == "-C" || argument == "--C":
		if len(arguments) < 2 {
			return nil, "", nil, fmt.Errorf("%s requires a directory", argument)
		}
		if arguments[1] == "" {
			return nil, "", nil, fmt.Errorf("%s requires a non-empty directory", argument)
		}

		return append([]string(nil), arguments[:2]...), arguments[1], arguments[2:], nil
	case strings.HasPrefix(argument, "-C=") || strings.HasPrefix(argument, "--C="):
		_, directory, _ := strings.Cut(argument, "=")
		if directory == "" {
			return nil, "", nil, fmt.Errorf("%s requires a non-empty directory", argument[:strings.IndexByte(argument, '=')])
		}

		return []string{argument}, directory, arguments[1:], nil
	default:
		return nil, "", arguments, nil
	}
}

func resolveGoExecutable(explicit string) string {
	if explicit != "" {
		return explicit
	}

	if fromEnvironment := os.Getenv("NBGV_GO_EXECUTABLE"); fromEnvironment != "" {
		return fromEnvironment
	}

	return "go"
}

func runGo(executable, directory, subcommand string, arguments []string, stderr io.Writer) int {
	commandArguments := append([]string{subcommand}, arguments...)
	command := exec.Command(executable, commandArguments...)
	command.Dir = directory
	command.Stdin = os.Stdin
	command.Stdout = os.Stdout
	command.Stderr = stderr
	if err := command.Run(); err != nil {
		var exitError *exec.ExitError
		if errors.As(err, &exitError) {
			return exitError.ExitCode()
		}

		fmt.Fprintf(stderr, "nbgv-go: run %q: %v\n", executable, err)
		return 1
	}

	return 0
}

func generate(projectDirectory string, arguments []string, info adapter.Info, stderr io.Writer) error {
	flags := flag.NewFlagSet("nbgv-go generate", flag.ContinueOnError)
	flags.SetOutput(stderr)
	output := "nbgv_version_generated.go"
	packageName := "main"
	flags.StringVar(&output, "output", output, "generated Go file, relative to project-dir")
	flags.StringVar(&packageName, "package", packageName, "package declaration for the generated file")
	if err := flags.Parse(arguments); err != nil {
		return err
	}

	if flags.NArg() != 0 {
		return fmt.Errorf("generate does not accept positional arguments: %v", flags.Args())
	}

	if !packageNamePattern.MatchString(packageName) || goKeywords[packageName] {
		return fmt.Errorf("%q is not a valid Go package name", packageName)
	}

	source, err := stamp.GeneratedSource(packageName, info)
	if err != nil {
		return fmt.Errorf("format generated source: %w", err)
	}

	outputPath := output
	if !filepath.IsAbs(outputPath) {
		outputPath = filepath.Join(projectDirectory, outputPath)
	}

	if err := writeFileIfChanged(outputPath, source); err != nil {
		return fmt.Errorf("write generated source: %w", err)
	}

	return nil
}

func writeFileIfChanged(path string, content []byte) error {
	existing, err := os.ReadFile(path)
	if err == nil && bytes.Equal(existing, content) {
		return nil
	}

	if err != nil && !errors.Is(err, os.ErrNotExist) {
		return err
	}

	directory := filepath.Dir(path)
	if err := os.MkdirAll(directory, 0o755); err != nil {
		return err
	}

	temporary, err := os.CreateTemp(directory, ".nbgv-go-*")
	if err != nil {
		return err
	}

	temporaryPath := temporary.Name()
	defer os.Remove(temporaryPath)
	if _, err := temporary.Write(content); err != nil {
		temporary.Close()
		return err
	}

	if err := temporary.Chmod(0o644); err != nil {
		temporary.Close()
		return err
	}

	if err := temporary.Close(); err != nil {
		return err
	}

	if err := os.Rename(temporaryPath, path); err != nil {
		if removeError := os.Remove(path); removeError != nil && !errors.Is(removeError, os.ErrNotExist) {
			return err
		}

		if retryError := os.Rename(temporaryPath, path); retryError != nil {
			return retryError
		}
	}

	return nil
}
