// Package version exposes Nerdbank.GitVersioning information embedded by
// nbgv-go. Programs should call Current rather than reading build settings
// directly.
package version

import (
	"runtime/debug"
	"strconv"
	"strings"
	"sync"
)

var (
	semVer           string
	version          string
	versionHeight    string
	gitCommitID      string
	gitCommitIDShort string
	gitCommitDate    string
	publicRelease    string

	generatedMu   sync.RWMutex
	generatedInfo *Info
)

// Source identifies how Current obtained version information.
type Source string

const (
	// SourceUnknown means no usable version information was available.
	SourceUnknown Source = ""

	// SourceLinker means nbgv-go embedded the information with linker flags.
	SourceLinker Source = "linker"

	// SourceGenerated means nbgv-go generate embedded the information in source.
	SourceGenerated Source = "generated"

	// SourceBuildInfo means Current fell back to runtime/debug.BuildInfo.
	SourceBuildInfo Source = "build-info"
)

// Info describes the version of the running program.
type Info struct {
	SemVer           string
	Version          string
	VersionHeight    int
	GitCommitID      string
	GitCommitIDShort string
	GitCommitDate    string
	PublicRelease    bool
	Source           Source
	Modified         bool
}

// Current returns linker-stamped or generated Nerdbank.GitVersioning
// information. If neither is present, it returns the module and VCS values
// available from runtime/debug.BuildInfo.
func Current() Info {
	if semVer != "" {
		height, _ := strconv.Atoi(versionHeight)
		return Info{
			SemVer:           semVer,
			Version:          version,
			VersionHeight:    height,
			GitCommitID:      gitCommitID,
			GitCommitIDShort: gitCommitIDShort,
			GitCommitDate:    gitCommitDate,
			PublicRelease:    publicRelease == "true",
			Source:           SourceLinker,
		}
	}

	generatedMu.RLock()
	generated := generatedInfo
	if generated != nil {
		copy := *generated
		generatedMu.RUnlock()
		copy.Source = SourceGenerated
		return copy
	}

	generatedMu.RUnlock()
	return fromBuildInfo()
}

// RegisterGenerated registers information emitted by nbgv-go generate.
// Applications normally call this only from generated source.
func RegisterGenerated(info Info) {
	copy := info
	copy.Source = SourceGenerated
	generatedMu.Lock()
	generatedInfo = &copy
	generatedMu.Unlock()
}

func fromBuildInfo() Info {
	buildInfo, ok := debug.ReadBuildInfo()
	if !ok {
		return Info{}
	}

	result := Info{Source: SourceBuildInfo}
	if buildInfo.Main.Version != "" && buildInfo.Main.Version != "(devel)" {
		result.SemVer = strings.TrimPrefix(buildInfo.Main.Version, "v")
	}

	for _, setting := range buildInfo.Settings {
		switch setting.Key {
		case "vcs.revision":
			result.GitCommitID = setting.Value
			if len(setting.Value) > 10 {
				result.GitCommitIDShort = setting.Value[:10]
			} else {
				result.GitCommitIDShort = setting.Value
			}
		case "vcs.time":
			result.GitCommitDate = setting.Value
		case "vcs.modified":
			result.Modified = setting.Value == "true"
		}
	}

	return result
}
