package version

import "testing"

func TestCurrentPrefersLinkerValues(t *testing.T) {
	previousSemVer := semVer
	previousVersion := version
	previousHeight := versionHeight
	previousPublic := publicRelease
	t.Cleanup(func() {
		semVer = previousSemVer
		version = previousVersion
		versionHeight = previousHeight
		publicRelease = previousPublic
	})

	semVer = "1.2.3"
	version = "1.2.3.4"
	versionHeight = "4"
	publicRelease = "true"
	info := Current()
	if info.Source != SourceLinker || info.SemVer != "1.2.3" || info.VersionHeight != 4 || !info.PublicRelease {
		t.Fatalf("unexpected info: %#v", info)
	}
}

func TestRegisterGenerated(t *testing.T) {
	generatedMu.Lock()
	previous := generatedInfo
	generatedInfo = nil
	generatedMu.Unlock()
	t.Cleanup(func() {
		generatedMu.Lock()
		generatedInfo = previous
		generatedMu.Unlock()
	})

	RegisterGenerated(Info{SemVer: "2.0.0"})
	info := Current()
	if info.Source != SourceGenerated || info.SemVer != "2.0.0" {
		t.Fatalf("unexpected info: %#v", info)
	}
}
