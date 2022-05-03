// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;
using Xunit.Abstractions;

namespace ManagedGit;

public class GitRepositoryTests : RepoTestBase
{
    public GitRepositoryTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void CreateTest()
    {
        this.InitializeSourceControl();
        this.AddCommits(1);

        using (var repository = GitRepository.Create(this.RepoPath))
        {
            AssertPath(Path.Combine(this.RepoPath, ".git"), repository.CommonDirectory);
            AssertPath(Path.Combine(this.RepoPath, ".git"), repository.GitDirectory);
            AssertPath(this.RepoPath, repository.WorkingDirectory);
            AssertPath(Path.Combine(this.RepoPath, ".git", "objects"), repository.ObjectDirectory);
        }
    }

    [Fact]
    public void CreateWorkTreeTest()
    {
        this.InitializeSourceControl();
        this.AddCommits(2);

        string workTreePath = this.CreateDirectoryForNewRepo();
        Directory.Delete(workTreePath);
        this.LibGit2Repository.Worktrees.Add("HEAD~1", "myworktree", workTreePath, isLocked: false);

        using (var repository = GitRepository.Create(workTreePath))
        {
            AssertPath(Path.Combine(this.RepoPath, ".git"), repository.CommonDirectory);
            AssertPath(Path.Combine(this.RepoPath, ".git", "worktrees", "myworktree"), repository.GitDirectory);
            AssertPath(workTreePath, repository.WorkingDirectory);
            AssertPath(Path.Combine(this.RepoPath, ".git", "objects"), repository.ObjectDirectory);
        }
    }

    [Fact]
    public void CreateNotARepoTest()
    {
        Assert.Null(GitRepository.Create(null));
        Assert.Null(GitRepository.Create(string.Empty));
        Assert.Null(GitRepository.Create("/A/Path/To/A/Directory/Which/Does/Not/Exist"));
        Assert.Null(GitRepository.Create(this.RepoPath));
    }

    // A "normal" repository, where a branch is currently checked out.
    [Fact]
    public void GetHeadAsReferenceTest()
    {
        this.InitializeSourceControl();
        this.AddCommits(2);

        var headObjectId = GitObjectId.Parse(this.LibGit2Repository.Head.Tip.Sha);

        using (var repository = GitRepository.Create(this.RepoPath))
        {
            object head = repository.GetHeadAsReferenceOrSha();
            string reference = Assert.IsType<string>(head);
            Assert.Equal("refs/heads/master", reference);

            Assert.Equal(headObjectId, repository.GetHeadCommitSha());

            GitCommit? headCommit = repository.GetHeadCommit();
            Assert.NotNull(headCommit);
            Assert.Equal(headObjectId, headCommit.Value.Sha);
        }
    }

    // A repository with a detached HEAD.
    [Fact]
    public void GetHeadAsShaTest()
    {
        this.InitializeSourceControl();
        this.AddCommits(2);

        Commit newHead = this.LibGit2Repository.Head.Tip.Parents.Single();
        var newHeadObjectId = GitObjectId.Parse(newHead.Sha);
        Commands.Checkout(this.LibGit2Repository, this.LibGit2Repository.Head.Tip.Parents.Single());

        using (var repository = GitRepository.Create(this.RepoPath))
        {
            object detachedHead = repository.GetHeadAsReferenceOrSha();
            GitObjectId reference = Assert.IsType<GitObjectId>(detachedHead);
            Assert.Equal(newHeadObjectId, reference);

            Assert.Equal(newHeadObjectId, repository.GetHeadCommitSha());

            GitCommit? headCommit = repository.GetHeadCommit();
            Assert.NotNull(headCommit);
            Assert.Equal(newHeadObjectId, headCommit.Value.Sha);
        }
    }

    // A fresh repository with no commits yet.
    [Fact]
    public void GetHeadMissingTest()
    {
        this.InitializeSourceControl(withInitialCommit: false);

        using (var repository = GitRepository.Create(this.RepoPath))
        {
            object head = repository.GetHeadAsReferenceOrSha();
            string reference = Assert.IsType<string>(head);
            Assert.Equal("refs/heads/master", reference);

            Assert.Equal(GitObjectId.Empty, repository.GetHeadCommitSha());

            Assert.Null(repository.GetHeadCommit());
        }
    }

