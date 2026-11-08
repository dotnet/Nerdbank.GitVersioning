// Package adapter reads the nbgv build adapter contract.
package adapter

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"regexp"
	"strings"
	"time"
)

const schemaVersion = 1

var (
	semVerPattern  = regexp.MustCompile(`^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$`)
	versionPattern = regexp.MustCompile(`^[0-9]+(?:\.[0-9]+){1,3}$`)
	commitPattern  = regexp.MustCompile(`^[0-9a-f]+(?:-dirty)?$`)
)

// Info is the validated schema version 1 adapter value.
type Info struct {
	SemVer           string
	Version          string
	VersionHeight    int
	GitCommitID      *string
	GitCommitIDShort *string
	GitCommitDate    *string
	PublicRelease    bool
}

// Executable returns the nbgv executable selected according to the adapter
// resolution policy.
func Executable(explicit string) string {
	if explicit != "" {
		return explicit
	}

	if fromEnvironment := os.Getenv("NBGV_EXECUTABLE"); fromEnvironment != "" {
		return fromEnvironment
	}

	return "nbgv"
}

// Get invokes nbgv directly and reads its adapter result.
func Get(executable, projectDirectory string, diagnostics io.Writer) (Info, error) {
	command := exec.Command(executable, "get-version", "--format", "adapter")
	command.Dir = projectDirectory
	command.Stderr = diagnostics
	var stdout bytes.Buffer
	command.Stdout = &stdout
	if err := command.Run(); err != nil {
		return Info{}, fmt.Errorf("run %q: %w", executable, err)
	}

	result, err := Parse(stdout.Bytes())
	if err != nil {
		return Info{}, fmt.Errorf("%q returned invalid adapter JSON: %w", executable, err)
	}

	return result, nil
}

// Parse strictly validates a schema version 1 adapter JSON object. Unknown
// properties are ignored for forward compatibility.
func Parse(data []byte) (Info, error) {
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.UseNumber()
	firstToken, err := decoder.Token()
	if err != nil {
		return Info{}, fmt.Errorf("decode JSON object: %w", err)
	}

	if delimiter, ok := firstToken.(json.Delim); !ok || delimiter != '{' {
		return Info{}, errors.New("expected a JSON object")
	}

	properties := make(map[string]json.RawMessage)
	for decoder.More() {
		nameToken, err := decoder.Token()
		if err != nil {
			return Info{}, fmt.Errorf("decode property name: %w", err)
		}

		name, ok := nameToken.(string)
		if !ok {
			return Info{}, errors.New("expected a property name")
		}

		if _, duplicate := properties[name]; duplicate {
			return Info{}, fmt.Errorf("duplicate property %q", name)
		}

		var value json.RawMessage
		if err := decoder.Decode(&value); err != nil {
			return Info{}, fmt.Errorf("decode property %q: %w", name, err)
		}

		properties[name] = value
	}

	if _, err := decoder.Token(); err != nil {
		return Info{}, fmt.Errorf("decode JSON object: %w", err)
	}

	var extra json.RawMessage
	if err := decoder.Decode(&extra); !errors.Is(err, io.EOF) {
		if err == nil {
			return Info{}, errors.New("unexpected content after JSON object")
		}

		return Info{}, fmt.Errorf("unexpected content after JSON object: %w", err)
	}

	actualSchemaVersion, err := requiredInt(properties, "schemaVersion")
	if err != nil {
		return Info{}, err
	}

	if actualSchemaVersion != schemaVersion {
		return Info{}, fmt.Errorf("unsupported schemaVersion %d; supported version is %d", actualSchemaVersion, schemaVersion)
	}

	semVer, err := requiredString(properties, "semVer2")
	if err != nil {
		return Info{}, err
	}

	if !validSemVer(semVer) {
		return Info{}, errors.New(`property "semVer2" is not a valid SemVer 2.0 version`)
	}

	version, err := requiredString(properties, "version")
	if err != nil {
		return Info{}, err
	}

	if !versionPattern.MatchString(version) {
		return Info{}, errors.New(`property "version" must have two to four non-negative integer components`)
	}

	versionHeight, err := requiredInt(properties, "versionHeight")
	if err != nil {
		return Info{}, err
	}

	if versionHeight < 0 {
		return Info{}, errors.New(`property "versionHeight" must be non-negative`)
	}

	gitCommitID, err := nullableString(properties, "gitCommitId")
	if err != nil {
		return Info{}, err
	}

	gitCommitIDShort, err := nullableString(properties, "gitCommitIdShort")
	if err != nil {
		return Info{}, err
	}

	gitCommitDate, err := nullableString(properties, "gitCommitDate")
	if err != nil {
		return Info{}, err
	}

	if gitCommitID != nil {
		if !commitPattern.MatchString(*gitCommitID) {
			return Info{}, errors.New(`property "gitCommitId" must be lowercase hexadecimal with an optional "-dirty" suffix`)
		}
	}

	if gitCommitIDShort != nil {
		if *gitCommitIDShort != "" && *gitCommitIDShort != "-dirty" && !commitPattern.MatchString(*gitCommitIDShort) {
			return Info{}, errors.New(`property "gitCommitIdShort" must be lowercase hexadecimal with an optional "-dirty" suffix`)
		}
	}

	if gitCommitID != nil && gitCommitIDShort != nil {
		if !strings.HasPrefix(strings.TrimSuffix(*gitCommitID, "-dirty"), strings.TrimSuffix(*gitCommitIDShort, "-dirty")) {
			return Info{}, errors.New(`property "gitCommitIdShort" must be a prefix of "gitCommitId"`)
		}
	}

	if gitCommitDate != nil {
		parsedDate, parseError := time.Parse("2006-01-02T15:04:05Z", *gitCommitDate)
		if parseError != nil || parsedDate.Format("2006-01-02T15:04:05Z") != *gitCommitDate {
			return Info{}, errors.New(`property "gitCommitDate" must be an RFC 3339 UTC timestamp in whole seconds`)
		}
	}

	publicRelease, err := requiredBool(properties, "publicRelease")
	if err != nil {
		return Info{}, err
	}

	return Info{
		SemVer:           semVer,
		Version:          version,
		VersionHeight:    versionHeight,
		GitCommitID:      gitCommitID,
		GitCommitIDShort: gitCommitIDShort,
		GitCommitDate:    gitCommitDate,
		PublicRelease:    publicRelease,
	}, nil
}

