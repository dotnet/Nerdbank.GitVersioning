// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using Nerdbank.GitVersioning;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1649 // File name should match first type name

[Trait("Engine", "Managed")]
public class GitContextManagedTests : GitContextTests
{
    public GitContextManagedTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <inheritdoc/>
    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, writable: false);
}

[Trait("Engine", "LibGit2")]
public class GitContextLibGit2Tests : GitContextTests
{
    public GitContextLibGit2Tests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <inheritdoc/>
    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, writable: true);
}

public abstract class GitContextTests : RepoTestBase
{
    protected GitContextTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.InitializeSourceControl();
        this.AddCommits();
    }

    [Fact]
    public void InitialDefaultState()
    {
        Assert.Equal(this.LibGit2Repository.Head.Tip.Id.Sha, this.Context.GitCommitId);
        Assert.Equal(this.LibGit2Repository.Head.Tip.Author.When, this.Context.GitCommitDate);
        Assert.Equal("refs/heads/master", this.Context.HeadCanonicalName);
        Assert.Equal(this.RepoPath, this.Context.AbsoluteProjectDirectory);
        Assert.Equal(this.RepoPath, this.Context.WorkingTreePath);
        Assert.Equal(string.Empty, this.Context.RepoRelativeProjectDirectory);
        Assert.True(this.Context.IsHead);
        Assert.True(this.Context.IsRepository);
        Assert.False(this.Context.IsShallow);
        Assert.NotNull(this.Context.VersionFile);
    }

    [Fact]
    public void SelectHead()
    {
        Assert.True(this.Context.TrySelectCommit("HEAD"));
        Assert.Equal(this.LibGit2Repository.Head.Tip.Sha, this.Context.GitCommitId);
    }

    [Theory, CombinatorialData]
    public void SelectCommitByFullId(bool uppercase)
    {
        Assert.True(this.Context.TrySelectCommit(uppercase ? this.Context.GitCommitId.ToUpperInvariant() : this.Context.GitCommitId));
        Assert.Equal(this.LibGit2Repository.Head.Tip.Sha, this.Context.GitCommitId);
    }

    [Theory, CombinatorialData]
    public void SelectCommitByPartialId(bool fromPack, bool oddLength)
    {
        if (fromPack)
        {
            this.LibGit2Repository.ObjectDatabase.Pack(new LibGit2Sharp.PackBuilderOptions(Path.Combine(this.RepoPath, ".git", "objects", "pack")));
            foreach (string obDirectory in Directory.EnumerateDirectories(Path.Combine(this.RepoPath, ".git", "objects"), "??"))
            {
                TestUtilities.DeleteDirectory(obDirectory);
            }

            // The managed git context always assumes read-only access. It won't detect a new Git pack file being
            // created on the fly, so we have to re-initialize.
            this.Context = this.CreateGitContext(this.RepoPath, null);
        }

        Assert.True(this.Context.TrySelectCommit(this.Context.GitCommitId.Substring(0, oddLength ? 11 : 12)));
        Assert.Equal(this.LibGit2Repository.Head.Tip.Sha, this.Context.GitCommitId);
    }

    [SkippableTheory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(11)]
    public void GetShortUniqueCommitId(int length)
    {
        Skip.If(length < 7 && this.Context is Nerdbank.GitVersioning.LibGit2.LibGit2Context, "LibGit2Sharp never returns commit IDs with fewer than 7 characters.");
        Assert.Equal(this.Context.GitCommitId.Substring(0, length), this.Context.GetShortUniqueCommitId(length));
    }

    [Theory, CombinatorialData]
    public void SelectCommitByTag(bool packedRefs, bool canonicalName)
    {
        if (packedRefs)
        {
            File.WriteAllText(Path.Combine(this.RepoPath, ".git", "packed-refs"), $"# pack-refs with: peeled fully-peeled sorted \n{this.Context.GitCommitId} refs/tags/test\n");
        }
        else
        {
            this.LibGit2Repository.Tags.Add("test", this.LibGit2Repository.Head.Tip);
        }

        Assert.True(this.Context.TrySelectCommit(canonicalName ? "refs/tags/test" : "test"));
        Assert.Equal(this.LibGit2Repository.Head.Tip.Sha, this.Context.GitCommitId);
    }

    [Theory, CombinatorialData]
    public void SelectCommitByBranch(bool packedRefs, bool canonicalName)
    {
        if (packedRefs)
        {
            File.WriteAllText(Path.Combine(this.RepoPath, ".git", "packed-refs"), $"# pack-refs with: peeled fully-peeled sorted \n{this.Context.GitCommitId} refs/heads/test\n");
        }
        else
        {
            this.LibGit2Repository.Branches.Add("test", this.LibGit2Repository.Head.Tip);
        }

        Assert.True(this.Context.TrySelectCommit(canonicalName ? "refs/heads/test" : "test"));
        Assert.Equal(this.LibGit2Repository.Head.Tip.Sha, this.Context.GitCommitId);
    }

    [Theory, CombinatorialData]
    public void SelectCommitByRemoteBranch(bool packedRefs, bool canonicalName)
    {
        if (packedRefs)
        {
            File.WriteAllText(Path.Combine(this.RepoPath, ".git", "packed-refs"), $"# pack-refs with: peeled fully-peeled sorted \n{this.Context.GitCommitId} refs/remotes/origin/test\n");
        }
        else
        {
            string fileName = Path.Combine(this.RepoPath, ".git", "refs", "remotes", "origin", "test");
            Directory.CreateDirectory(Path.GetDirectoryName(fileName));
            File.WriteAllText(fileName, $"{this.Context.GitCommitId}\n");
        }

        Assert.True(this.Context.TrySelectCommit(canonicalName ? "refs/remotes/origin/test" : "origin/test"));
        Assert.Equal(this.LibGit2Repository.Head.Tip.Sha, this.Context.GitCommitId);
    }

    [Fact]
    public void SelectDirectory_Empty()
    {
        this.Context.RepoRelativeProjectDirectory = string.Empty;
        Assert.Equal(string.Empty, this.Context.RepoRelativeProjectDirectory);
    }

    [Fact]
    public void SelectDirectory_SubDir()
    {
        string absolutePath = Path.Combine(this.RepoPath, "sub");
        Directory.CreateDirectory(absolutePath);
        this.Context.RepoRelativeProjectDirectory = "sub";
        Assert.Equal("sub", this.Context.RepoRelativeProjectDirectory);
        Assert.Equal(absolutePath, this.Context.AbsoluteProjectDirectory);
    }

    [Fact]
    public void GetVersion_PackedHead()
    {
        using TestUtilities.ExpandedRepo expandedRepo = TestUtilities.ExtractRepoArchive("PackedHeadRef");
        this.Context = this.CreateGitContext(Path.Combine(expandedRepo.RepoPath));
        var oracle = new VersionOracle(this.Context);
        Assert.Equal("1.0.1", oracle.SimpleVersion.ToString());
        this.Context.TrySelectCommit("HEAD");
        Assert.Equal("1.0.1", oracle.SimpleVersion.ToString());
    }

    [Fact]
    public void HeadCanonicalName_PackedHead()
    {
        using TestUtilities.ExpandedRepo expandedRepo = TestUtilities.ExtractRepoArchive("PackedHeadRef");
        this.Context = this.CreateGitContext(Path.Combine(expandedRepo.RepoPath));
        Assert.Equal("refs/heads/main", this.Context.HeadCanonicalName);
    }
}
