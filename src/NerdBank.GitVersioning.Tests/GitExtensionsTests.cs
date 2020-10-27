using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Validation;
using Xunit;
using Xunit.Abstractions;
using Version = System.Version;

public partial class GitExtensionsTests : RepoTestBase
{
    public GitExtensionsTests(ITestOutputHelper Logger)
        : base(Logger)
    {
        this.InitializeSourceControl();
    }

    [Fact]
    public void GetHeight_EmptyRepo()
    {
        Branch head = this.Repo.Head;
        Assert.Throws<InvalidOperationException>(() => head.GetHeight());
        Assert.Throws<InvalidOperationException>(() => head.GetHeight(c => true));
    }

    [Fact]
    public void GetHeight_SinglePath()
    {
        var first = this.Repo.Commit("First", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        var second = this.Repo.Commit("Second", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        var third = this.Repo.Commit("Third", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        Assert.Equal(3, this.Repo.Head.GetHeight());
        Assert.Equal(3, this.Repo.Head.GetHeight(c => true));

        Assert.Equal(2, this.Repo.Head.GetHeight(c => c != first));
        Assert.Equal(1, this.Repo.Head.GetHeight(c => c != second));
    }

    [Fact]
    public void GetHeight_Merge()
    {
        var firstCommit = this.Repo.Commit("First", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        var anotherBranch = this.Repo.CreateBranch("another");
        var secondCommit = this.Repo.Commit("Second", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        Commands.Checkout(this.Repo, anotherBranch);
        Commit[] branchCommits = new Commit[5];
        for (int i = 1; i <= branchCommits.Length; i++)
        {
            branchCommits[i - 1] = this.Repo.Commit($"branch commit #{i}", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        }

        this.Repo.Merge(secondCommit, new Signature("t", "t@t.com", DateTimeOffset.Now), new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastForward });

        // While we've created 8 commits, the tallest height is only 7.
        Assert.Equal(7, this.Repo.Head.GetHeight());

        // Now stop enumerating early on just one branch of the ancestry -- the number should remain high.
        Assert.Equal(7, this.Repo.Head.GetHeight(c => c != secondCommit));

        // This time stop in both branches of history, and verify that we count the taller one.
        Assert.Equal(3, this.Repo.Head.GetHeight(c => c != secondCommit && c != branchCommits[2]));
    }

    [Fact]
    public void GetVersionHeight_Test()
    {
        var first = this.Repo.Commit("First", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        var second = this.Repo.Commit("Second", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        this.WriteVersionFile();
        var third = this.Repo.Commit("Third", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        Assert.Equal(2, this.GetVersionHeight(this.Repo.Head));
    }

    [Fact]
    public void GetVersionHeight_VersionJsonHasUnrelatedHistory()
    {
        // Emulate a repo that used version.json for something else.
        string versionJsonPath = Path.Combine(this.RepoPath, "version.json");
        File.WriteAllText(versionJsonPath, @"{ ""unrelated"": false }");
        Assert.Equal(0, this.GetVersionHeight()); // exercise code that handles the file not yet checked in.
        Commands.Stage(this.Repo, versionJsonPath);
        this.Repo.Commit("Add unrelated version.json file.", this.Signer, this.Signer);
        Assert.Equal(0, this.GetVersionHeight()); // exercise code that handles a checked in file.

        // And now the repo has decided to use this package.
        this.WriteVersionFile();

        Assert.Equal(1, this.GetVersionHeight(this.Repo.Head));
        Assert.Equal(1, this.GetVersionHeight());

        // Also emulate case of where the related version.json was just changed to conform,
        // but not yet checked in.
        this.Repo.Reset(ResetMode.Mixed, this.Repo.Head.Tip.Parents.Single());
        Assert.Equal(0, this.GetVersionHeight());
    }

    [Fact]
    public void GetVersionHeight_VersionJsonHasParsingErrorsInHistory()
    {
        this.WriteVersionFile();
        Assert.Equal(1, this.GetVersionHeight());

        // Now introduce a parsing error.
        string versionJsonPath = Path.Combine(this.RepoPath, "version.json");
        File.WriteAllText(versionJsonPath, @"{ ""version"": ""1.0"""); // no closing curly brace for parsing error
        Assert.Equal(0, this.GetVersionHeight());
        Commands.Stage(this.Repo, versionJsonPath);
        this.Repo.Commit("Add broken version.json file.", this.Signer, this.Signer);
        Assert.Equal(0, this.GetVersionHeight());

        // Now fix it.
        this.WriteVersionFile();
        Assert.Equal(1, this.GetVersionHeight());

        // And emulate fixing it without having checked in yet.
        this.Repo.Reset(ResetMode.Mixed, this.Repo.Head.Tip.Parents.Single());
        Assert.Equal(0, this.GetVersionHeight());
    }

    [Fact]
    public void GetVersionHeight_IntroducingFiltersIncrementsHeight()
    {
        string relativeDirectory = "some-sub-dir";

        this.WriteVersionFile(relativeDirectory: relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[] { new FilterPath("./", relativeDirectory) };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));
    }

    [Theory]
    [InlineData("./")]
    [InlineData("../some-sub-dir")]
    [InlineData("/some-sub-dir")]
    [InlineData(":/some-sub-dir")]
    public void GetVersionHeight_IncludeFilter(string includeFilter)
    {
        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[] { new FilterPath(includeFilter, relativeDirectory) };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Expect commit outside of project tree to not affect version height
        var otherFilePath = Path.Combine(this.RepoPath, "my-file.txt");
        File.WriteAllText(otherFilePath, "hello");
        Commands.Stage(this.Repo, otherFilePath);
        this.Repo.Commit("Add other file outside of project root", this.Signer, this.Signer);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Expect commit inside project tree to affect version height
        var containedFilePath = Path.Combine(this.RepoPath, relativeDirectory, "another-file.txt");
        File.WriteAllText(containedFilePath, "hello");
        Commands.Stage(this.Repo, containedFilePath);
        this.Repo.Commit("Add file within project root", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersionHeight_IncludeExcludeFilter()
    {
        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[]
        {
            new FilterPath("./", relativeDirectory),
            new FilterPath(":^/some-sub-dir/ignore.txt", relativeDirectory),
            new FilterPath(":^excluded-dir", relativeDirectory)
        };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Commit touching excluded path does not affect version height
        var ignoredFilePath = Path.Combine(this.RepoPath, relativeDirectory, "ignore.txt");
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.Repo, ignoredFilePath);
        this.Repo.Commit("Add excluded file", this.Signer, this.Signer);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Commit touching both excluded and included path does affect height
        var includedFilePath = Path.Combine(this.RepoPath, relativeDirectory, "another-file.txt");
        File.WriteAllText(includedFilePath, "hello");
        File.WriteAllText(ignoredFilePath, "changed");
        Commands.Stage(this.Repo, includedFilePath);
        Commands.Stage(this.Repo, ignoredFilePath);
        this.Repo.Commit("Change both excluded and included file", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));

        // Commit touching excluded directory does not affect version height
        var fileInExcludedDirPath = Path.Combine(this.RepoPath, relativeDirectory, "excluded-dir", "ignore.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(fileInExcludedDirPath));
        File.WriteAllText(fileInExcludedDirPath, "hello");
        Commands.Stage(this.Repo, fileInExcludedDirPath);
        this.Repo.Commit("Add file to excluded dir", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersionHeight_IncludeExcludeFilter_NoProjectDirectory()
    {
        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[]
        {
            new FilterPath("./", "."),
            new FilterPath(":^/some-sub-dir/ignore.txt", "."),
            new FilterPath(":^/excluded-dir", ".")
        };
        this.WriteVersionFile(versionData);
        Assert.Equal(1, this.GetVersionHeight());

        // Commit touching excluded path does not affect version height
        var ignoredFilePath = Path.Combine(this.RepoPath, "some-sub-dir", "ignore.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredFilePath));
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.Repo, ignoredFilePath);
        this.Repo.Commit("Add excluded file", this.Signer, this.Signer);
        Assert.Equal(1, this.GetVersionHeight());

        // Commit touching both excluded and included path does affect height
        var includedFilePath = Path.Combine(this.RepoPath, "some-sub-dir", "another-file.txt");
        File.WriteAllText(includedFilePath, "hello");
        File.WriteAllText(ignoredFilePath, "changed");
        Commands.Stage(this.Repo, includedFilePath);
        Commands.Stage(this.Repo, ignoredFilePath);
        this.Repo.Commit("Change both excluded and included file", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight());

        // Commit touching excluded directory does not affect version height
        var fileInExcludedDirPath = Path.Combine(this.RepoPath, "excluded-dir", "ignore.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(fileInExcludedDirPath));
        File.WriteAllText(fileInExcludedDirPath, "hello");
        Commands.Stage(this.Repo, fileInExcludedDirPath);
        this.Repo.Commit("Add file to excluded dir", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight());
    }

    [Theory]
    [InlineData(":^/excluded-dir")]
    [InlineData(":^../excluded-dir")]
    public void GetVersionHeight_AddingExcludeDoesNotLowerHeight(string excludePathFilter)
    {
        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Commit a file which will later be ignored
        var ignoredFilePath = Path.Combine(this.RepoPath, "excluded-dir", "ignore.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredFilePath));
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.Repo, ignoredFilePath);
        this.Repo.Commit("Add file which will later be excluded", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));

        versionData.PathFilters = new[] { new FilterPath(excludePathFilter, relativeDirectory), };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(3, this.GetVersionHeight(relativeDirectory));

        // Committing a change to an ignored file does not increment the version height
        File.WriteAllText(ignoredFilePath, "changed");
        Commands.Stage(this.Repo, ignoredFilePath);
        this.Repo.Commit("Change now excluded file", this.Signer, this.Signer);
        Assert.Equal(3, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersionHeight_IncludeRoot()
    {
        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[] { new FilterPath(":/", relativeDirectory) };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Expect commit outside of project tree to affect version height
        var otherFilePath = Path.Combine(this.RepoPath, "my-file.txt");
        File.WriteAllText(otherFilePath, "hello");
        Commands.Stage(this.Repo, otherFilePath);
        this.Repo.Commit("Add other file outside of project root", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));

        // Expect commit inside project tree to affect version height
        var containedFilePath = Path.Combine(this.RepoPath, relativeDirectory, "another-file.txt");
        File.WriteAllText(containedFilePath, "hello");
        Commands.Stage(this.Repo, containedFilePath);
        this.Repo.Commit("Add file within project root", this.Signer, this.Signer);
        Assert.Equal(3, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersionHeight_IncludeRootExcludeSome()
    {
        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[]
        {
            new FilterPath(":/", relativeDirectory),
            new FilterPath(":^/excluded-dir", relativeDirectory),
        };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Expect commit in an excluded directory to not affect version height
        var ignoredFilePath = Path.Combine(this.RepoPath, "excluded-dir", "my-file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredFilePath));
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.Repo, ignoredFilePath);
        this.Repo.Commit("Add other file to excluded directory", this.Signer, this.Signer);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Expect commit within another directory to affect version height
        var otherFilePath = Path.Combine(this.RepoPath, "another-dir", "another-file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(otherFilePath));
        File.WriteAllText(otherFilePath, "hello");
        Commands.Stage(this.Repo, otherFilePath);
        this.Repo.Commit("Add file within project root", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersionHeight_ProjectDirectoryDifferentToVersionJsonDirectory()
    {
        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[]
        {
            new FilterPath(".", "")
        };
        this.WriteVersionFile(versionData, "");
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Expect commit in an excluded directory to not affect version height
        var ignoredFilePath = Path.Combine(this.RepoPath, "other-dir", "my-file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredFilePath));
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.Repo, ignoredFilePath);
        this.Repo.Commit("Add file to other directory", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));
    }

    [Fact]
    public void GetVersionHeight_ProjectDirectoryIsMoved()
    {
        string relativeDirectory = "some-sub-dir";

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[]
        {
            new FilterPath("./", relativeDirectory),
            new FilterPath(":^/some-sub-dir/ignore.txt", relativeDirectory),
            new FilterPath(":^excluded-dir", relativeDirectory),
        };
        this.WriteVersionFile(versionData, relativeDirectory);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Commit touching excluded path does not affect version height
        var ignoredFilePath = Path.Combine(this.RepoPath, relativeDirectory, "ignore.txt");
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.Repo, ignoredFilePath);
        this.Repo.Commit("Add excluded file", this.Signer, this.Signer);
        Assert.Equal(1, this.GetVersionHeight(relativeDirectory));

        // Commit touching both excluded and included path does affect height
        var includedFilePath = Path.Combine(this.RepoPath, relativeDirectory, "another-file.txt");
        File.WriteAllText(includedFilePath, "hello");
        File.WriteAllText(ignoredFilePath, "changed");
        Commands.Stage(this.Repo, includedFilePath);
        Commands.Stage(this.Repo, ignoredFilePath);
        this.Repo.Commit("Change both excluded and included file", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));

        // Commit touching excluded directory does not affect version height
        var fileInExcludedDirPath = Path.Combine(this.RepoPath, relativeDirectory, "excluded-dir", "ignore.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(fileInExcludedDirPath));
        File.WriteAllText(fileInExcludedDirPath, "hello");
        Commands.Stage(this.Repo, fileInExcludedDirPath);
        this.Repo.Commit("Add file to excluded dir", this.Signer, this.Signer);
        Assert.Equal(2, this.GetVersionHeight(relativeDirectory));

        // Rename the project directory
        Directory.Move(Path.Combine(this.RepoPath, relativeDirectory), Path.Combine(this.RepoPath, "new-project-dir"));
        Commands.Stage(this.Repo, relativeDirectory);
        Commands.Stage(this.Repo, "new-project-dir");
        this.Repo.Commit("Move project directory", this.Signer, this.Signer);

        // Version is reset as project directory cannot be find in the ancestor commit
        Assert.Equal(1, this.GetVersionHeight("new-project-dir"));
    }

    [Fact(Skip = "Slow test")]
    public void GetVersionHeight_VeryLongHistory()
    {
        this.WriteVersionFile();

        // Make a *lot* of commits
        this.AddCommits(2000);

        this.GetVersionHeight();
    }

    [Fact]
    public void GetCommitsFromVersion_WithPathFilters()
    {
        string relativeDirectory = "some-sub-dir";

        var commitsAt121 = new List<Commit>();
        var commitsAt122 = new List<Commit>();
        var commitsAt123 = new List<Commit>();

        var versionData = VersionOptions.FromVersion(new Version("1.2"));
        versionData.PathFilters = new[]
        {
            new FilterPath("./", relativeDirectory),
            new FilterPath(":^/some-sub-dir/ignore.txt", relativeDirectory),
            new FilterPath(":^excluded-dir", relativeDirectory)
        };
        commitsAt121.Add(this.WriteVersionFile(versionData, relativeDirectory));

        // Commit touching excluded path does not affect version height
        var ignoredFilePath = Path.Combine(this.RepoPath, relativeDirectory, "ignore.txt");
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.Repo, ignoredFilePath);
        commitsAt121.Add(this.Repo.Commit("Add excluded file", this.Signer, this.Signer));

        // Commit touching both excluded and included path does affect height
        var includedFilePath = Path.Combine(this.RepoPath, relativeDirectory, "another-file.txt");
        File.WriteAllText(includedFilePath, "hello");
        File.WriteAllText(ignoredFilePath, "changed");
        Commands.Stage(this.Repo, includedFilePath);
        Commands.Stage(this.Repo, ignoredFilePath);
        commitsAt122.Add(this.Repo.Commit("Change both excluded and included file", this.Signer, this.Signer));

        // Commit touching excluded directory does not affect version height
        var fileInExcludedDirPath = Path.Combine(this.RepoPath, relativeDirectory, "excluded-dir", "ignore.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(fileInExcludedDirPath));
        File.WriteAllText(fileInExcludedDirPath, "hello");
        Commands.Stage(this.Repo, fileInExcludedDirPath);
        commitsAt122.Add(this.Repo.Commit("Add file to excluded dir", this.Signer, this.Signer));

        // Commit touching project directory affects version height
        File.WriteAllText(includedFilePath, "more changes");
        Commands.Stage(this.Repo, includedFilePath);
        commitsAt123.Add(this.Repo.Commit("Changed included file", this.Signer, this.Signer));

        Assert.Equal(
            commitsAt121.OrderBy(c => c.Sha),
            this.Repo.GetCommitsFromVersion(new Version(1, 2, 1), relativeDirectory).OrderBy(c => c.Sha));
        Assert.Equal(
            commitsAt122.OrderBy(c => c.Sha),
            this.Repo.GetCommitsFromVersion(new Version(1, 2, 2), relativeDirectory).OrderBy(c => c.Sha));
        Assert.Equal(
            commitsAt123.OrderBy(c => c.Sha),
            this.Repo.GetCommitsFromVersion(new Version(1, 2, 3), relativeDirectory).OrderBy(c => c.Sha));
    }

    [Theory]
    [InlineData("2.2-alpha", "2.2-rc", false)]
    [InlineData("2.2-rc", "2.2", false)]
    [InlineData("2.2", "2.3-alpha", true)]
    [InlineData("2.2", "2.3", true)]
    [InlineData("2.2-rc", "2.3", true)]
    [InlineData("2.2-alpha.{height}", "2.2-rc.{height}", true)]
    [InlineData("2.2-alpha.{height}", "2.3-rc.{height}", true)]
    [InlineData("2.2-alpha.{height}", "2.2", true)]
    [InlineData("2.2", "2.2-alpha.{height}", true)]
    public void GetVersionHeight_ProgressAndReset(string version1, string version2, bool versionHeightReset)
    {
        const string repoRelativeSubDirectory = "subdir";

        var semanticVersion1 = SemanticVersion.Parse(version1);
        this.WriteVersionFile(
            new VersionOptions { Version = semanticVersion1 },
            repoRelativeSubDirectory);

        var semanticVersion2 = SemanticVersion.Parse(version2);
        this.WriteVersionFile(
            new VersionOptions { Version = semanticVersion2 },
            repoRelativeSubDirectory);

        int height2 = this.GetVersionHeight(repoRelativeSubDirectory);
        int height1 = this.GetVersionHeight(this.Repo.Head.Commits.Skip(1).First(), repoRelativeSubDirectory);

        this.Logger.WriteLine("Height 1: {0}", height1);
        this.Logger.WriteLine("Height 2: {0}", height2);

        Assert.Equal(!versionHeightReset, height2 > height1);
    }

    [Fact]
    public void GetTruncatedCommitIdAsInteger_Roundtrip()
    {
        var firstCommit = this.Repo.Commit("First", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        var secondCommit = this.Repo.Commit("Second", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });

        int id1 = firstCommit.GetTruncatedCommitIdAsInt32();
        int id2 = secondCommit.GetTruncatedCommitIdAsInt32();

        this.Logger.WriteLine($"Commit {firstCommit.Id.Sha.Substring(0, 8)} as int: {id1}");
        this.Logger.WriteLine($"Commit {secondCommit.Id.Sha.Substring(0, 8)} as int: {id2}");

        Assert.Equal(firstCommit, this.Repo.GetCommitFromTruncatedIdInteger(id1));
        Assert.Equal(secondCommit, this.Repo.GetCommitFromTruncatedIdInteger(id2));
    }

    [Fact]
    public void GetIdAsVersion_ReadsMajorMinorFromVersionTxt()
    {
        this.WriteVersionFile("4.8");
        var firstCommit = this.Repo.Commits.First();

        Version v1 = this.GetIdAsVersion(firstCommit);
        Assert.Equal(4, v1.Major);
        Assert.Equal(8, v1.Minor);
    }

    [Fact]
    public void GetIdAsVersion_ReadsMajorMinorFromVersionTxtInSubdirectory()
    {
        this.WriteVersionFile("4.8", relativeDirectory: "foo/bar");
        var firstCommit = this.Repo.Commits.First();

        Version v1 = this.GetIdAsVersion(firstCommit, "foo/bar");
        Assert.Equal(4, v1.Major);
        Assert.Equal(8, v1.Minor);
    }

    [Fact]
    public void GetIdAsVersion_MissingVersionTxt()
    {
        this.AddCommits();
        var firstCommit = this.Repo.Commits.First();

        Version v1 = this.GetIdAsVersion(firstCommit);
        Assert.Equal(0, v1.Major);
        Assert.Equal(0, v1.Minor);
    }

    [Fact]
    public void GetIdAsVersion_VersionFileNeverCheckedIn_3Ints()
    {
        this.AddCommits();
        var expectedVersion = new Version(1, 1, 0);
        var unstagedVersionData = VersionOptions.FromVersion(expectedVersion);
        string versionFilePath = VersionFile.SetVersion(this.RepoPath, unstagedVersionData);
        Version actualVersion = this.GetIdAsVersion();
        Assert.Equal(expectedVersion.Major, actualVersion.Major);
        Assert.Equal(expectedVersion.Minor, actualVersion.Minor);
        Assert.Equal(expectedVersion.Build, actualVersion.Build);

        // Height is expressed in the 4th integer since 3 were specified in version.json.
        // height is 0 since the change hasn't been committed.
        Assert.Equal(0, actualVersion.Revision);
    }

    [Fact]
    public void GetIdAsVersion_VersionFileNeverCheckedIn_2Ints()
    {
        this.AddCommits();
        var expectedVersion = new Version(1, 1);
        var unstagedVersionData = VersionOptions.FromVersion(expectedVersion);
        string versionFilePath = VersionFile.SetVersion(this.RepoPath, unstagedVersionData);
        Version actualVersion = this.GetIdAsVersion();
        Assert.Equal(expectedVersion.Major, actualVersion.Major);
        Assert.Equal(expectedVersion.Minor, actualVersion.Minor);
        Assert.Equal(0, actualVersion.Build); // height is 0 since the change hasn't been committed.
        Assert.Equal(this.Repo.Head.Commits.First().GetTruncatedCommitIdAsUInt16(), actualVersion.Revision);
    }

    [Fact]
    public void GetIdAsVersion_VersionFileChangedOnDisk()
    {
        this.WriteVersionFile();
        var versionChangeCommit = this.Repo.Commits.First();
        this.AddCommits();

        // Verify that we're seeing the original version.
        Version actualVersion = this.GetIdAsVersion();
        Assert.Equal(1, actualVersion.Major);
        Assert.Equal(2, actualVersion.Minor);
        Assert.Equal(2, actualVersion.Build);
        Assert.Equal(this.Repo.Head.Commits.First().GetTruncatedCommitIdAsUInt16(), actualVersion.Revision);

        // Now make a change on disk that isn't committed yet.
        string versionFile = VersionFile.SetVersion(this.RepoPath, new Version("1.3"));

        // Verify that HEAD reports whatever is on disk at the time.
        actualVersion = this.GetIdAsVersion();
        Assert.Equal(1, actualVersion.Major);
        Assert.Equal(3, actualVersion.Minor);
        Assert.Equal(0, actualVersion.Build);
        Assert.Equal(this.Repo.Head.Commits.First().GetTruncatedCommitIdAsUInt16(), actualVersion.Revision);

        // Now commit it and verify the height advances 0->1
        this.CommitVersionFile(versionFile, "1.3");
        actualVersion = this.GetIdAsVersion();
        Assert.Equal(1, actualVersion.Major);
        Assert.Equal(3, actualVersion.Minor);
        Assert.Equal(1, actualVersion.Build);
        Assert.Equal(this.Repo.Head.Commits.First().GetTruncatedCommitIdAsUInt16(), actualVersion.Revision);
    }

    [Fact]
    public void GetIdAsVersion_ResetsBuildNumberForEachMajorMinorVersion()
    {
        Commit[] v48Commits = this.CommitsWithVersion("4.8");
        Commit[] v49Commits = this.CommitsWithVersion("4.9"); // change minor version only
        Commit[] v59Commits = this.CommitsWithVersion("5.9"); // change major version only

        this.VerifyCommitsWithVersion(v48Commits);
        this.VerifyCommitsWithVersion(v49Commits);
        this.VerifyCommitsWithVersion(v59Commits);
    }

    [Theory]
    [InlineData("2.5", "2.5", 0)]
    [InlineData("2.5.1", "2.5", 0)]
    [InlineData("2.5", "2.5", 5)]
    [InlineData("2.5", "2.5", -1)]
    [InlineData("2.5", "2.0", 0)]
    [InlineData("2.5", "2.0", 5)]
    [InlineData("2.5", "2.0", -1)]
    public void GetIdAsVersion_Roundtrip(string version, string assemblyVersion, int versionHeightOffset)
    {
        var semanticVersion = SemanticVersion.Parse(version);
        const string repoRelativeSubDirectory = "subdir";
        this.WriteVersionFile(
            new VersionOptions
            {
                Version = semanticVersion,
                AssemblyVersion = new VersionOptions.AssemblyVersionOptions(new Version(assemblyVersion)),
                VersionHeightOffset = versionHeightOffset,
            },
            repoRelativeSubDirectory);

        Commit[] commits = new Commit[16]; // create enough that statistically we'll likely hit interesting bits as MSB and LSB
        Version[] versions = new Version[commits.Length];
        for (int i = 0; i < commits.Length; i++)
        {
            commits[i] = this.Repo.Commit($"Commit {i + 1}", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
            versions[i] = this.GetIdAsVersion(commits[i], repoRelativeSubDirectory);
            this.Logger.WriteLine($"Commit {commits[i].Id.Sha.Substring(0, 8)} as version: {versions[i]}");
        }

        for (int i = 0; i < commits.Length; i++)
        {
            Assert.Equal(commits[i], this.Repo.GetCommitFromVersion(versions[i], repoRelativeSubDirectory));

            // Also verify that we can find it without the revision number.
            // This is important because stable, publicly released NuGet packages
            // that contain no assemblies may only have major.minor.build as their version evidence.
            // But folks who specify a.b.c version numbers don't have any unique version component for the commit at all without the 4th integer.
            if (semanticVersion.Version.Build == -1)
            {
                Assert.Equal(commits[i], this.Repo.GetCommitFromVersion(new Version(versions[i].Major, versions[i].Minor, versions[i].Build), repoRelativeSubDirectory));
            }
        }
    }

    [Theory]
    [InlineData(0, 2, false)]
    [InlineData(50, -4, false)] // go backwards, but don't overlap
    [InlineData(50, -2, true)] // force many build number collisions. generally revision will still make them unique, but it *might* collide on occasion.
    public void GetIdAsVersion_Roundtrip_UnstableOffset(int startingOffset, int offsetStepChange, bool allowCollisions)
    {
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.2"),
            AssemblyVersion = null,
            VersionHeightOffset = startingOffset,
        };
        this.WriteVersionFile(versionOptions);

        Commit[] commits = new Commit[16]; // create enough that statistically we'll likely hit interesting bits as MSB and LSB
        Version[] versions = new Version[commits.Length];
        for (int i = 0; i < commits.Length; i += 2)
        {
            versionOptions.VersionHeightOffset += offsetStepChange;
            commits[i] = this.WriteVersionFile(versionOptions);
            versions[i] = this.GetIdAsVersion(commits[i]);

            commits[i + 1] = this.Repo.Commit($"Commit {i + 1}", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
            versions[i + 1] = this.GetIdAsVersion(commits[i + 1]);

            this.Logger.WriteLine($"Commit {commits[i].Id.Sha.Substring(0, 8)} as version: {versions[i]}");
            this.Logger.WriteLine($"Commit {commits[i + 1].Id.Sha.Substring(0, 8)} as version: {versions[i + 1]}");

            // Find the commits we just wrote while they are still at the tip of the branch.
            var matchingCommits = this.Repo.GetCommitsFromVersion(versions[i]);
            Assert.Contains(commits[i], matchingCommits);
            matchingCommits = this.Repo.GetCommitsFromVersion(versions[i + 1]);
            Assert.Contains(commits[i + 1], matchingCommits);
        }

        // Find all commits (again) now that history has been written.
        for (int i = 0; i < commits.Length; i++)
        {
            var matchingCommits = this.Repo.GetCommitsFromVersion(versions[i]).ToList();
            Assert.Contains(commits[i], matchingCommits);
            if (!allowCollisions)
            {
                Assert.Single(matchingCommits);
            }
        }
    }

    [Fact]
    public void GetIdAsVersion_Roundtrip_WithSubdirectoryVersionFiles()
    {
        var rootVersionExpected = VersionOptions.FromVersion(new Version(1, 0));
        VersionFile.SetVersion(this.RepoPath, rootVersionExpected);

        var subPathVersionExpected = VersionOptions.FromVersion(new Version(1, 1));
        const string subPathRelative = "a";
        string subPath = Path.Combine(this.RepoPath, subPathRelative);
        Directory.CreateDirectory(subPath);
        VersionFile.SetVersion(subPath, subPathVersionExpected);

        this.InitializeSourceControl();

        Commit head = this.Repo.Head.Commits.First();
        Version rootVersionActual = this.GetIdAsVersion(head);
        Version subPathVersionActual = this.GetIdAsVersion(head, subPathRelative);

        // Verify that the versions calculated took the path into account.
        Assert.Equal(rootVersionExpected.Version.Version.Minor, rootVersionActual?.Minor);
        Assert.Equal(subPathVersionExpected.Version.Version.Minor, subPathVersionActual?.Minor);

        // Verify that we can find the commit given the version and path.
        Assert.Equal(head, this.Repo.GetCommitFromVersion(rootVersionActual));
        Assert.Equal(head, this.Repo.GetCommitFromVersion(subPathVersionActual, subPathRelative));

        // Verify that mismatching path and version results in a null value.
        Assert.Null(this.Repo.GetCommitFromVersion(rootVersionActual, subPathRelative));
        Assert.Null(this.Repo.GetCommitFromVersion(subPathVersionActual));
    }

    [Fact]
    public void GetIdAsVersion_FitsInsideCompilerConstraints()
    {
        this.WriteVersionFile("2.5");
        var firstCommit = this.Repo.Commits.First();

        Version version = this.GetIdAsVersion(firstCommit);
        this.Logger.WriteLine(version.ToString());

        // The C# compiler produces a build warning and truncates the version number if it exceeds 0xfffe,
        // even though a System.Version is made up of four 32-bit integers.
        Assert.True(version.Build < 0xfffe, $"{nameof(Version.Build)} component exceeds maximum allowed by the compiler as an argument for AssemblyVersionAttribute and AssemblyFileVersionAttribute.");
        Assert.True(version.Revision < 0xfffe, $"{nameof(Version.Revision)} component exceeds maximum allowed by the compiler as an argument for AssemblyVersionAttribute and AssemblyFileVersionAttribute.");
    }

    [Fact]
    public void GetIdAsVersion_MigrationFromVersionTxtToJson()
    {
        var txtCommit = this.WriteVersionTxtFile("4.8");

        // Delete the version.txt file so the system writes the version.json file.
        File.Delete(Path.Combine(this.RepoPath, "version.txt"));
        var jsonCommit = this.WriteVersionFile("4.8");
        Assert.True(File.Exists(Path.Combine(this.RepoPath, "version.json")));

        Version v1 = this.GetIdAsVersion(txtCommit);
        Assert.Equal(4, v1.Major);
        Assert.Equal(8, v1.Minor);
        Assert.Equal(1, v1.Build);

        Version v2 = this.GetIdAsVersion(jsonCommit);
        Assert.Equal(4, v2.Major);
        Assert.Equal(8, v2.Minor);
        Assert.Equal(2, v2.Build);
    }

    [SkippableFact(Skip = "It fails already.")] // Skippable, only run test on specific machine
    public void TestBiggerRepo()
    {
        var testBiggerRepoPath = @"D:\git\NerdBank.GitVersioning";
        Skip.If(!Directory.Exists(testBiggerRepoPath));

        using (this.Repo = new Repository(testBiggerRepoPath))
        {
            foreach (var commit in this.Repo.Head.Commits)
            {
                var version = this.GetIdAsVersion(commit, "src");
                this.Logger.WriteLine($"commit {commit.Id} got version {version}");
                var backAgain = this.Repo.GetCommitFromVersion(version, "src");
                Assert.Equal(commit, backAgain);
            }
        }
    }

    private Commit[] CommitsWithVersion(string majorMinorVersion)
    {
        this.WriteVersionFile(majorMinorVersion);
        var commits = new Commit[2];
        commits[0] = this.Repo.Commits.First();
        for (int i = 1; i < commits.Length; i++)
        {
            commits[i] = this.Repo.Commit($"Extra commit {i} for version {majorMinorVersion}", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        }

        return commits;
    }

    private void VerifyCommitsWithVersion(Commit[] commits)
    {
        Requires.NotNull(commits, nameof(commits));

        for (int i = 0; i < commits.Length; i++)
        {
            Version encodedVersion = this.GetIdAsVersion(commits[i]);
            Assert.Equal(i + 1, encodedVersion.Build);
            Assert.Equal(commits[i], this.Repo.GetCommitFromVersion(encodedVersion));
        }
    }
}
