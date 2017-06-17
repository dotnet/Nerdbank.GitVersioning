using System.IO;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.Tests;
using Xunit;
using Xunit.Abstractions;

public class VersionOracleTests : RepoTestBase
{
    public VersionOracleTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact(Skip = "Unstable test. See issue #125")]
    public void Submodule_RecognizedWithCorrectVersion()
    {
        using (var expandedRepo = TestUtilities.ExtractRepoArchive("submodules"))
        {
            this.Repo = new Repository(expandedRepo.RepoPath);

            var oracleA = VersionOracle.Create(Path.Combine(expandedRepo.RepoPath, "a"));
            Assert.Equal("1.3.1", oracleA.SimpleVersion.ToString());
            Assert.Equal("e238b03e75", oracleA.GitCommitIdShort);

            var oracleB = VersionOracle.Create(Path.Combine(expandedRepo.RepoPath, "b", "projB"));
            Assert.Equal("2.5.2", oracleB.SimpleVersion.ToString());
            Assert.Equal("3ea7f010c3", oracleB.GitCommitIdShort);
        }
    }
}
