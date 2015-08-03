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

public class VersionTextFileTests : RepoTestBase
{
    private string versionTxtPath;

    public VersionTextFileTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.versionTxtPath = Path.Combine(this.RepoPath, VersionTextFile.FileName);
    }

    [Fact]
    public void IsVersionTxtPresent_NullCommit()
    {
        Assert.False(VersionTextFile.IsVersionFilePresent(null));
    }

    [Fact]
    public void IsVersionTxtPresent()
    {
        this.InitializeSourceControl();
        this.AddCommits();
        Assert.False(VersionTextFile.IsVersionFilePresent(this.Repo.Head.Commits.First()));

        this.WriteVersionFile();

        // Verify that we can find the version.txt file in the most recent commit,
        // But not in the initial commit.
        Assert.True(VersionTextFile.IsVersionFilePresent(this.Repo.Head.Commits.First()));
        Assert.False(VersionTextFile.IsVersionFilePresent(this.Repo.Head.Commits.Last()));
    }

    [Theory]
    [InlineData("2.3", "")]
    [InlineData("2.3", null)]
    [InlineData("2.3", "-beta")]
    [InlineData("2.3.0", "")]
    [InlineData("2.3.0", "-rc")]
    public void WriteVersionFile_GetVersionFromTxtFile(string expectedVersion, string expectedPrerelease)
    {
        string pathWritten = VersionTextFile.WriteVersionFile(this.RepoPath, new Version(expectedVersion), expectedPrerelease);
        Assert.Equal(Path.Combine(this.RepoPath, VersionTextFile.FileName), pathWritten);

        string[] actualFileContent = File.ReadAllLines(this.versionTxtPath);
        this.Logger.WriteLine(string.Join(Environment.NewLine, actualFileContent));

        Assert.Equal(2, actualFileContent.Length);
        Assert.Equal(expectedVersion, actualFileContent[0]);
        Assert.Equal(expectedPrerelease ?? string.Empty, actualFileContent[1]);

        SemanticVersion actualVersion = VersionTextFile.GetVersionFromFile(this.RepoPath);

        Assert.Equal(new Version(expectedVersion), actualVersion.Version);
        Assert.Equal(expectedPrerelease ?? string.Empty, actualVersion.UnstableTag);
    }

    [Fact]
    public void GetVersionFromTxtFile_ViaCommit()
    {
        Assert.Null(VersionTextFile.GetVersionFromFile((Commit)null));

        this.InitializeSourceControl();
        this.WriteVersionFile();
        SemanticVersion fromCommit = VersionTextFile.GetVersionFromFile(this.Repo.Head.Commits.First());
        SemanticVersion fromFile = VersionTextFile.GetVersionFromFile(this.RepoPath);
        Assert.NotNull(fromCommit);
        Assert.Equal(fromFile, fromCommit);
    }
}
