// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.LibGit2;
using Validation;
using Xunit;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContainsRelevantChangesDisposesTreeChanges(bool hasChanges)
    {
        Commit parent = this.LibGit2Repository.Commit("Parent", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        Commit child;
        if (hasChanges)
        {
            string filePath = Path.Combine(this.RepoPath, "file.txt");
            File.WriteAllText(filePath, "content");
            Commands.Stage(this.LibGit2Repository, filePath);
            child = this.LibGit2Repository.Commit("Add file", this.Signer, this.Signer);
        }
        else
        {
            child = this.LibGit2Repository.Commit("Empty child", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        }

        TreeChanges changes = this.LibGit2Repository.Diff.Compare<TreeChanges>(parent.Tree, child.Tree);
        try
        {
            FieldInfo diffField = typeof(TreeChanges).GetField("diff", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(diffField);
            SafeHandle diffHandle = Assert.IsAssignableFrom<SafeHandle>(diffField.GetValue(changes));
            MethodInfo containsRelevantChanges = typeof(LibGit2GitExtensions).GetMethod(
                "ContainsRelevantChanges",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(TreeChanges), typeof(bool), typeof(IReadOnlyList<FilterPath>), typeof(IReadOnlyList<FilterPath>), typeof(bool) },
                modifiers: null);
            Assert.NotNull(containsRelevantChanges);

            object result = containsRelevantChanges.Invoke(null, new object[] { changes, false, Array.Empty<FilterPath>(), Array.Empty<FilterPath>(), false });
            Assert.NotNull(result);

            Assert.Equal(hasChanges, Assert.IsType<bool>(result));
            Assert.True(diffHandle.IsClosed);
        }
        finally
        {
            changes.Dispose();
        }
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

    [Fact(Skip = "It fails already.")] // Skippable, only run test on specific machine
    public void TestBiggerRepo()
    {
        string testBiggerRepoPath = @"D:\git\Nerdbank.GitVersioning";
        Assert.SkipWhen(!Directory.Exists(testBiggerRepoPath), $"{testBiggerRepoPath} does not exist.");

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
