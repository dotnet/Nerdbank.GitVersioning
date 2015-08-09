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

public class GitExtensionsTests : RepoTestBase
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
        var first = this.Repo.Commit("First", new CommitOptions { AllowEmptyCommit = true });
        var second = this.Repo.Commit("Second", new CommitOptions { AllowEmptyCommit = true });
        var third = this.Repo.Commit("Third", new CommitOptions { AllowEmptyCommit = true });
        Assert.Equal(3, this.Repo.Head.GetHeight());
        Assert.Equal(3, this.Repo.Head.GetHeight(c => true));

        Assert.Equal(2, this.Repo.Head.GetHeight(c => c != second));
        Assert.Equal(1, this.Repo.Head.GetHeight(c => c != third));
    }

    [Fact]
    public void GetHeight_Merge()
    {
        var firstCommit = this.Repo.Commit("First", new CommitOptions { AllowEmptyCommit = true });
        var anotherBranch = this.Repo.CreateBranch("another");
        var secondCommit = this.Repo.Commit("Second", new CommitOptions { AllowEmptyCommit = true });
        this.Repo.Checkout(anotherBranch);
        Commit[] branchCommits = new Commit[5];
        for (int i = 1; i <= branchCommits.Length; i++)
        {
            branchCommits[i - 1] = this.Repo.Commit($"branch commit #{i}", new CommitOptions { AllowEmptyCommit = true });
        }

        this.Repo.Merge(secondCommit, new Signature("t", "t@t.com", DateTimeOffset.Now), new MergeOptions { FastForwardStrategy = FastForwardStrategy.NoFastFoward });

        // While we've created 8 commits, the tallest height is only 7.
        Assert.Equal(7, this.Repo.Head.GetHeight());

        // Now stop enumerating early on just one branch of the ancestry -- the number should remain high.
        Assert.Equal(7, this.Repo.Head.GetHeight(c => c != secondCommit));

        // This time stop in both branches of history, and verify that we count the taller one.
        Assert.Equal(6, this.Repo.Head.GetHeight(c => c != secondCommit && c != branchCommits[2]));
    }

    [Fact]
    public void GetTruncatedCommitIdAsInteger_Roundtrip()
    {
        var firstCommit = this.Repo.Commit("First", new CommitOptions { AllowEmptyCommit = true });
        var secondCommit = this.Repo.Commit("Second", new CommitOptions { AllowEmptyCommit = true });

        int id1 = firstCommit.GetTruncatedCommitIdAsInteger();
        int id2 = secondCommit.GetTruncatedCommitIdAsInteger();

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

        Version v1 = firstCommit.GetIdAsVersion();
        Assert.Equal(4, v1.Major);
        Assert.Equal(8, v1.Minor);
    }

    [Fact]
    public void GetIdAsVersion_Roundtrip()
    {
        this.WriteVersionFile("2.5");
        var firstCommit = this.Repo.Commits.First();
        var secondCommit = this.Repo.Commit("Second", new CommitOptions { AllowEmptyCommit = true });

        Version v1 = firstCommit.GetIdAsVersion();
        Version v2 = secondCommit.GetIdAsVersion();

        this.Logger.WriteLine($"Commit {firstCommit.Id.Sha.Substring(0, 8)} as version: {v1}");
        this.Logger.WriteLine($"Commit {secondCommit.Id.Sha.Substring(0, 8)} as version: {v2}");

        Assert.Equal(firstCommit, this.Repo.GetCommitFromVersion(v1));
        Assert.Equal(secondCommit, this.Repo.GetCommitFromVersion(v2));
    }

    [Fact(Skip = "Does not yet pass")]
    public void GetIdAsVersion_FitsInsideCompilerConstraints()
    {
        this.WriteVersionFile("2.5");
        var firstCommit = this.Repo.Commits.First();

        Version version = firstCommit.GetIdAsVersion();
        this.Logger.WriteLine(version.ToString());

        // The C# compiler produces a build warning and truncates the version number if it exceeds 0xfffe,
        // even though a System.Version is made up of four 32-bit integers.
        Assert.True(version.Build < 0xfffe, $"{nameof(Version.Build)} component exceeds maximum allowed by the compiler as an argument for AssemblyVersionAttribute and AssemblyFileVersionAttribute.");
        Assert.True(version.Revision < 0xfffe, $"{nameof(Version.Revision)} component exceeds maximum allowed by the compiler as an argument for AssemblyVersionAttribute and AssemblyFileVersionAttribute.");
    }
}
