using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using NerdBank.GitVersioning;
using Xunit;

public class GitExtensionsTests : IDisposable
{
    public GitExtensionsTests()
    {
        this.RepoPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this.RepoPath);
        this.Repo = new Repository(Repository.Init(this.RepoPath));
    }

    public Repository Repo { get; set; }

    public string RepoPath { get; set; }

    public void Dispose()
    {
        this.Repo.Dispose();

        try
        {
            Directory.Delete(this.RepoPath, true);
        }
        catch (UnauthorizedAccessException)
        {
            // Unknown why this fails so often.
            // Somehow making commits with libgit2sharp locks files
            // such that we can't delete them (but Windows Explorer can).
        }
    }

    [Fact]
    public void GetHeight_EmptyRepo()
    {
        Branch head = this.Repo.Head;
        Assert.Throws<InvalidOperationException>(() => head.GetHeight());
    }

    [Fact]
    public void GetHeight_SinglePath()
    {
        this.Repo.Commit("First", new CommitOptions { AllowEmptyCommit = true });
        this.Repo.Commit("Second", new CommitOptions { AllowEmptyCommit = true });
        Assert.Equal(2, this.Repo.Head.GetHeight());
    }

    [Fact]
    public void GetHeight_Merge()
    {
        var firstCommit = this.Repo.Commit("First", new CommitOptions { AllowEmptyCommit = true });
        var anotherBranch = this.Repo.CreateBranch("another");
        var secondCommit = this.Repo.Commit("Second", new CommitOptions { AllowEmptyCommit = true });
        this.Repo.Checkout(anotherBranch);
        for (int i = 1; i <= 5; i++)
        {
            this.Repo.Commit($"branch commit #{i}", new CommitOptions { AllowEmptyCommit = true });
        }

        this.Repo.Merge(secondCommit, new Signature("t", "t@t.com", DateTimeOffset.Now), new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastFoward });

        // While we've created 8 commits, the tallest height is only 7.
        Assert.Equal(7, this.Repo.Head.GetHeight());
    }
}
