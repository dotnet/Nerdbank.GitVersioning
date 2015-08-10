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
    public void IsVersionFilePresent_Commit_Null()
    {
        Assert.False(VersionFile.IsVersionFilePresent((Commit)null));
    }

    [Fact]
    public void IsVersionFilePresent_String_NullOrEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => VersionFile.IsVersionFilePresent((string)null));
        Assert.Throws<ArgumentException>(() => VersionFile.IsVersionFilePresent(string.Empty));
    }

    [Fact]
    public void IsVersionFilePresent_Commit()
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

    [Fact]
    public void IsVersionFilePresent_String_ConsiderAncestorFolders()
    {
        // Construct a repo where versions are defined like this:
        /*   root <- 1.0
                a             (inherits 1.0)
                    b <- 1.1
                         c    (inherits 1.1)
        */
        VersionFile.WriteVersionFile(this.RepoPath, new Version(1, 0));
        string subDirA = Path.Combine(this.RepoPath, "a");
        string subDirAB = Path.Combine(subDirA, "b");
        string subDirABC = Path.Combine(subDirAB, "c");
        Directory.CreateDirectory(subDirABC);
        VersionFile.WriteVersionFile(subDirAB, new Version(1, 1));

        Assert.True(VersionFile.IsVersionFilePresent(subDirABC));
        Assert.True(VersionFile.IsVersionFilePresent(subDirAB));
        Assert.True(VersionFile.IsVersionFilePresent(subDirA));
        Assert.True(VersionFile.IsVersionFilePresent(this.RepoPath));
    }

    [Theory]
    [InlineData("2.3", "")]
    [InlineData("2.3", null)]
    [InlineData("2.3", "-beta")]
    [InlineData("2.3.0", "")]
    [InlineData("2.3.0", "-rc")]
    public void WriteVersionFile_GetVersionFromFile(string expectedVersion, string expectedPrerelease)
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
    public void GetVersionFromFile_Commit()
    {
        Assert.Null(VersionFile.GetVersionFromFile((Commit)null));

        this.InitializeSourceControl();
        this.WriteVersionFile();
        SemanticVersion fromCommit = VersionFile.GetVersionFromFile(this.Repo.Head.Commits.First());
        SemanticVersion fromFile = VersionFile.GetVersionFromFile(this.RepoPath);
        Assert.NotNull(fromCommit);
        Assert.Equal(fromFile, fromCommit);
    }

    [Fact]
    public void GetVersionFromFile_String_FindsNearestFileInAncestorDirectories()
    {
        // Construct a repo where versions are defined like this:
        /*   root <- 1.0
                a             (inherits 1.0)
                    b <- 1.1
                         c    (inherits 1.1)
        */
        VersionFile.WriteVersionFile(this.RepoPath, new Version(1, 0));
        string subDirA = Path.Combine(this.RepoPath, "a");
        string subDirAB = Path.Combine(subDirA, "b");
        string subDirABC = Path.Combine(subDirAB, "c");
        Directory.CreateDirectory(subDirABC);
        VersionFile.WriteVersionFile(subDirAB, new Version(1, 1));
        this.InitializeSourceControl();
        var commit = this.Repo.Head.Commits.First();

        AssertPathHasVersion(commit, subDirABC, new Version(1, 1));
        AssertPathHasVersion(commit, subDirAB, new Version(1, 1));
        AssertPathHasVersion(commit, subDirA, new Version(1, 0));
        AssertPathHasVersion(commit, this.RepoPath, new Version(1, 0));
    }

    [Fact]
    public void GetVersionFromFile_String_MissingFile()
    {
        Assert.Null(VersionFile.GetVersionFromFile(this.RepoPath));
    }

    private void AssertPathHasVersion(Commit commit, string absolutePath, Version expected)
    {
        var actual = VersionFile.GetVersionFromFile(absolutePath)?.Version;
        Assert.Equal(expected, actual);

        // Pass in the repo-relative path to ensure the commit is used as the data source.
        string relativePath = absolutePath.Substring(this.RepoPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        actual = VersionFile.GetVersionFromFile(commit, relativePath)?.Version;
        Assert.Equal(expected, actual);
    }
}
