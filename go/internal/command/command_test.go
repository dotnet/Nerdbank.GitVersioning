package command

import (
	"bytes"
	"encoding/json"
	"os"
	"os/exec"
	"path/filepath"
	"reflect"
	"runtime"
	"strings"
	"testing"

	"github.com/dotnet/Nerdbank.GitVersioning/go/internal/adapter"
	"github.com/dotnet/Nerdbank.GitVersioning/go/internal/stamp"
)

type helperInvocation struct {
	Arguments []string
	Directory string
}

func TestMain(m *testing.M) {
	if os.Getenv("NBGV_GO_TEST_HELPER") == "1" {
		arguments := os.Args[1:]
		if len(arguments) >= 3 && arguments[0] == "get-version" {
			if capture := os.Getenv("NBGV_GO_TEST_ADAPTER_CAPTURE"); capture != "" {
				directory, _ := os.Getwd()
				invocation, _ := json.Marshal(helperInvocation{Arguments: arguments, Directory: directory})
				_ = os.WriteFile(capture, invocation, 0o600)
			}

			os.Stdout.WriteString(`{"schemaVersion":1,"semVer2":"1.2.3-beta.1","version":"1.2.3.4","versionHeight":4,"gitCommitId":"0123456789abcdef","gitCommitIdShort":"0123456789","gitCommitDate":"2026-08-09T21:59:55Z","publicRelease":false}`)
			os.Exit(0)
		}

		directory, _ := os.Getwd()
		invocation, _ := json.Marshal(helperInvocation{Arguments: arguments, Directory: directory})
		_ = os.WriteFile(os.Getenv("NBGV_GO_TEST_CAPTURE"), invocation, 0o600)
		os.Exit(23)
	}

	os.Exit(m.Run())
}

func TestRunDelegatesAndReturnsGoExitCode(t *testing.T) {
	executable, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}

	projectDirectory := t.TempDir()
	capturePath := filepath.Join(projectDirectory, "invocation.json")
	t.Setenv("NBGV_GO_TEST_HELPER", "1")
	t.Setenv("NBGV_GO_TEST_CAPTURE", capturePath)
	t.Setenv("GOFLAGS", "")

	var stderr strings.Builder
	exitCode := Run([]string{
		"--nbgv-executable", executable,
		"--go-executable", executable,
		"--project-dir", projectDirectory,
		"build", "-trimpath", "-ldflags=-s -w", "./cmd/app",
	}, &stderr)
	if exitCode != 23 {
		t.Fatalf("got exit code %d, want 23; stderr: %s", exitCode, stderr.String())
	}

	var invocation helperInvocation
	content, err := os.ReadFile(capturePath)
	if err != nil {
		t.Fatal(err)
	}

	if err := json.Unmarshal(content, &invocation); err != nil {
		t.Fatal(err)
	}

	if invocation.Directory != projectDirectory {
		t.Fatalf("go directory was %q, want %q", invocation.Directory, projectDirectory)
	}

	if len(invocation.Arguments) != 5 || invocation.Arguments[0] != "build" ||
		!strings.HasPrefix(invocation.Arguments[1], "-ldflags=-X=github.com/dotnet/Nerdbank.GitVersioning/go/version.semVer=1.2.3-beta.1") ||
		invocation.Arguments[2] != "-trimpath" || invocation.Arguments[4] != "./cmd/app" {
		t.Fatalf("unexpected arguments: %#v", invocation.Arguments)
	}

	if !strings.HasPrefix(invocation.Arguments[3], "-ldflags=-s -w -X=github.com/dotnet/Nerdbank.GitVersioning/go/version.semVer=1.2.3-beta.1") {
		t.Fatalf("existing ldflags were not safely preserved: %q", invocation.Arguments[3])
	}
}

