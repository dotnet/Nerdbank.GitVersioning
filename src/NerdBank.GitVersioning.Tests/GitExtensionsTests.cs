using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.Tests;
using Validation;
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

        Assert.Equal(2, this.Repo.Head.GetHeight(c => c != first));
        Assert.Equal(1, this.Repo.Head.GetHeight(c => c != second));
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
        Assert.Equal(3, this.Repo.Head.GetHeight(c => c != secondCommit && c != branchCommits[2]));
    }

    [Fact]
    public void GetTruncatedCommitIdAsInteger_Roundtrip()
    {
        var firstCommit = this.Repo.Commit("First", new CommitOptions { AllowEmptyCommit = true });
        var secondCommit = this.Repo.Commit("Second", new CommitOptions { AllowEmptyCommit = true });

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

        Version v1 = firstCommit.GetIdAsVersion();
        Assert.Equal(4, v1.Major);
        Assert.Equal(8, v1.Minor);
    }

    [Fact]
    public void GetIdAsVersion_MissingVersionTxt()
    {
        this.AddCommits();
        var firstCommit = this.Repo.Commits.First();

        Version v1 = firstCommit.GetIdAsVersion();
        Assert.Equal(0, v1.Major);
        Assert.Equal(0, v1.Minor);
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

    [Fact]
    public void GetIdAsVersion_Roundtrip()
    {
        this.WriteVersionFile("2.5");

        Commit[] commits = new Commit[16]; // create enough that statistically we'll likely hit interesting bits as MSB and LSB
        Version[] versions = new Version[commits.Length];
        for (int i = 0; i < commits.Length; i++)
        {
            commits[i] = this.Repo.Commit($"Commit {i + 1}", new CommitOptions { AllowEmptyCommit = true });
            versions[i] = commits[i].GetIdAsVersion();
            this.Logger.WriteLine($"Commit {commits[i].Id.Sha.Substring(0, 8)} as version: {versions[i]}");
        }

        for (int i = 0; i < commits.Length; i++)
        {
            Assert.Equal(commits[i], this.Repo.GetCommitFromVersion(versions[i]));
        }
    }

    [Fact]
    public void GetIdAsVersion_Roundtrip_WithSubdirectoryVersionFiles()
    {
        var rootVersionExpected = new Version(1, 0);
        VersionFile.SetVersion(this.RepoPath, rootVersionExpected);

        var subPathVersionExpected = new Version(1, 1);
        const string subPathRelative = "a";
        string subPath = Path.Combine(this.RepoPath, subPathRelative);
        Directory.CreateDirectory(subPath);
        VersionFile.SetVersion(subPath, subPathVersionExpected);

        this.InitializeSourceControl();

        Commit head = this.Repo.Head.Commits.First();
        Version rootVersionActual = head.GetIdAsVersion();
        Version subPathVersionActual = head.GetIdAsVersion(subPathRelative);

        // Verify that the versions calculated took the path into account.
        Assert.Equal(rootVersionExpected.Minor, rootVersionActual?.Minor);
        Assert.Equal(subPathVersionExpected.Minor, subPathVersionActual?.Minor);

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

        Version version = firstCommit.GetIdAsVersion();
        this.Logger.WriteLine(version.ToString());

        // The C# compiler produces a build warning and truncates the version number if it exceeds 0xfffe,
        // even though a System.Version is made up of four 32-bit integers.
        Assert.True(version.Build < 0xfffe, $"{nameof(Version.Build)} component exceeds maximum allowed by the compiler as an argument for AssemblyVersionAttribute and AssemblyFileVersionAttribute.");
        Assert.True(version.Revision < 0xfffe, $"{nameof(Version.Revision)} component exceeds maximum allowed by the compiler as an argument for AssemblyVersionAttribute and AssemblyFileVersionAttribute.");
    }

    ////[Fact] // Manual, per machine test
    public void TestBiggerRepo()
    {
        using (this.Repo = new Repository(@"C:\Users\andrew\git\NerdBank.GitVersioning"))
        {
            foreach (var commit in this.Repo.Head.Commits)
            {
                var version = commit.GetIdAsVersion();
                this.Logger.WriteLine($"commit {commit.Id} got version {version}");
                var backAgain = this.Repo.GetCommitFromVersion(version);
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
            commits[i] = this.Repo.Commit($"Extra commit {i} for version {majorMinorVersion}", new CommitOptions { AllowEmptyCommit = true });
        }

        return commits;
    }

    private void VerifyCommitsWithVersion(Commit[] commits)
    {
        Requires.NotNull(commits, nameof(commits));

        for (int i = 0; i < commits.Length; i++)
        {
            Version encodedVersion = commits[i].GetIdAsVersion();
            Assert.Equal(i + 1, encodedVersion.Build);
            Assert.Equal(commits[i], this.Repo.GetCommitFromVersion(encodedVersion));
        }
    }
}
