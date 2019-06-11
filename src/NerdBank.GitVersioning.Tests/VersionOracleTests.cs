using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Xunit;
using Xunit.Abstractions;

public class VersionOracleTests : RepoTestBase
{
    public VersionOracleTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    private string CommitIdShort => this.Repo.Head.Commits.First().Id.Sha.Substring(0, VersionOptions.DefaultGitCommitIdShortFixedLength);

    [Fact]
    public void NotRepo()
    {
        // Seems safe to assume the system directory is not a repository.
        var oracle = VersionOracle.Create(Environment.SystemDirectory);
        Assert.Equal(0, oracle.VersionHeight);
    }

    [Fact(Skip = "Unstable test. See issue #125")]
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

    [Fact]
    public void SemVerStableNonPublicVersion()
    {
        var workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("2.3"),
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        oracle.PublicRelease = false;
        Assert.Matches(@"^2.3.1-[^g]{10}$", oracle.SemVer1);
        Assert.Matches(@"^2.3.1-g[a-f0-9]{10}$", oracle.SemVer2);
        Assert.Matches(@"^2.3.1-g[a-f0-9]{10}$", oracle.NuGetPackageVersion);
        Assert.Matches(@"^2.3.1-g[a-f0-9]{10}$", oracle.ChocolateyPackageVersion);
    }

    [Fact]
    public void SemVerStableNonPublicVersionShortened()
    {
        var workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("2.3"),
            GitCommitIdShortFixedLength = 7
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        oracle.PublicRelease = false;
        Assert.Matches(@"^2.3.1-[^g]{7}$", oracle.SemVer1);
        Assert.Matches(@"^2.3.1-g[a-f0-9]{7}$", oracle.SemVer2);
        Assert.Matches(@"^2.3.1-g[a-f0-9]{7}$", oracle.NuGetPackageVersion);
        Assert.Matches(@"^2.3.1-g[a-f0-9]{7}$", oracle.ChocolateyPackageVersion);
    }

    [Fact]
    public void DefaultNuGetPackageVersionIsSemVer1PublicRelease()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            SemVer1NumericIdentifierPadding = 2,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        oracle.PublicRelease = true;
        Assert.Equal($"7.8.9-foo-25", oracle.NuGetPackageVersion);
    }

    [Fact]
    public void DefaultNuGetPackageVersionIsSemVer1NonPublicRelease()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            SemVer1NumericIdentifierPadding = 2,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        oracle.PublicRelease = false;
        Assert.Equal($"7.8.9-foo-25-g{this.CommitIdShort}", oracle.NuGetPackageVersion);
    }

    [Fact]
    public void NpmPackageVersionIsSemVer2()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            SemVer1NumericIdentifierPadding = 2
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        oracle.PublicRelease = true;
        Assert.Equal("7.8.9-foo.25", oracle.NpmPackageVersion);
    }

    [Fact]
    public void CanSetSemVer2ForNuGetPackageVersionPublicRelease()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            NuGetPackageVersion = new VersionOptions.NuGetPackageVersionOptions
            {
                SemVer = 2,
            }
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        oracle.PublicRelease = true;
        Assert.Equal($"7.8.9-foo.25", oracle.NuGetPackageVersion);
    }

    [Fact]
    public void CanSetSemVer2ForNuGetPackageVersionNonPublicRelease()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-foo.25"),
            NuGetPackageVersion = new VersionOptions.NuGetPackageVersionOptions
            {
                SemVer = 2,
            }
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        oracle.PublicRelease = false;
        Assert.Equal($"7.8.9-foo.25.g{this.CommitIdShort}", oracle.NuGetPackageVersion);
    }

    [Fact]
    public void CanUseGitProjectRelativePathWithGitRepoRoot()
    {
        VersionOptions rootVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.1"),
        };

        VersionOptions projectVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("2.2"),
        };

        string childProjectRelativeDir = "ChildProject1";
        string childProjectAbsoluteDir = Path.Combine(this.RepoPath, childProjectRelativeDir);
        this.WriteVersionFile(rootVersion);
        this.WriteVersionFile(projectVersion, childProjectRelativeDir);

        this.InitializeSourceControl();

        // Check Root Version. Root version will be used
        var oracle = VersionOracle.Create(this.RepoPath, this.RepoPath, null, null);
        Assert.Equal("1.1", oracle.MajorMinorVersion.ToString());

        // Check ChildProject with projectRelativeDir, with version file. Child project version will be used.
        oracle = VersionOracle.Create(childProjectAbsoluteDir, this.RepoPath, null, null, childProjectRelativeDir);
        Assert.Equal("2.2", oracle.MajorMinorVersion.ToString());

        // Check ChildProject withOUT projectRelativeDir, with Version file. Child project version will be used.
        oracle = VersionOracle.Create(childProjectAbsoluteDir, this.RepoPath);
        Assert.Equal("2.2", oracle.MajorMinorVersion.ToString());

        // Check ChildProject withOUT Version file. Root version will be used.
        oracle = VersionOracle.Create(Path.Combine(this.RepoPath, "otherChildProject"), this.RepoPath, null, null, "otherChildProject");
        Assert.Equal("1.1", oracle.MajorMinorVersion.ToString());
    }

    [Fact]
    public void VersionJsonWithoutVersion()
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.JsonFileName), "{}");
        this.InitializeSourceControl();
        var oracle = VersionOracle.Create(this.RepoPath);
        Assert.Equal(0, oracle.Version.Major);
        Assert.Equal(0, oracle.Version.Minor);
    }

    [Fact]
    public void VersionJsonWithSingleIntegerForVersion()
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.JsonFileName), @"{""version"":""3""}");
        this.InitializeSourceControl();
        var ex = Assert.Throws<FormatException>(() => VersionOracle.Create(this.RepoPath));
        Assert.Contains(this.Repo.Head.Commits.First().Sha, ex.Message);
        Assert.Contains("\"3\"", ex.InnerException.Message);
        this.Logger.WriteLine(ex.ToString());
    }
}