func validSemVer(value string) bool {
	if !semVerPattern.MatchString(value) {
		return false
	}

	if metadataStart := strings.IndexByte(value, '+'); metadataStart >= 0 {
		value = value[:metadataStart]
	}
	prereleaseStart := strings.IndexByte(value, '-')
	if prereleaseStart < 0 {
		return true
	}

	prerelease := value[prereleaseStart+1:]
	if metadataStart := strings.IndexByte(prerelease, '+'); metadataStart >= 0 {
		prerelease = prerelease[:metadataStart]
	}

	for _, identifier := range strings.Split(prerelease, ".") {
		if len(identifier) > 1 && identifier[0] == '0' {
			numeric := true
			for _, character := range identifier {
				if character < '0' || character > '9' {
					numeric = false
					break
				}
			}

			if numeric {
				return false
			}
		}
	}

	return true
}

func requiredInt(properties map[string]json.RawMessage, name string) (int, error) {
	raw, ok := properties[name]
	if !ok {
		return 0, fmt.Errorf("missing required property %q", name)
	}

	decoder := json.NewDecoder(bytes.NewReader(raw))
	decoder.UseNumber()
	var decoded any
	if err := decoder.Decode(&decoded); err != nil {
		return 0, fmt.Errorf("property %q must be an integer", name)
	}

	number, ok := decoded.(json.Number)
	if !ok {
		return 0, fmt.Errorf("property %q must be an integer", name)
	}

	value, err := number.Int64()
	if err != nil {
		return 0, fmt.Errorf("property %q must be an integer", name)
	}

	converted := int(value)
	if int64(converted) != value {
		return 0, fmt.Errorf("property %q is outside the supported integer range", name)
	}

	return converted, nil
}

func requiredString(properties map[string]json.RawMessage, name string) (string, error) {
	raw, ok := properties[name]
	if !ok {
		return "", fmt.Errorf("missing required property %q", name)
	}

	var decoded any
	if err := json.Unmarshal(raw, &decoded); err != nil {
		return "", fmt.Errorf("property %q must be a string", name)
	}

	value, ok := decoded.(string)
	if !ok {
		return "", fmt.Errorf("property %q must be a string", name)
	}

	return value, nil
}

func nullableString(properties map[string]json.RawMessage, name string) (*string, error) {
	raw, ok := properties[name]
	if !ok {
		return nil, fmt.Errorf("missing required property %q", name)
	}

	if bytes.Equal(bytes.TrimSpace(raw), []byte("null")) {
		return nil, nil
	}

	value, err := requiredString(properties, name)
	if err != nil {
		return nil, fmt.Errorf("property %q must be a string or null", name)
	}

	return &value, nil
}

func requiredBool(properties map[string]json.RawMessage, name string) (bool, error) {
	raw, ok := properties[name]
	if !ok {
		return false, fmt.Errorf("missing required property %q", name)
	}

	var decoded any
	if err := json.Unmarshal(raw, &decoded); err != nil {
		return false, fmt.Errorf("property %q must be a boolean", name)
	}

	value, ok := decoded.(bool)
	if !ok {
		return false, fmt.Errorf("property %q must be a boolean", name)
	}

	return value, nil
}
