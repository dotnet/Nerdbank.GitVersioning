using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using NerdBank.GitVersioning;
using NerdBank.GitVersioning.Tests;
using Xunit;
using Xunit.Abstractions;
using Version = System.Version;

public class VersionFileTests : RepoTestBase
{
    private string versionTxtPath;

    public VersionFileTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.versionTxtPath = Path.Combine(this.RepoPath, VersionFile.FileName);
    }

    [Fact]
    public void IsVersionTxtPresent_NullCommit()
    {
        Assert.False(VersionFile.IsVersionFilePresent(null));
    }

    [Fact]
    public void IsVersionTxtPresent()
    {
        this.InitializeSourceControl();
        this.AddCommits();
        Assert.False(VersionFile.IsVersionFilePresent(this.Repo.Head.Commits.First()));

        this.WriteVersionFile();

        // Verify that we can find the version.txt file in the most recent commit,
        // But not in the initial commit.
        Assert.True(VersionFile.IsVersionFilePresent(this.Repo.Head.Commits.First()));
        Assert.False(VersionFile.IsVersionFilePresent(this.Repo.Head.Commits.Last()));
    }

    [Theory]
    [InlineData("2.3", "")]
    [InlineData("2.3", null)]
    [InlineData("2.3", "-beta")]
    [InlineData("2.3.0", "")]
    [InlineData("2.3.0", "-rc")]
    public void WriteVersionFile_GetVersionFromTxtFile(string expectedVersion, string expectedPrerelease)
    {
        string pathWritten = VersionFile.WriteVersionFile(this.RepoPath, new Version(expectedVersion), expectedPrerelease);
        Assert.Equal(Path.Combine(this.RepoPath, VersionFile.FileName), pathWritten);

        string[] actualFileContent = File.ReadAllLines(this.versionTxtPath);
        this.Logger.WriteLine(string.Join(Environment.NewLine, actualFileContent));

        Assert.Equal(2, actualFileContent.Length);
        Assert.Equal(expectedVersion, actualFileContent[0]);
        Assert.Equal(expectedPrerelease ?? string.Empty, actualFileContent[1]);

        SemanticVersion actualVersion = VersionFile.GetVersionFromFile(this.RepoPath);

        Assert.Equal(new Version(expectedVersion), actualVersion.Version);
        Assert.Equal(expectedPrerelease ?? string.Empty, actualVersion.UnstableTag);
    }

    [Fact]
    public void GetVersionFromTxtFile_ViaCommit()
    {
        Assert.Null(VersionFile.GetVersionFromFile((Commit)null));

        this.InitializeSourceControl();
        this.WriteVersionFile();
        SemanticVersion fromCommit = VersionFile.GetVersionFromFile(this.Repo.Head.Commits.First());
        SemanticVersion fromFile = VersionFile.GetVersionFromFile(this.RepoPath);
        Assert.NotNull(fromCommit);
        Assert.Equal(fromFile, fromCommit);
    }
}
