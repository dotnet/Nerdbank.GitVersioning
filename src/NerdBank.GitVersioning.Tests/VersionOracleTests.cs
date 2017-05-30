using System.IO;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.Tests;
using Xunit;
using Xunit.Abstractions;

public class VersionOracleTests : RepoTestBase
{
    public VersionOracleTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void Submodule_RecognizedWithCorrectVersion()
    {
        using (var expandedRepo = TestUtilities.ExtractRepoArchive("submodules"))
        {
            this.Repo = new Repository(expandedRepo.RepoPath);

            var oracleA = VersionOracle.Create(Path.Combine(expandedRepo.RepoPath, "a"));
            Assert.Equal("1.3.1", oracleA.SimpleVersion.ToString());
            Assert.Equal("e238b03e75", oracleA.GitCommitIdShort);

            var oracleB = VersionOracle.Create(Path.Combine(expandedRepo.RepoPath, "b", "projB"));
            Assert.Equal("2.5.2", oracleB.SimpleVersion.ToString());
            Assert.Equal("3ea7f010c3", oracleB.GitCommitIdShort);
        }
    }

    [Fact]
    public void MajorMinorPrereleaseBuildMetadata()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8-beta.3+metadata.4"),
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        Assert.Equal("7.8", oracle.MajorMinorVersion.ToString());
        Assert.Equal(oracle.VersionHeight, oracle.BuildNumber);

        Assert.Equal("-beta.3", oracle.PrereleaseVersion);
        ////Assert.Equal("+metadata.4", oracle.BuildMetadataFragment);

        Assert.Equal(1, oracle.VersionHeight);
        Assert.Equal(0, oracle.VersionHeightOffset);
    }

    [Fact]
    public void MajorMinorBuildPrereleaseBuildMetadata()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-beta.3+metadata.4"),
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        Assert.Equal("7.8", oracle.MajorMinorVersion.ToString());
        Assert.Equal(9, oracle.BuildNumber);
        Assert.Equal(oracle.VersionHeight + oracle.VersionHeightOffset, oracle.Version.Revision);

        Assert.Equal("-beta.3", oracle.PrereleaseVersion);
        ////Assert.Equal("+metadata.4", oracle.BuildMetadataFragment);

        Assert.Equal(1, oracle.VersionHeight);
        Assert.Equal(0, oracle.VersionHeightOffset);
    }

    [Fact]
    public void HeightInPrerelease()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-beta.{height}.foo"),
            BuildNumberOffset = 2,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        Assert.Equal("7.8", oracle.MajorMinorVersion.ToString());
        Assert.Equal(9, oracle.BuildNumber);
        Assert.Equal(oracle.VersionHeight + oracle.VersionHeightOffset, oracle.Version.Revision);

        Assert.Equal("-beta." + (oracle.VersionHeight + oracle.VersionHeightOffset) + ".foo", oracle.PrereleaseVersion);

        Assert.Equal(1, oracle.VersionHeight);
        Assert.Equal(2, oracle.VersionHeightOffset);
    }

    [Fact(Skip = "Build metadata not yet retained from version.json")]
    public void HeightInBuildMetadata()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-beta+another.{height}.foo"),
            BuildNumberOffset = 2,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        Assert.Equal("7.8", oracle.MajorMinorVersion.ToString());
        Assert.Equal(9, oracle.BuildNumber);
        Assert.Equal(oracle.VersionHeight + oracle.VersionHeightOffset, oracle.Version.Revision);

        Assert.Equal("-beta", oracle.PrereleaseVersion);
        Assert.Equal("+another." + (oracle.VersionHeight + oracle.VersionHeightOffset) + ".foo", oracle.BuildMetadataFragment);

        Assert.Equal(1, oracle.VersionHeight);
        Assert.Equal(2, oracle.VersionHeightOffset);
    }

    [Theory]
    [InlineData("7.8.9-foo.25", "7.8.9-foo-0025")]
    [InlineData("7.8.9-foo.25s", "7.8.9-foo-25s")]
    [InlineData("7.8.9-foo.s25", "7.8.9-foo-s25")]
    [InlineData("7.8.9-foo.25.bar-24.13-11", "7.8.9-foo-0025-bar-24-13-11")]
    [InlineData("7.8.9-25.bar.baz-25", "7.8.9-0025-bar-baz-25")]
    [InlineData("7.8.9-foo.5.bar.1.43.baz", "7.8.9-foo-0005-bar-0001-0043-baz")]
    public void SemVer1PrereleaseConversion(string semVer2, string semVer1)
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse(semVer2),
            BuildNumberOffset = 2,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        oracle.PublicRelease = true;
        Assert.Equal(semVer1, oracle.SemVer1);
    }

    [Fact]
    public void SemVer1PrereleaseConversionPadding()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            BuildNumberOffset = 2,
            SemVer1NumericIdentifierPadding = 3,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        oracle.PublicRelease = true;
        Assert.Equal("7.8.9-foo-025", oracle.SemVer1);
    }
}
