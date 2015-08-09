namespace NerdBank.GitVersioning.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using LibGit2Sharp;
    using Validation;
    using Xunit.Abstractions;

    public abstract class RepoTestBase : IDisposable
    {
        public RepoTestBase(ITestOutputHelper logger)
        {
            Requires.NotNull(logger, nameof(logger));

            this.Logger = logger;
            this.RepoPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(this.RepoPath);
        }

        protected ITestOutputHelper Logger { get; }

        protected Repository Repo { get; set; }

        protected string RepoPath { get; set; }

        protected Signature Signer => new Signature("a", "a@a.com", new DateTimeOffset(2015, 8, 2, 0, 0, 0, TimeSpan.Zero));

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.Repo?.Dispose();
                TestUtilities.DeleteDirectory(this.RepoPath);
            }
        }

        protected void InitializeSourceControl()
        {
            Repository.Init(this.RepoPath);
            this.Repo = new Repository(this.RepoPath);
            foreach (var file in this.Repo.RetrieveStatus().Untracked)
            {
                this.Repo.Stage(file.FilePath);
            }

            if (this.Repo.Index.Count > 0)
            {
                this.Repo.Commit("initial commit", this.Signer);
            }
        }

        protected void AddCommits(int count = 1)
        {
            Verify.Operation(this.Repo != null, "Repo has not been created yet.");
            for (int i = 1; i <= count; i++)
            {
                this.Repo.Commit($"filler commit {i}", this.Signer, new CommitOptions { AllowEmptyCommit = true });
            }
        }

        protected void WriteVersionFile(string version = "1.2", string prerelease = "")
        {
            VersionFile.WriteVersionFile(this.RepoPath, new System.Version(version), prerelease);

            if (this.Repo != null)
            {
                this.Repo.Index.Add(VersionFile.FileName);
                this.Repo.Commit($"Add/write {VersionFile.FileName} set to {version}", this.Signer);
            }
        }
    }
}