    // Fetch a commit from the object store
    [Fact]
    public void GetCommitTest()
    {
        this.InitializeSourceControl();
        this.AddCommits(2);

        var headObjectId = GitObjectId.Parse(this.LibGit2Repository.Head.Tip.Sha);

        using (var repository = GitRepository.Create(this.RepoPath))
        {
            GitCommit commit = repository.GetCommit(headObjectId);
            Assert.Equal(headObjectId, commit.Sha);
        }
    }

    [Fact]
    public void GetInvalidCommitTest()
    {
        this.InitializeSourceControl();
        this.AddCommits(2);

        var headObjectId = GitObjectId.Parse(this.LibGit2Repository.Head.Tip.Sha);

        using (var repository = GitRepository.Create(this.RepoPath))
        {
            Assert.Throws<GitException>(() => repository.GetCommit(GitObjectId.Empty));
        }
    }

    [Fact]
    public void GetTreeEntryTest()
    {
        this.InitializeSourceControl();
        File.WriteAllText(Path.Combine(this.RepoPath, "hello.txt"), "Hello, World");
        Commands.Stage(this.LibGit2Repository, "hello.txt");
        this.AddCommits();

        using (var repository = GitRepository.Create(this.RepoPath))
        {
            GitCommit? headCommit = repository.GetHeadCommit();
            Assert.NotNull(headCommit);

            GitObjectId helloBlobId = repository.GetTreeEntry(headCommit.Value.Tree, Encoding.UTF8.GetBytes("hello.txt"));
            Assert.Equal("1856e9be02756984c385482a07e42f42efd5d2f3", helloBlobId.ToString());
        }
    }

    [Fact]
    public void GetInvalidTreeEntryTest()
    {
        this.InitializeSourceControl();
        File.WriteAllText(Path.Combine(this.RepoPath, "hello.txt"), "Hello, World");
        Commands.Stage(this.LibGit2Repository, "hello.txt");
        this.AddCommits();

        using (var repository = GitRepository.Create(this.RepoPath))
        {
            GitCommit? headCommit = repository.GetHeadCommit();
            Assert.NotNull(headCommit);

            Assert.Equal(GitObjectId.Empty, repository.GetTreeEntry(headCommit.Value.Tree, Encoding.UTF8.GetBytes("goodbye.txt")));
        }
    }

    [Fact]
    public void GetObjectByShaTest()
    {
        this.InitializeSourceControl();
        this.AddCommits(2);

        var headObjectId = GitObjectId.Parse(this.LibGit2Repository.Head.Tip.Sha);

        using (var repository = GitRepository.Create(this.RepoPath))
        {
            Stream commitStream = repository.GetObjectBySha(headObjectId, "commit");
            Assert.NotNull(commitStream);

            GitObjectStream objectStream = Assert.IsType<GitObjectStream>(commitStream);
            Assert.Equal("commit", objectStream.ObjectType);
            Assert.Equal(186, objectStream.Length);
        }
    }

    // This test runs on netcoreapp only; netstandard/netfx don't support Path.GetRelativePath
#if NETCOREAPP
    [Fact]
    public void GetObjectFromAlternateTest()
    {
        // Add 2 alternates for this repository, each with their own commit.
        // Make sure that commits from the current repository and the alternates
        // can be found.
        //
        // Alternate1    Alternate2
        //     |             |
        //     +-----+ +-----+
        //            |
        //          Repo
        this.InitializeSourceControl();

        Commit localCommit = this.LibGit2Repository.Commit("Local", this.Signer, this.Signer, new CommitOptions() { AllowEmptyCommit = true });

        string alternate1Path = this.CreateDirectoryForNewRepo();
        this.InitializeSourceControl(alternate1Path).Dispose();
        var alternate1 = new Repository(alternate1Path);
        Commit alternate1Commit = alternate1.Commit("Alternate 1", this.Signer, this.Signer, new CommitOptions() { AllowEmptyCommit = true });

        string alternate2Path = this.CreateDirectoryForNewRepo();
        this.InitializeSourceControl(alternate2Path).Dispose();
        var alternate2 = new Repository(alternate2Path);
        Commit alternate2Commit = alternate2.Commit("Alternate 2", this.Signer, this.Signer, new CommitOptions() { AllowEmptyCommit = true });

        string objectDatabasePath = Path.Combine(this.RepoPath, ".git", "objects");

        Directory.CreateDirectory(Path.Combine(this.RepoPath, ".git", "objects", "info"));
        File.WriteAllText(
            Path.Combine(this.RepoPath, ".git", "objects", "info", "alternates"),
            $"{Path.GetRelativePath(objectDatabasePath, Path.Combine(alternate1Path, ".git", "objects"))}:{Path.GetRelativePath(objectDatabasePath, Path.Combine(alternate2Path, ".git", "objects"))}:");

        using (GitRepository repository = GitRepository.Create(this.RepoPath))
        {
            Assert.Equal(localCommit.Sha, repository.GetCommit(GitObjectId.Parse(localCommit.Sha)).Sha.ToString());
            Assert.Equal(alternate1Commit.Sha, repository.GetCommit(GitObjectId.Parse(alternate1Commit.Sha)).Sha.ToString());
            Assert.Equal(alternate2Commit.Sha, repository.GetCommit(GitObjectId.Parse(alternate2Commit.Sha)).Sha.ToString());
        }
    }
#endif

