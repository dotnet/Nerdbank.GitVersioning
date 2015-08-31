using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.Tests;
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
    public void IsVersionDefined_Commit_Null()
    {
        Assert.False(VersionFile.IsVersionDefined((Commit)null));
    }

    [Fact]
    public void IsVersionDefined_String_NullOrEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => VersionFile.IsVersionDefined((string)null));
        Assert.Throws<ArgumentException>(() => VersionFile.IsVersionDefined(string.Empty));
    }

    [Fact]
    public void IsVersionDefined_Commit()
    {
        this.InitializeSourceControl();
        this.AddCommits();
        Assert.False(VersionFile.IsVersionDefined(this.Repo.Head.Commits.First()));

        this.WriteVersionFile();

        // Verify that we can find the version.txt file in the most recent commit,
        // But not in the initial commit.
        Assert.True(VersionFile.IsVersionDefined(this.Repo.Head.Commits.First()));
        Assert.False(VersionFile.IsVersionDefined(this.Repo.Head.Commits.Last()));
    }

    [Fact]
    public void IsVersionDefined_String_ConsiderAncestorFolders()
    {
        // Construct a repo where versions are defined like this:
        /*   root <- 1.0
                a             (inherits 1.0)
                    b <- 1.1
                         c    (inherits 1.1)
        */
        VersionFile.SetVersion(this.RepoPath, new Version(1, 0));
        string subDirA = Path.Combine(this.RepoPath, "a");
        string subDirAB = Path.Combine(subDirA, "b");
        string subDirABC = Path.Combine(subDirAB, "c");
        Directory.CreateDirectory(subDirABC);
        VersionFile.SetVersion(subDirAB, new Version(1, 1));

        Assert.True(VersionFile.IsVersionDefined(subDirABC));
        Assert.True(VersionFile.IsVersionDefined(subDirAB));
        Assert.True(VersionFile.IsVersionDefined(subDirA));
        Assert.True(VersionFile.IsVersionDefined(this.RepoPath));
    }

    [Theory]
    [InlineData("2.3", "")]
    [InlineData("2.3", null)]
    [InlineData("2.3", "-beta")]
    [InlineData("2.3.0", "")]
    [InlineData("2.3.0", "-rc")]
    public void SetVersion_GetVersionFromFile(string expectedVersion, string expectedPrerelease)
    {
        string pathWritten = VersionFile.SetVersion(this.RepoPath, new Version(expectedVersion), expectedPrerelease);
        Assert.Equal(Path.Combine(this.RepoPath, VersionFile.FileName), pathWritten);

        string[] actualFileContent = File.ReadAllLines(this.versionTxtPath);
        this.Logger.WriteLine(string.Join(Environment.NewLine, actualFileContent));

        Assert.Equal(2, actualFileContent.Length);
        Assert.Equal(expectedVersion, actualFileContent[0]);
        Assert.Equal(expectedPrerelease ?? string.Empty, actualFileContent[1]);

        SemanticVersion actualVersion = VersionFile.GetVersion(this.RepoPath);

        Assert.Equal(new Version(expectedVersion), actualVersion.Version);
        Assert.Equal(expectedPrerelease ?? string.Empty, actualVersion.UnstableTag);
    }

    [Theory]
    [InlineData(new[] { "2.3" }, "2.3")]
    [InlineData(new[] { "2.3", "-beta" }, "2.3-beta")]
    [InlineData(new[] { "2.3-alpha" }, "2.3-alpha")]
    [InlineData(new[] { " 2 . 3  - unstable  " }, "2.3-unstable")]
    public void GetVersionFromFile(string[] file, string expectedVersion)
    {
        File.WriteAllLines(Path.Combine(this.RepoPath, VersionFile.FileName), file);
        var version = VersionFile.GetVersion(this.RepoPath);
        Assert.Equal(version.ToString(), expectedVersion);
    }

    [Fact]
    public void GetVersion_Commit()
    {
        Assert.Null(VersionFile.GetVersion((Commit)null));

        this.InitializeSourceControl();
        this.WriteVersionFile();
        SemanticVersion fromCommit = VersionFile.GetVersion(this.Repo.Head.Commits.First());
        SemanticVersion fromFile = VersionFile.GetVersion(this.RepoPath);
        Assert.NotNull(fromCommit);
        Assert.Equal(fromFile, fromCommit);
    }

    [Fact]
    public void GetVersion_String_FindsNearestFileInAncestorDirectories()
    {
        // Construct a repo where versions are defined like this:
        /*   root <- 1.0
                a             (inherits 1.0)
                    b <- 1.1
                         c    (inherits 1.1)
        */
        VersionFile.SetVersion(this.RepoPath, new Version(1, 0));
        string subDirA = Path.Combine(this.RepoPath, "a");
        string subDirAB = Path.Combine(subDirA, "b");
        string subDirABC = Path.Combine(subDirAB, "c");
        Directory.CreateDirectory(subDirABC);
        VersionFile.SetVersion(subDirAB, new Version(1, 1));
        this.InitializeSourceControl();
        var commit = this.Repo.Head.Commits.First();

        AssertPathHasVersion(commit, subDirABC, new Version(1, 1));
        AssertPathHasVersion(commit, subDirAB, new Version(1, 1));
        AssertPathHasVersion(commit, subDirA, new Version(1, 0));
        AssertPathHasVersion(commit, this.RepoPath, new Version(1, 0));
    }

    [Fact]
    public void GetVersion_String_MissingFile()
    {
        Assert.Null(VersionFile.GetVersion(this.RepoPath));
    }

    private void AssertPathHasVersion(Commit commit, string absolutePath, Version expected)
    {
        var actual = VersionFile.GetVersion(absolutePath)?.Version;
        Assert.Equal(expected, actual);

        // Pass in the repo-relative path to ensure the commit is used as the data source.
        string relativePath = absolutePath.Substring(this.RepoPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        actual = VersionFile.GetVersion(commit, relativePath)?.Version;
        Assert.Equal(expected, actual);
    }
}
