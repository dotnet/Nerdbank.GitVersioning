using System;
using System.Collections.Generic;
using System.IO;
using LibGit2Sharp;
using Nerdbank.GitVersioning;

public partial class RepoTestBase
{
    /// <summary>
    /// Gets the number of commits in the longest single path between
    /// the specified commit and the most distant ancestor (inclusive)
    /// that set the version to the value at the tip of the <paramref name="branch"/>.
    /// </summary>
    /// <param name="branch">The branch to measure the height of.</param>
    /// <param name="repoRelativeProjectDirectory">The repo-relative project directory for which to calculate the version.</param>
    /// <returns>The height of the branch till the version is changed.</returns>
    protected int GetVersionHeight(Branch branch, string repoRelativeProjectDirectory = null)
    {
        var commit = branch.Tip ?? throw new InvalidOperationException("No commit exists.");
        return this.GetVersionHeight(commit, repoRelativeProjectDirectory);
    }
    /// <summary>
    /// Gets the number of commits in the longest single path between
    /// the specified commit and the most distant ancestor (inclusive)
    /// that set the version to the value at <paramref name="commit"/>.
    /// </summary>
    /// <param name="commit">The commit to measure the height of.</param>
    /// <param name="repoRelativeProjectDirectory">The repo-relative project directory for which to calculate the version.</param>
    /// <param name="baseVersion">Optional base version to calculate the height. If not specified, the base version will be calculated by scanning the repository.</param>
    /// <returns>The height of the commit. Always a positive integer.</returns>
    protected int GetVersionHeight(Commit commit, string repoRelativeProjectDirectory = null)
    {
        VersionOracle oracle = VersionOracle.Create(
            projectDirectory: repoRelativeProjectDirectory == null ? this.RepoPath : Path.Combine(this.RepoPath, repoRelativeProjectDirectory),
            head: commit.Sha);

        return oracle.VersionHeight;
    }

    /// <summary>
    /// Gets the number of commits in the longest single path between
    /// HEAD in a repo and the most distant ancestor (inclusive)
    /// that set the version to the value in the working copy
    /// (or HEAD for bare repositories).
    /// </summary>
    /// <param name="repoRelativeProjectDirectory">The repo-relative project directory for which to calculate the version.</param>
    /// <returns>The height of the repo at HEAD. Always a positive integer.</returns>
    protected int GetVersionHeight(string repoRelativeProjectDirectory = null)
    {
        return GetVersionHeight(this.Repo, repoRelativeProjectDirectory);
    }

    protected static int GetVersionHeight(Repository repository, string repoRelativeProjectDirectory = null)
    {
        VersionOracle oracle = VersionOracle.Create(
            projectDirectory: repoRelativeProjectDirectory == null ? repository.Info.WorkingDirectory : Path.Combine(repository.Info.WorkingDirectory, repoRelativeProjectDirectory));

        return oracle.VersionHeight;
    }

    /// <summary>
    /// Encodes HEAD (or a modified working copy) from history in a <see cref="Version"/>
    /// so that the original commit can be found later.
    /// </summary>
    /// <param name="repoRelativeProjectDirectory">The repo-relative project directory for which to calculate the version.</param>
    /// <returns>
    /// A version whose <see cref="Version.Build"/> and
    /// <see cref="Version.Revision"/> components are calculated based on the commit.
    /// </returns>
    /// <remarks>
    /// In the returned version, the <see cref="Version.Build"/> component is
    /// the height of the git commit while the <see cref="Version.Revision"/>
    /// component is the first four bytes of the git commit id (forced to be a positive integer).
    /// </remarks>
    protected System.Version GetIdAsVersion(string repoRelativeProjectDirectory = null)
    {
        VersionOracle oracle = new Nerdbank.GitVersioning.LibGit2.LibGit2VersionOracle(
            repoRelativeProjectDirectory == null ? this.Repo.Info.WorkingDirectory : Path.Combine(this.Repo.Info.WorkingDirectory, repoRelativeProjectDirectory),
            this.Repo,
            null);

        return oracle.Version;
    }

    protected System.Version GetIdAsVersion(Commit commit, string repoRelativeProjectDirectory = null)
    {
        return GetIdAsVersion(this.Repo, commit, repoRelativeProjectDirectory);
    }

    protected static System.Version GetIdAsVersion(Repository repository, Commit commit, string repoRelativeProjectDirectory = null)
    {
        VersionOracle oracle = new Nerdbank.GitVersioning.LibGit2.LibGit2VersionOracle(
            repoRelativeProjectDirectory == null ? repository.Info.WorkingDirectory : Path.Combine(repository.Info.WorkingDirectory, repoRelativeProjectDirectory),
            repository,
            commit,
            null);

        return oracle.Version;
    }

    private class FakeCloudBuild : ICloudBuild
    {
        public FakeCloudBuild(string gitCommitId)
        {
            this.GitCommitId = gitCommitId;
        }

        public bool IsApplicable => true;

        public bool IsPullRequest => false;

        public string BuildingBranch => null;

        public string BuildingTag => null;

        public string GitCommitId { get; private set; }

        public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
        {
            return new Dictionary<string, string>();
        }

        public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
        {
            return new Dictionary<string, string>();
        }
    }
}
