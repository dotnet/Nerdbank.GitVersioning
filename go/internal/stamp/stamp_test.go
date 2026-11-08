package stamp

import (
	"strings"
	"testing"

	"github.com/dotnet/Nerdbank.GitVersioning/go/internal/adapter"
)

func TestAppendLinkerFlagsPreservesExistingFlags(t *testing.T) {
	arguments, err := AppendLinkerFlags([]string{"-trimpath", "-ldflags=-s -w", "./cmd/app"}, "", "-X=example.value=1")
	if err != nil {
		t.Fatal(err)
	}

	if got, want := strings.Join(arguments, "|"), "-ldflags=-X=example.value=1|-trimpath|-ldflags=-s -w -X=example.value=1|./cmd/app"; got != want {
		t.Fatalf("got %q, want %q", got, want)
	}
}

func TestAppendLinkerFlagsPreservesSeparateValue(t *testing.T) {
	arguments, err := AppendLinkerFlags([]string{"-ldflags", "-s -w", "./..."}, "", "-X=example.value=1")
	if err != nil {
		t.Fatal(err)
	}

	if got, want := strings.Join(arguments, "|"), "-ldflags=-X=example.value=1|-ldflags|-s -w -X=example.value=1|./..."; got != want {
		t.Fatalf("got %q, want %q", got, want)
	}
}

func TestAppendLinkerFlagsInsertsBeforePackages(t *testing.T) {
	arguments, err := AppendLinkerFlags([]string{"./cmd/app"}, "", "-X=example.value=1")
	if err != nil {
		t.Fatal(err)
	}

	if got, want := strings.Join(arguments, "|"), "-ldflags=-X=example.value=1|./cmd/app"; got != want {
		t.Fatalf("got %q, want %q", got, want)
	}
}

func TestAppendLinkerFlagsDoesNotAlterTestBinaryArguments(t *testing.T) {
	arguments, err := AppendLinkerFlags([]string{"./...", "-args", "-ldflags", "application-value"}, "", "-X=example.value=1")
	if err != nil {
		t.Fatal(err)
	}

	if got, want := strings.Join(arguments, "|"), "-ldflags=-X=example.value=1|./...|-args|-ldflags|application-value"; got != want {
		t.Fatalf("got %q, want %q", got, want)
	}
}

func TestAppendLinkerFlagsPreservesGOFLAGSAndPackagePatterns(t *testing.T) {
	arguments, err := AppendLinkerFlags(
		[]string{
			"-ldflags=example.com/other=-race",
			"-ldflags", "example.com/tool=-w",
			"./cmd/tool",
		},
		`-trimpath '-ldflags=-s -w' '-ldflags=example.com/tool=-H windowsgui'`,
		"-X=example.value=1",
	)
	if err != nil {
		t.Fatal(err)
	}

	want := []string{
		"-ldflags=-X=example.value=1",
		"-ldflags=-s -w -X=example.value=1",
		"-ldflags=example.com/tool=-H windowsgui -X=example.value=1",
		"-ldflags=example.com/other=-race -X=example.value=1",
		"-ldflags", "example.com/tool=-w -X=example.value=1",
		"./cmd/tool",
	}
	if got := strings.Join(arguments, "|"); got != strings.Join(want, "|") {
		t.Fatalf("got\n%q\nwant\n%q", arguments, want)
	}
}

func TestAppendLinkerFlagsRejectsInvalidGOFLAGS(t *testing.T) {
	if _, err := AppendLinkerFlags(nil, `'-ldflags=-s`, "-X=example.value=1"); err == nil {
		t.Fatal("expected unterminated GOFLAGS quote to fail")
	}
}

func TestGeneratedSourceIsDeterministic(t *testing.T) {
	commit := "0123456789abcdef"
	shortCommit := "0123456789"
	date := "2026-08-09T21:59:55Z"
	info := adapter.Info{
		SemVer:           "1.2.3",
		Version:          "1.2.3.4",
		VersionHeight:    4,
		GitCommitID:      &commit,
		GitCommitIDShort: &shortCommit,
		GitCommitDate:    &date,
		PublicRelease:    true,
	}

	first, err := GeneratedSource("main", info)
	if err != nil {
		t.Fatal(err)
	}

	second, err := GeneratedSource("main", info)
	if err != nil {
		t.Fatal(err)
	}

	if string(first) != string(second) || !strings.Contains(string(first), `SemVer:           "1.2.3"`) {
		t.Fatalf("unexpected generated source:\n%s", first)
	}
}
