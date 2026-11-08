package adapter

import (
	"bytes"
	"encoding/json"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

const validJSON = `{
	"schemaVersion": 1,
	"semVer2": "1.2.3-beta.g0123456789+build.4",
	"version": "1.2.3.4",
	"versionHeight": 7,
	"gitCommitId": "0123456789abcdef",
	"gitCommitIdShort": "0123456789",
	"gitCommitDate": "2026-08-09T21:59:55Z",
	"publicRelease": false
}`

func TestParseValidAndUnknownProperty(t *testing.T) {
	input := strings.Replace(validJSON, `"publicRelease": false`, `"publicRelease": false, "futureValue": {"nested": true}`, 1)
	info, err := Parse([]byte(input))
	if err != nil {
		t.Fatal(err)
	}

	if info.SemVer != "1.2.3-beta.g0123456789+build.4" || info.VersionHeight != 7 || info.GitCommitID == nil {
		t.Fatalf("unexpected value: %#v", info)
	}
}

func TestExecutableResolution(t *testing.T) {
	t.Setenv("NBGV_EXECUTABLE", "from-environment")
	if got := Executable("explicit"); got != "explicit" {
		t.Fatalf("explicit executable was %q", got)
	}

	if got := Executable(""); got != "from-environment" {
		t.Fatalf("environment executable was %q", got)
	}

	t.Setenv("NBGV_EXECUTABLE", "")
	if got := Executable(""); got != "nbgv" {
		t.Fatalf("PATH command name was %q", got)
	}
}

func TestParseAllowsNullGitProperties(t *testing.T) {
	input := strings.Replace(validJSON, `"gitCommitId": "0123456789abcdef"`, `"gitCommitId": null`, 1)
	input = strings.Replace(input, `"gitCommitIdShort": "0123456789"`, `"gitCommitIdShort": null`, 1)
	input = strings.Replace(input, `"gitCommitDate": "2026-08-09T21:59:55Z"`, `"gitCommitDate": null`, 1)
	info, err := Parse([]byte(input))
	if err != nil {
		t.Fatal(err)
	}

	if info.GitCommitID != nil || info.GitCommitIDShort != nil || info.GitCommitDate != nil {
		t.Fatalf("expected nil Git values: %#v", info)
	}
}

func TestParseAllowsIndependentlyNullGitProperties(t *testing.T) {
	input := strings.Replace(validJSON, `"gitCommitDate": "2026-08-09T21:59:55Z"`, `"gitCommitDate": null`, 1)
	info, err := Parse([]byte(input))
	if err != nil {
		t.Fatal(err)
	}

	if info.GitCommitID == nil || info.GitCommitIDShort == nil || info.GitCommitDate != nil {
		t.Fatalf("unexpected Git values: %#v", info)
	}
}

func TestParseAllowsEmptyShortCommitID(t *testing.T) {
	input := strings.Replace(validJSON, `"gitCommitIdShort": "0123456789"`, `"gitCommitIdShort": ""`, 1)
	info, err := Parse([]byte(input))
	if err != nil {
		t.Fatal(err)
	}

	if info.GitCommitIDShort == nil || *info.GitCommitIDShort != "" {
		t.Fatalf("unexpected short Git ID: %#v", info.GitCommitIDShort)
	}
}

func TestParseRejectsInvalidContracts(t *testing.T) {
	tests := map[string]string{
		"unsupported schema": strings.Replace(validJSON, `"schemaVersion": 1`, `"schemaVersion": 2`, 1),
		"fractional schema":  strings.Replace(validJSON, `"schemaVersion": 1`, `"schemaVersion": 1.0`, 1),
		"string schema":      strings.Replace(validJSON, `"schemaVersion": 1`, `"schemaVersion": "1"`, 1),
		"duplicate property": strings.Replace(validJSON, `"schemaVersion": 1`, `"schemaVersion": 1, "schemaVersion": 1`, 1),
		"missing property":   strings.Replace(validJSON, `"versionHeight": 7,`, "", 1),
		"wrong type":         strings.Replace(validJSON, `"publicRelease": false`, `"publicRelease": "false"`, 1),
		"null boolean":       strings.Replace(validJSON, `"publicRelease": false`, `"publicRelease": null`, 1),
		"null string":        strings.Replace(validJSON, `"version": "1.2.3.4"`, `"version": null`, 1),
		"invalid semver":     strings.Replace(validJSON, `"1.2.3-beta.g0123456789+build.4"`, `"1.2"`, 1),
		"leading zero":       strings.Replace(validJSON, `"1.2.3-beta.g0123456789+build.4"`, `"1.2.3-01"`, 1),
		"uppercase commit":   strings.Replace(validJSON, `"0123456789abcdef"`, `"0123456789ABCDEF"`, 1),
		"non-UTC date":       strings.Replace(validJSON, `"2026-08-09T21:59:55Z"`, `"2026-08-09T15:59:55-06:00"`, 1),
		"trailing JSON":      validJSON + `{}`,
	}

	for name, input := range tests {
		t.Run(name, func(t *testing.T) {
			if _, err := Parse([]byte(input)); err == nil {
				t.Fatal("expected an error")
			}
		})
	}
}

type parityManifest struct {
	Executable string           `json:"executable"`
	Scenarios  []parityScenario `json:"scenarios"`
}

type parityScenario struct {
	Name      string `json:"name"`
	Directory string `json:"directory"`
	Oracle    string `json:"oracle"`
	GitEngine string `json:"gitEngine"`
}

func TestSharedGitFixtureMatchesDotNetOracleByteForByte(t *testing.T) {
	manifestPath := os.Getenv("NBGV_PARITY_MANIFEST")
	if manifestPath == "" {
		t.Skip("NBGV_PARITY_MANIFEST is not set; shared parity fixture was not requested")
	}

	content, err := os.ReadFile(manifestPath)
	if err != nil {
		t.Fatal(err)
	}

	var manifest parityManifest
	if err := json.Unmarshal(content, &manifest); err != nil {
		t.Fatal(err)
	}

	for _, scenario := range manifest.Scenarios {
		scenario := scenario
		t.Run(scenario.Name, func(t *testing.T) {
			if !hasGitMarker(scenario.Directory) {
				t.Fatalf("%q is not backed by a real Git repository", scenario.Directory)
			}

			t.Setenv("NBGV_GitEngine", scenario.GitEngine)
			oracleJSON, err := os.ReadFile(scenario.Oracle)
			if err != nil {
				t.Fatal(err)
			}

			expected, err := Parse(oracleJSON)
			if err != nil {
				t.Fatalf("parse .NET oracle: %v", err)
			}

			actual, err := Get(manifest.Executable, scenario.Directory, io.Discard)
			if err != nil {
				t.Fatal(err)
			}

			expectedBytes, err := json.Marshal(expected)
			if err != nil {
				t.Fatal(err)
			}
			actualBytes, err := json.Marshal(actual)
			if err != nil {
				t.Fatal(err)
			}
			if !bytes.Equal(actualBytes, expectedBytes) {
				t.Fatalf("adapter fields differ from .NET oracle:\nactual: %s\noracle: %s", actualBytes, expectedBytes)
			}
		})
	}
}

func hasGitMarker(directory string) bool {
	for {
		if _, err := os.Stat(filepath.Join(directory, ".git")); err == nil {
			return true
		}

		parent := filepath.Dir(directory)
		if parent == directory {
			return false
		}

		directory = parent
	}
}