    [Fact]
    public void GetObjectByShaAndWrongTypeTest()
    {
        this.InitializeSourceControl();
        this.AddCommits(2);

        var headObjectId = GitObjectId.Parse(this.LibGit2Repository.Head.Tip.Sha);

        using (var repository = GitRepository.Create(this.RepoPath))
        {
            Assert.Throws<GitException>(() => repository.GetObjectBySha(headObjectId, "tree"));
        }
    }

    [Fact]
    public void GetMissingObjectByShaTest()
    {
        this.InitializeSourceControl();
        this.AddCommits(2);

        var headObjectId = GitObjectId.Parse(this.LibGit2Repository.Head.Tip.Sha);

        using (var repository = GitRepository.Create(this.RepoPath))
        {
            Assert.Throws<GitException>(() => repository.GetObjectBySha(GitObjectId.Parse("7d6b2c56ffb97eedb92f4e28583c093f7ee4b3d9"), "commit"));
            Assert.Null(repository.GetObjectBySha(GitObjectId.Empty, "commit"));
        }
    }

    [Fact]
    public void ParseAlternates_SingleValue_Test()
    {
        List<string> alternates = GitRepository.ParseAlternates(Encoding.UTF8.GetBytes("/home/git/nbgv/.git/objects\n"));
        Assert.Collection(
            alternates,
            a => Assert.Equal("/home/git/nbgv/.git/objects", a));
    }

    [Fact]
    public void ParseAlternates_SingleValue_NoTrailingNewline_Test()
    {
        List<string> alternates = GitRepository.ParseAlternates(Encoding.UTF8.GetBytes("../repo/.git/objects"));
        Assert.Collection(
            alternates,
            a => Assert.Equal("../repo/.git/objects", a));
    }

    [Fact]
    public void ParseAlternates_TwoValues_Test()
    {
        List<string> alternates = GitRepository.ParseAlternates(Encoding.UTF8.GetBytes("/home/git/nbgv/.git/objects:../../clone/.git/objects\n"));
        Assert.Collection(
            alternates,
            a => Assert.Equal("/home/git/nbgv/.git/objects", a),
            a => Assert.Equal("../../clone/.git/objects", a));
    }

    [Fact]
    public void ParseAlternates_PathWithColon_Test()
    {
        List<string> alternates = GitRepository.ParseAlternates(
            Encoding.UTF8.GetBytes("C:/Users/nbgv/objects:C:/Users/nbgv2/objects/:../../clone/.git/objects\n"),
            2);
        Assert.Collection(
            alternates,
            a => Assert.Equal("C:/Users/nbgv/objects", a),
            a => Assert.Equal("C:/Users/nbgv2/objects/", a),
            a => Assert.Equal("../../clone/.git/objects", a));
    }

    /// <inheritdoc/>
    protected override Nerdbank.GitVersioning.GitContext CreateGitContext(string path, string committish = null)
        => Nerdbank.GitVersioning.GitContext.Create(path, committish, writable: false);

    private static void AssertPath(string expected, string actual)
    {
        Assert.Equal(
            Path.GetFullPath(expected),
            Path.GetFullPath(actual));
    }
}