func TestRunUsesLeadingChdirForVersionAndGo(t *testing.T) {
	executable, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}

	projectDirectory := t.TempDir()
	effectiveDirectory := filepath.Join(projectDirectory, "nested")
	if err := os.Mkdir(effectiveDirectory, 0o755); err != nil {
		t.Fatal(err)
	}

	goCapture := filepath.Join(projectDirectory, "go.json")
	adapterCapture := filepath.Join(projectDirectory, "adapter.json")
	t.Setenv("NBGV_GO_TEST_HELPER", "1")
	t.Setenv("NBGV_GO_TEST_CAPTURE", goCapture)
	t.Setenv("NBGV_GO_TEST_ADAPTER_CAPTURE", adapterCapture)
	t.Setenv("GOFLAGS", "")

	var stderr strings.Builder
	exitCode := Run([]string{
		"--nbgv-executable", executable,
		"--go-executable", executable,
		"--project-dir", projectDirectory,
		"build", "-C=nested", ".",
	}, &stderr)
	if exitCode != 23 {
		t.Fatalf("got exit code %d, want 23; stderr: %s", exitCode, stderr.String())
	}

	adapterInvocation := readHelperInvocation(t, adapterCapture)
	if adapterInvocation.Directory != effectiveDirectory {
		t.Fatalf("nbgv directory was %q, want %q", adapterInvocation.Directory, effectiveDirectory)
	}

	goInvocation := readHelperInvocation(t, goCapture)
	if goInvocation.Directory != projectDirectory {
		t.Fatalf("go directory was %q, want %q", goInvocation.Directory, projectDirectory)
	}

	if len(goInvocation.Arguments) < 4 || goInvocation.Arguments[0] != "build" ||
		goInvocation.Arguments[1] != "-C=nested" ||
		!strings.HasPrefix(goInvocation.Arguments[2], "-ldflags=") ||
		goInvocation.Arguments[3] != "." {
		t.Fatalf("unexpected go command: %#v", goInvocation.Arguments)
	}
}

func TestLeadingChdirSeparateArgument(t *testing.T) {
	preserved, directory, remaining, err := leadingChdir([]string{"-C", "somewhere", "-trimpath", "."})
	if err != nil {
		t.Fatal(err)
	}

	if !reflect.DeepEqual(preserved, []string{"-C", "somewhere"}) ||
		directory != "somewhere" ||
		!reflect.DeepEqual(remaining, []string{"-trimpath", "."}) {
		t.Fatalf("unexpected parse: preserved=%q directory=%q remaining=%q", preserved, directory, remaining)
	}
}

func TestRunGenerateWritesStableSource(t *testing.T) {
	executable, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}

	projectDirectory := t.TempDir()
	t.Setenv("NBGV_GO_TEST_HELPER", "1")
	t.Setenv("NBGV_GO_TEST_CAPTURE", filepath.Join(projectDirectory, "unused.json"))
	t.Setenv("GOFLAGS", "")
	arguments := []string{
		"--nbgv-executable", executable,
		"--project-dir", projectDirectory,
		"generate", "--output", "cmd/app/version_generated.go", "--package", "main",
	}

	var stderr strings.Builder
	if exitCode := Run(arguments, &stderr); exitCode != 0 {
		t.Fatalf("got exit code %d; stderr: %s", exitCode, stderr.String())
	}

	path := filepath.Join(projectDirectory, "cmd", "app", "version_generated.go")
	first, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}

	if exitCode := Run(arguments, &stderr); exitCode != 0 {
		t.Fatalf("second generation got exit code %d; stderr: %s", exitCode, stderr.String())
	}

	second, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}

	if !reflect.DeepEqual(first, second) {
		t.Fatal("generated output changed between identical runs")
	}
}

