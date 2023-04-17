// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.LibGit2;
using Validation;
using Xunit;
using Xunit.Abstractions;
using Version = System.Version;

public class LibGit2GitExtensionsTests : RepoTestBase
{
    public LibGit2GitExtensionsTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.InitializeSourceControl();
    }

    protected new LibGit2Context Context => (LibGit2Context)base.Context;

    [Fact]
    public void GetHeight_EmptyRepo()
    {
        this.InitializeSourceControl();

        Branch head = this.LibGit2Repository.Head;
        Assert.Throws<InvalidOperationException>(() => LibGit2GitExtensions.GetHeight(this.Context));
        Assert.Throws<InvalidOperationException>(() => LibGit2GitExtensions.GetHeight(this.Context, c => true));
    }

    [Fact]
    public void GetHeight_SinglePath()
    {
        Commit first = this.LibGit2Repository.Commit("First", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        Commit second = this.LibGit2Repository.Commit("Second", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        Commit third = this.LibGit2Repository.Commit("Third", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        this.SetContextToHead();
        Assert.Equal(3, LibGit2GitExtensions.GetHeight(this.Context));
        Assert.Equal(3, LibGit2GitExtensions.GetHeight(this.Context, c => true));

        Assert.Equal(2, LibGit2GitExtensions.GetHeight(this.Context, c => c != first));
        Assert.Equal(1, LibGit2GitExtensions.GetHeight(this.Context, c => c != second));
    }

    [Fact]
    public void GetHeight_Merge()
    {
        Commit firstCommit = this.LibGit2Repository.Commit("First", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        Branch anotherBranch = this.LibGit2Repository.CreateBranch("another");
        Commit secondCommit = this.LibGit2Repository.Commit("Second", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        Commands.Checkout(this.LibGit2Repository, anotherBranch);
        Commit[] branchCommits = new Commit[5];
        for (int i = 1; i <= branchCommits.Length; i++)
        {
            branchCommits[i - 1] = this.LibGit2Repository.Commit($"branch commit #{i}", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        }

        this.LibGit2Repository.Merge(secondCommit, new Signature("t", "t@t.com", DateTimeOffset.Now), new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastForward });
        this.SetContextToHead();

        // While we've created 8 commits, the tallest height is only 7.
        Assert.Equal(7, LibGit2GitExtensions.GetHeight(this.Context));

        // Now stop enumerating early on just one branch of the ancestry -- the number should remain high.
        Assert.Equal(7, LibGit2GitExtensions.GetHeight(this.Context, c => c != secondCommit));

        // This time stop in both branches of history, and verify that we count the taller one.
        Assert.Equal(3, LibGit2GitExtensions.GetHeight(this.Context, c => c != secondCommit && c != branchCommits[2]));
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
            new FilterPath(":^excluded-dir", relativeDirectory),
        };
        commitsAt121.Add(this.WriteVersionFile(versionData, relativeDirectory));

        // Commit touching excluded path does not affect version height
        string ignoredFilePath = Path.Combine(this.RepoPath, relativeDirectory, "ignore.txt");
        File.WriteAllText(ignoredFilePath, "hello");
        Commands.Stage(this.LibGit2Repository, ignoredFilePath);
        commitsAt121.Add(this.LibGit2Repository.Commit("Add excluded file", this.Signer, this.Signer));

        // Commit touching both excluded and included path does affect height
        string includedFilePath = Path.Combine(this.RepoPath, relativeDirectory, "another-file.txt");
        File.WriteAllText(includedFilePath, "hello");
        File.WriteAllText(ignoredFilePath, "changed");
        Commands.Stage(this.LibGit2Repository, includedFilePath);
        Commands.Stage(this.LibGit2Repository, ignoredFilePath);
        commitsAt122.Add(this.LibGit2Repository.Commit("Change both excluded and included file", this.Signer, this.Signer));

        // Commit touching excluded directory does not affect version height
        string fileInExcludedDirPath = Path.Combine(this.RepoPath, relativeDirectory, "excluded-dir", "ignore.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(fileInExcludedDirPath));
        File.WriteAllText(fileInExcludedDirPath, "hello");
        Commands.Stage(this.LibGit2Repository, fileInExcludedDirPath);
        commitsAt122.Add(this.LibGit2Repository.Commit("Add file to excluded dir", this.Signer, this.Signer));

        // Commit touching project directory affects version height
        File.WriteAllText(includedFilePath, "more changes");
        Commands.Stage(this.LibGit2Repository, includedFilePath);
        commitsAt123.Add(this.LibGit2Repository.Commit("Changed included file", this.Signer, this.Signer));

        this.Context.RepoRelativeProjectDirectory = relativeDirectory;
        Assert.Equal(
            commitsAt121.OrderBy(c => c.Sha),
            LibGit2GitExtensions.GetCommitsFromVersion(this.Context, new Version(1, 2, 1)).OrderBy(c => c.Sha));
        Assert.Equal(
            commitsAt122.OrderBy(c => c.Sha),
            LibGit2GitExtensions.GetCommitsFromVersion(this.Context, new Version(1, 2, 2)).OrderBy(c => c.Sha));
        Assert.Equal(
            commitsAt123.OrderBy(c => c.Sha),
            LibGit2GitExtensions.GetCommitsFromVersion(this.Context, new Version(1, 2, 3)).OrderBy(c => c.Sha));
    }

    [Fact]
    public void GetCommitsFromVersion_WithMajorMinorChecks()
    {
        Commit v1_0_50 = this.WriteVersionFile(new VersionOptions { Version = SemanticVersion.Parse("1.0.50-preview.{height}") });
        Commit v1_1_50 = this.WriteVersionFile(new VersionOptions { Version = SemanticVersion.Parse("1.1.50-preview.{height}") });

        Assert.Empty(LibGit2GitExtensions.GetCommitsFromVersion(this.Context, new Version(1, 0)));
        Assert.Empty(LibGit2GitExtensions.GetCommitsFromVersion(this.Context, new Version(1, 0, 49)));
        Assert.Equal(v1_0_50, Assert.Single(LibGit2GitExtensions.GetCommitsFromVersion(this.Context, new Version(1, 0, 50))));
        Assert.Equal(v1_1_50, Assert.Single(LibGit2GitExtensions.GetCommitsFromVersion(this.Context, new Version(1, 1, 50))));
    }

    [Theory]
    [InlineData("2.2", "2.2-alpha.{height}", 1, 1, true)]
    [InlineData("2.2", "2.3", 1, 1, true)]
    [InlineData("2.2", "2.3-alpha", 1, 1, true)]
    [InlineData("2.2-alpha", "2.2-rc", 1, 2, false)]
    [InlineData("2.2-alpha.{height}", "2.2", 1, 1, true)]
    [InlineData("2.2-alpha.{height}", "2.2-rc.{height}", 1, 1, true)]
    [InlineData("2.2-alpha.{height}", "2.3-rc.{height}", 1, 1, true)]
    [InlineData("2.2-rc", "2.2", 1, 2, false)]
    [InlineData("2.2-rc", "2.3", 1, 1, true)]
    public void GetVersionHeight_ProgressAndReset(string version1, string version2, int expectedHeight1, int expectedHeight2, bool versionHeightReset)
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
        Debug.WriteLine("---");
        int height1 = this.GetVersionHeight(this.LibGit2Repository.Head.Commits.Skip(1).First().Sha, repoRelativeSubDirectory);

        this.Logger.WriteLine("Height 1: {0}", height1);
        this.Logger.WriteLine("Height 2: {0}", height2);

        Assert.Equal(expectedHeight1, height1);
        Assert.Equal(expectedHeight2, height2);

        Assert.Equal(!versionHeightReset, height2 > height1);
    }

    [Fact]
    public void GetIdAsVersion_ReadsMajorMinorFromVersionTxt()
    {
        this.WriteVersionFile("4.8");
        Commit firstCommit = this.LibGit2Repository.Commits.First();

        Version v1 = this.GetVersion(committish: firstCommit.Sha);
        Assert.Equal(4, v1.Major);
        Assert.Equal(8, v1.Minor);
    }

    [Fact]
    public void GetIdAsVersion_ReadsMajorMinorFromVersionTxtInSubdirectory()
    {
        this.WriteVersionFile("4.8", relativeDirectory: "foo/bar");
        Commit firstCommit = this.LibGit2Repository.Commits.First();

        Version v1 = this.GetVersion("foo/bar", firstCommit.Sha);
        Assert.Equal(4, v1.Major);
        Assert.Equal(8, v1.Minor);
    }

    [Fact]
    public void GetIdAsVersion_MissingVersionTxt()
    {
        this.AddCommits();
        Commit firstCommit = this.LibGit2Repository.Commits.First();

        Version v1 = this.GetVersion(committish: firstCommit.Sha);
        Assert.Equal(0, v1.Major);
        Assert.Equal(0, v1.Minor);
    }

    [Fact]
    public void GetIdAsVersion_VersionFileNeverCheckedIn_3Ints()
    {
        this.AddCommits();
        var expectedVersion = new Version(1, 1, 0);
        var unstagedVersionData = VersionOptions.FromVersion(expectedVersion);
        string versionFilePath = this.Context.VersionFile.SetVersion(this.RepoPath, unstagedVersionData);
        Version actualVersion = this.GetVersion();
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
        string versionFilePath = this.Context.VersionFile.SetVersion(this.RepoPath, unstagedVersionData);
        Version actualVersion = this.GetVersion();
        Assert.Equal(expectedVersion.Major, actualVersion.Major);
        Assert.Equal(expectedVersion.Minor, actualVersion.Minor);
        Assert.Equal(0, actualVersion.Build); // height is 0 since the change hasn't been committed.
        Assert.Equal(this.LibGit2Repository.Head.Commits.First().GetTruncatedCommitIdAsUInt16(), actualVersion.Revision);
    }

    [Fact]
    public void GetIdAsVersion_VersionFileChangedOnDisk()
    {
        this.WriteVersionFile();
        Commit versionChangeCommit = this.LibGit2Repository.Commits.First();
        this.AddCommits();

        // Verify that we're seeing the original version.
        Version actualVersion = this.GetVersion();
        Assert.Equal(1, actualVersion.Major);
        Assert.Equal(2, actualVersion.Minor);
        Assert.Equal(2, actualVersion.Build);
        Assert.Equal(this.LibGit2Repository.Head.Commits.First().GetTruncatedCommitIdAsUInt16(), actualVersion.Revision);

        // Now make a change on disk that isn't committed yet.
        string versionFile = this.Context.VersionFile.SetVersion(this.RepoPath, new Version("1.3"));

        // Verify that HEAD reports whatever is on disk at the time.
        actualVersion = this.GetVersion();
        Assert.Equal(1, actualVersion.Major);
        Assert.Equal(3, actualVersion.Minor);
        Assert.Equal(0, actualVersion.Build);
        Assert.Equal(this.LibGit2Repository.Head.Commits.First().GetTruncatedCommitIdAsUInt16(), actualVersion.Revision);

        // Now commit it and verify the height advances 0->1
        this.CommitVersionFile(versionFile, "1.3");
        actualVersion = this.GetVersion();
        Assert.Equal(1, actualVersion.Major);
        Assert.Equal(3, actualVersion.Minor);
        Assert.Equal(1, actualVersion.Build);
        Assert.Equal(this.LibGit2Repository.Head.Commits.First().GetTruncatedCommitIdAsUInt16(), actualVersion.Revision);
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
            commits[i] = this.LibGit2Repository.Commit($"Commit {i + 1}", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
            versions[i] = this.GetVersion(repoRelativeSubDirectory, commits[i].Sha);
            this.Logger.WriteLine($"Commit {commits[i].Id.Sha.Substring(0, 8)} as version: {versions[i]}");
        }

        this.Context.RepoRelativeProjectDirectory = repoRelativeSubDirectory;
        for (int i = 0; i < commits.Length; i++)
        {
            Assert.Equal(commits[i], LibGit2GitExtensions.GetCommitFromVersion(this.Context, versions[i]));

            // Also verify that we can find it without the revision number.
            // This is important because stable, publicly released NuGet packages
            // that contain no assemblies may only have major.minor.build as their version evidence.
            // But folks who specify a.b.c version numbers don't have any unique version component for the commit at all without the 4th integer.
            if (semanticVersion.Version.Build == -1)
            {
                Assert.Equal(commits[i], LibGit2GitExtensions.GetCommitFromVersion(this.Context, new Version(versions[i].Major, versions[i].Minor, versions[i].Build)));
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
            versions[i] = this.GetVersion(committish: commits[i].Sha);

            commits[i + 1] = this.LibGit2Repository.Commit($"Commit {i + 1}", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
            versions[i + 1] = this.GetVersion(committish: commits[i + 1].Sha);

            this.Logger.WriteLine($"Commit {commits[i].Id.Sha.Substring(0, 8)} as version: {versions[i]}");
            this.Logger.WriteLine($"Commit {commits[i + 1].Id.Sha.Substring(0, 8)} as version: {versions[i + 1]}");

            // Find the commits we just wrote while they are still at the tip of the branch.
            IEnumerable<Commit> matchingCommits = LibGit2GitExtensions.GetCommitsFromVersion(this.Context, versions[i]);
            Assert.Contains(commits[i], matchingCommits);
            matchingCommits = LibGit2GitExtensions.GetCommitsFromVersion(this.Context, versions[i + 1]);
            Assert.Contains(commits[i + 1], matchingCommits);
        }

        // Find all commits (again) now that history has been written.
        for (int i = 0; i < commits.Length; i++)
        {
            var matchingCommits = LibGit2GitExtensions.GetCommitsFromVersion(this.Context, versions[i]).ToList();
            Assert.Contains(commits[i], matchingCommits);
            if (!allowCollisions)
            {
                Assert.Single(matchingCommits);
            }
        }
    }

    [Fact]
    public void GetCommitsFromVersion_MatchesOnEitherEndian()
    {
        this.InitializeSourceControl();
        Commit commit = this.WriteVersionFile(new VersionOptions { Version = SemanticVersion.Parse("1.2"), GitCommitIdShortAutoMinimum = 4 });

        Version originalVersion = new VersionOracle(this.Context).Version;
        Version swappedEndian = new Version(originalVersion.Major, originalVersion.Minor, originalVersion.Build, BinaryPrimitives.ReverseEndianness((ushort)originalVersion.Revision));
        ushort twoBytesFromCommitId = checked((ushort)originalVersion.Revision);
        Assert.Contains(commit, LibGit2GitExtensions.GetCommitsFromVersion(this.Context, originalVersion));
        Assert.Contains(commit, LibGit2GitExtensions.GetCommitsFromVersion(this.Context, swappedEndian));
    }

    [Fact]
    public void GetIdAsVersion_Roundtrip_WithSubdirectoryVersionFiles()
    {
        var rootVersionExpected = VersionOptions.FromVersion(new Version(1, 0));
        this.Context.VersionFile.SetVersion(this.RepoPath, rootVersionExpected);

        var subPathVersionExpected = VersionOptions.FromVersion(new Version(1, 1));
        const string subPathRelative = "a";
        string subPath = Path.Combine(this.RepoPath, subPathRelative);
        Directory.CreateDirectory(subPath);
        this.Context.VersionFile.SetVersion(subPath, subPathVersionExpected);

        this.InitializeSourceControl();

        Commit head = this.LibGit2Repository.Head.Commits.First();
        Version rootVersionActual = this.GetVersion(committish: head.Sha);
        Version subPathVersionActual = this.GetVersion(subPathRelative, head.Sha);

        // Verify that the versions calculated took the path into account.
        Assert.Equal(rootVersionExpected.Version.Version.Minor, rootVersionActual?.Minor);
        Assert.Equal(subPathVersionExpected.Version.Version.Minor, subPathVersionActual?.Minor);

        // Verify that we can find the commit given the version and path.
        Assert.Equal(head, LibGit2GitExtensions.GetCommitFromVersion(this.Context, rootVersionActual));
        this.Context.RepoRelativeProjectDirectory = subPathRelative;
        Assert.Equal(head, LibGit2GitExtensions.GetCommitFromVersion(this.Context, subPathVersionActual));

        // Verify that mismatching path and version results in a null value.
        Assert.Null(LibGit2GitExtensions.GetCommitFromVersion(this.Context, rootVersionActual));
        this.Context.RepoRelativeProjectDirectory = string.Empty;
        Assert.Null(LibGit2GitExtensions.GetCommitFromVersion(this.Context, subPathVersionActual));
    }

    [Fact]
    public void GetIdAsVersion_FitsInsideCompilerConstraints()
    {
        this.WriteVersionFile("2.5");
        Commit firstCommit = this.LibGit2Repository.Commits.First();

        Version version = this.GetVersion(committish: firstCommit.Sha);
        this.Logger.WriteLine(version.ToString());

        // The C# compiler produces a build warning and truncates the version number if it exceeds 0xfffe,
        // even though a System.Version is made up of four 32-bit integers.
        Assert.True(version.Build < 0xfffe, $"{nameof(Version.Build)} component exceeds maximum allowed by the compiler as an argument for AssemblyVersionAttribute and AssemblyFileVersionAttribute.");
        Assert.True(version.Revision < 0xfffe, $"{nameof(Version.Revision)} component exceeds maximum allowed by the compiler as an argument for AssemblyVersionAttribute and AssemblyFileVersionAttribute.");
    }

    [Fact]
    public void GetIdAsVersion_MigrationFromVersionTxtToJson()
    {
        Commit txtCommit = this.WriteVersionTxtFile("4.8");

        // Delete the version.txt file so the system writes the version.json file.
        File.Delete(Path.Combine(this.RepoPath, "version.txt"));
        Commit jsonCommit = this.WriteVersionFile("4.8");
        Assert.True(File.Exists(Path.Combine(this.RepoPath, "version.json")));

        Version v1 = this.GetVersion(committish: txtCommit.Sha);
        Assert.Equal(4, v1.Major);
        Assert.Equal(8, v1.Minor);
        Assert.Equal(1, v1.Build);

        Version v2 = this.GetVersion(committish: jsonCommit.Sha);
        Assert.Equal(4, v2.Major);
        Assert.Equal(8, v2.Minor);
        Assert.Equal(2, v2.Build);
    }

    [SkippableFact(Skip = "It fails already.")] // Skippable, only run test on specific machine
    public void TestBiggerRepo()
    {
        string testBiggerRepoPath = @"D:\git\Nerdbank.GitVersioning";
        Skip.If(!Directory.Exists(testBiggerRepoPath));

        using var largeRepo = new Repository(testBiggerRepoPath);
        foreach (Commit commit in largeRepo.Head.Commits)
        {
            Version version = this.GetVersion("src", commit.Sha);
            this.Logger.WriteLine($"commit {commit.Id} got version {version}");
            using var context = LibGit2Context.Create("src", commit.Sha);
            Commit backAgain = LibGit2GitExtensions.GetCommitFromVersion(context, version);
            Assert.Equal(commit, backAgain);
        }
    }

    /// <inheritdoc/>
    protected override GitContext CreateGitContext(string path, string committish = null) => GitContext.Create(path, committish, engine: GitContext.Engine.ReadWrite);

    private Commit[] CommitsWithVersion(string majorMinorVersion)
    {
        this.WriteVersionFile(majorMinorVersion);
        var commits = new Commit[2];
        commits[0] = this.LibGit2Repository.Commits.First();
        for (int i = 1; i < commits.Length; i++)
        {
            commits[i] = this.LibGit2Repository.Commit($"Extra commit {i} for version {majorMinorVersion}", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        }

        return commits;
    }

    private void VerifyCommitsWithVersion(Commit[] commits)
    {
        Requires.NotNull(commits, nameof(commits));

        for (int i = 0; i < commits.Length; i++)
        {
            Version encodedVersion = this.GetVersion(committish: commits[i].Sha);
            Assert.Equal(i + 1, encodedVersion.Build);
            Assert.Equal(commits[i], LibGit2GitExtensions.GetCommitFromVersion(this.Context, encodedVersion));
        }
    }
}