func TestRunBuildEmbedsVersionWithRealGo(t *testing.T) {
	helperExecutable, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}

	baseDirectory := t.TempDir()
	projectDirectory := filepath.Join(baseDirectory, "nested")
	if err := os.Mkdir(projectDirectory, 0o755); err != nil {
		t.Fatal(err)
	}

	moduleDirectory, err := filepath.Abs(filepath.Join("..", ".."))
	if err != nil {
		t.Fatal(err)
	}

	goMod := "module example.com/nbgvtest\n\ngo 1.22\n\n" +
		"require github.com/dotnet/Nerdbank.GitVersioning/go v0.0.0\n" +
		"replace github.com/dotnet/Nerdbank.GitVersioning/go => " + filepath.ToSlash(moduleDirectory) + "\n"
	mainSource := `package main

import (
	"fmt"
	nbgvversion "github.com/dotnet/Nerdbank.GitVersioning/go/version"
)

func main() {
	info := nbgvversion.Current()
	fmt.Printf("%s|%s|%d|%s|%t", info.SemVer, info.Version, info.VersionHeight, info.Source, info.PublicRelease)
}
`
	if err := os.WriteFile(filepath.Join(projectDirectory, "go.mod"), []byte(goMod), 0o600); err != nil {
		t.Fatal(err)
	}

	if err := os.WriteFile(filepath.Join(projectDirectory, "main.go"), []byte(mainSource), 0o600); err != nil {
		t.Fatal(err)
	}

	output := filepath.Join(projectDirectory, "app")
	if runtime.GOOS == "windows" {
		output += ".exe"
	}
	goExecutable := filepath.Join(runtime.GOROOT(), "bin", "go"+executableSuffix())

	t.Setenv("NBGV_GO_TEST_HELPER", "1")
	t.Setenv("NBGV_GO_TEST_CAPTURE", filepath.Join(projectDirectory, "unused.json"))
	t.Setenv("GOFLAGS", "")
	var stderr strings.Builder
	exitCode := Run([]string{
		"--nbgv-executable", helperExecutable,
		"--go-executable", goExecutable,
		"--project-dir", baseDirectory,
		"build", "-C=nested", "-o", output, ".",
	}, &stderr)
	if exitCode != 0 {
		t.Fatalf("got exit code %d; stderr: %s", exitCode, stderr.String())
	}

	builtOutput, err := exec.Command(output).CombinedOutput()
	if err != nil {
		t.Fatalf("run built program: %v\n%s", err, builtOutput)
	}

	if got, want := string(builtOutput), "1.2.3-beta.1|1.2.3.4|4|linker|false"; got != want {
		t.Fatalf("got %q, want %q", got, want)
	}

	strippedOutput := filepath.Join(projectDirectory, "app-stripped"+executableSuffix())
	t.Setenv("GOFLAGS", `'-ldflags=-s -w'`)
	exitCode = Run([]string{
		"--nbgv-executable", helperExecutable,
		"--go-executable", goExecutable,
		"--project-dir", projectDirectory,
		"build",
		"-o", strippedOutput,
		"-ldflags=example.com/not-built=-not-a-real-linker-flag",
		".",
	}, &stderr)
	if exitCode != 0 {
		t.Fatalf("GOFLAGS build got exit code %d; stderr: %s", exitCode, stderr.String())
	}

	strippedProgramOutput, err := exec.Command(strippedOutput).CombinedOutput()
	if err != nil {
		t.Fatalf("run stripped program: %v\n%s", err, strippedProgramOutput)
	}

	if got, want := string(strippedProgramOutput), "1.2.3-beta.1|1.2.3.4|4|linker|false"; got != want {
		t.Fatalf("GOFLAGS build got %q, want %q", got, want)
	}

	nm := exec.Command(goExecutable, "tool", "nm", strippedOutput)
	nm.Env = append(os.Environ(), "GOFLAGS=")
	nmOutput, nmError := nm.CombinedOutput()
	if nmError == nil || !strings.Contains(string(nmOutput), "no symbols") {
		t.Fatalf("GOFLAGS -s was not retained; error: %v\n%s", nmError, nmOutput)
	}

	patternOutput := filepath.Join(projectDirectory, "app-pattern"+executableSuffix())
	t.Setenv("GOFLAGS", "")
	exitCode = Run([]string{
		"--nbgv-executable", helperExecutable,
		"--go-executable", goExecutable,
		"--project-dir", projectDirectory,
		"build",
		"-o", patternOutput,
		"-ldflags=example.com/not-built=-not-a-real-linker-flag",
		"-ldflags=example.com/nbgvtest=-s -w",
		".",
	}, &stderr)
	if exitCode != 0 {
		t.Fatalf("pattern-specific build got exit code %d; stderr: %s", exitCode, stderr.String())
	}

	patternProgramOutput, err := exec.Command(patternOutput).CombinedOutput()
	if err != nil {
		t.Fatalf("run pattern-specific program: %v\n%s", err, patternProgramOutput)
	}

	if got, want := string(patternProgramOutput), "1.2.3-beta.1|1.2.3.4|4|linker|false"; got != want {
		t.Fatalf("pattern-specific build got %q, want %q", got, want)
	}

	exitCode = Run([]string{
		"--nbgv-executable", helperExecutable,
		"--project-dir", projectDirectory,
		"generate", "--package", "main",
	}, &stderr)
	if exitCode != 0 {
		t.Fatalf("generate got exit code %d; stderr: %s", exitCode, stderr.String())
	}

	installDirectory := filepath.Join(projectDirectory, "bin")
	if err := os.Mkdir(installDirectory, 0o755); err != nil {
		t.Fatal(err)
	}

	install := exec.Command(goExecutable, "install", ".")
	install.Dir = projectDirectory
	install.Env = append(os.Environ(), "GOBIN="+installDirectory)
	if installOutput, err := install.CombinedOutput(); err != nil {
		t.Fatalf("plain go install: %v\n%s", err, installOutput)
	}

	installedProgram := filepath.Join(installDirectory, "nbgvtest"+executableSuffix())
	generatedOutput, err := exec.Command(installedProgram).CombinedOutput()
	if err != nil {
		t.Fatalf("run installed program: %v\n%s", err, generatedOutput)
	}

	if got, want := string(generatedOutput), "1.2.3-beta.1|1.2.3.4|4|generated|false"; got != want {
		t.Fatalf("generated mode got %q, want %q", got, want)
	}
}

func TestRunGenerateUsesSharedDotNetOracle(t *testing.T) {
	manifestPath := os.Getenv("NBGV_PARITY_MANIFEST")
	if manifestPath == "" {
		t.Skip("NBGV_PARITY_MANIFEST is not set; shared parity fixture was not requested")
	}

	content, err := os.ReadFile(manifestPath)
	if err != nil {
		t.Fatal(err)
	}
	var manifest struct {
		Executable string `json:"executable"`
		Scenarios  []struct {
			Directory string `json:"directory"`
			Oracle    string `json:"oracle"`
			GitEngine string `json:"gitEngine"`
		} `json:"scenarios"`
	}
	if err := json.Unmarshal(content, &manifest); err != nil {
		t.Fatal(err)
	}
	if len(manifest.Scenarios) == 0 {
		t.Fatal("parity manifest has no scenarios")
	}

	scenario := manifest.Scenarios[0]
	t.Setenv("NBGV_GitEngine", scenario.GitEngine)
	oracleJSON, err := os.ReadFile(scenario.Oracle)
	if err != nil {
		t.Fatal(err)
	}
	expectedInfo, err := adapter.Parse(oracleJSON)
	if err != nil {
		t.Fatal(err)
	}
	expectedSource, err := stamp.GeneratedSource("main", expectedInfo)
	if err != nil {
		t.Fatal(err)
	}

	output := filepath.Join(scenario.Directory, "nbgv_parity_generated.go")
	t.Cleanup(func() { _ = os.Remove(output) })
	var stderr strings.Builder
	exitCode := Run([]string{
		"--nbgv-executable", manifest.Executable,
		"--project-dir", scenario.Directory,
		"generate", "--output", output, "--package", "main",
	}, &stderr)
	if exitCode != 0 {
		t.Fatalf("got exit code %d; stderr: %s", exitCode, stderr.String())
	}
	actualSource, err := os.ReadFile(output)
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(actualSource, expectedSource) {
		t.Fatalf("generated source differs from .NET oracle:\n%s", actualSource)
	}
}

func executableSuffix() string {
	if runtime.GOOS == "windows" {
		return ".exe"
	}

	return ""
}

func readHelperInvocation(t *testing.T, path string) helperInvocation {
	t.Helper()
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}

	var invocation helperInvocation
	if err := json.Unmarshal(content, &invocation); err != nil {
		t.Fatal(err)
	}

	return invocation
}
