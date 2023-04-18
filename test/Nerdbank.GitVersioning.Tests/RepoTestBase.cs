// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.LibGit2;
using Validation;
using Xunit.Abstractions;

public abstract partial class RepoTestBase : IDisposable
{
    private readonly List<string> repoDirectories = new List<string>();

    public RepoTestBase(ITestOutputHelper logger)
    {
        Requires.NotNull(logger, nameof(logger));

        this.Logger = logger;
        this.RepoPath = this.CreateDirectoryForNewRepo();
        this.Context = this.CreateGitContext(this.RepoPath);
    }

    protected ITestOutputHelper Logger { get; }

    protected GitContext? Context { get; set; }

    protected string RepoPath { get; set; }

    protected Signature Signer => new Signature("a", "a@a.com", new DateTimeOffset(2015, 8, 2, 0, 0, 0, TimeSpan.Zero));

    protected Repository? LibGit2Repository { get; private set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected abstract GitContext CreateGitContext(string path, string? committish = null);

    protected void SetContextToHead() => Assumes.True(this.Context?.TrySelectCommit("HEAD") ?? false);

    protected VersionOptions? GetVersionOptions(string? path = null, string? committish = null)
    {
        Debug.Assert(path?.Length != 40, "commit passed as path");

        using GitContext? context = this.CreateGitContext(path is null ? this.RepoPath : Path.Combine(this.RepoPath, path), committish);
        return context.VersionFile.GetVersion();
    }

    protected VersionOracle GetVersionOracle(string? path = null, string? committish = null)
    {
        using GitContext? context = this.CreateGitContext(path is null ? this.RepoPath : Path.Combine(this.RepoPath, path), committish);
        return new VersionOracle(context);
    }

    protected System.Version GetVersion(string? path = null, string? committish = null) => this.GetVersionOracle(path, committish).Version;

    protected string CreateDirectoryForNewRepo()
    {
        string repoPath;
        do
        {
            repoPath = Path.Combine(Path.GetTempPath(), this.GetType().Name + "_" + Path.GetRandomFileName());
        }
        while (Directory.Exists(repoPath));
        Directory.CreateDirectory(repoPath);

        this.repoDirectories.Add(repoPath);
        return repoPath;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.LibGit2Repository?.Dispose();
            this.Context?.Dispose();
            foreach (string dir in this.repoDirectories)
            {
                try
                {
                    TestUtilities.DeleteDirectory(dir);
                }
                catch (IOException)
                {
                    // This happens in AppVeyor a lot.
                }
            }
        }
    }

    protected virtual void InitializeSourceControl(bool withInitialCommit = true)
    {
        this.Context?.Dispose();
        this.Context = this.InitializeSourceControl(this.RepoPath, withInitialCommit);
        this.LibGit2Repository = new Repository(this.RepoPath);
    }

    protected virtual GitContext InitializeSourceControl(string repoPath, bool withInitialCommit = true)
    {
        Repository.Init(repoPath);
        var repo = new Repository(repoPath);

        // Our tests assume the default branch is master, so retain that regardless of global git configuration on the the machine running the tests.
        if (repo.Head.FriendlyName != "master")
        {
            File.WriteAllText(Path.Combine(repoPath, ".git", "HEAD"), "ref: refs/heads/master\n");
        }

        repo.Config.Set("user.name", this.Signer.Name, ConfigurationLevel.Local);
        repo.Config.Set("user.email", this.Signer.Email, ConfigurationLevel.Local);
        foreach (StatusEntry? file in repo.RetrieveStatus().Untracked)
        {
            if (!Path.GetFileName(file.FilePath).StartsWith("_git2_", StringComparison.Ordinal))
            {
                Commands.Stage(repo, file.FilePath);
            }
        }

        if (repo.Index.Count > 0 && withInitialCommit)
        {
            repo.Commit("initial commit", this.Signer, this.Signer);
        }

        repo.Dispose();
        return this.CreateGitContext(repoPath);
    }

    protected void Ignore_git2_UntrackedFile()
    {
        string gitIgnoreFilePath = Path.Combine(this.RepoPath, ".gitignore");
        File.WriteAllLines(gitIgnoreFilePath, new[] { "_git2_*" });
        Commands.Stage(this.LibGit2Repository, gitIgnoreFilePath);
        this.LibGit2Repository.Commit("Ignore _git2_ files.", this.Signer, this.Signer);
    }

    protected void AddCommits(int count = 1)
    {
        Verify.Operation(this.LibGit2Repository is object, "Repo has not been created yet.");
        for (int i = 1; i <= count; i++)
        {
            this.LibGit2Repository.Commit($"filler commit {i}", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        }

        this.SetContextToHead();
    }

    protected Commit? WriteVersionTxtFile(string version = "1.2", string prerelease = "", string? relativeDirectory = null)
    {
        if (relativeDirectory is null)
        {
            relativeDirectory = string.Empty;
        }

        string versionFilePath = Path.Combine(this.RepoPath, relativeDirectory, "version.txt");
        File.WriteAllText(versionFilePath, $"{version}\r\n{prerelease}");
        return this.CommitVersionFile(versionFilePath, $"{version}{prerelease}");
    }

    protected Commit? WriteVersionFile(string version = "1.2", string prerelease = "", string? relativeDirectory = null)
    {
        var versionData = VersionOptions.FromVersion(new System.Version(version), prerelease);
        return this.WriteVersionFile(versionData, relativeDirectory);
    }

    protected Commit? WriteVersionFile(VersionOptions versionData, string? relativeDirectory = null)
    {
        Requires.NotNull(versionData, nameof(versionData));

        if (relativeDirectory is null)
        {
            relativeDirectory = string.Empty;
        }

        bool localContextCreated = this.Context is null;
        GitContext? context = this.Context ?? GitContext.Create(this.RepoPath, engine: GitContext.Engine.ReadWrite);
        try
        {
            string versionFilePath = context.VersionFile.SetVersion(Path.Combine(this.RepoPath, relativeDirectory), versionData);
            return this.CommitVersionFile(versionFilePath, versionData.Version?.ToString());
        }
        finally
        {
            if (localContextCreated)
            {
                context.Dispose();
            }
        }
    }

    protected Commit? CommitVersionFile(string versionFilePath, string? version)
    {
        Requires.NotNullOrEmpty(versionFilePath, nameof(versionFilePath));
        Requires.NotNullOrEmpty(versionFilePath, nameof(versionFilePath));

        if (this.LibGit2Repository is object)
        {
            Assumes.True(versionFilePath.StartsWith(this.RepoPath, StringComparison.OrdinalIgnoreCase));
            string? relativeFilePath = versionFilePath.Substring(this.RepoPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Commands.Stage(this.LibGit2Repository, relativeFilePath);
            if (Path.GetExtension(relativeFilePath) == ".json")
            {
                string txtFilePath = relativeFilePath.Substring(0, relativeFilePath.Length - 4) + "txt";
                if (!File.Exists(Path.Combine(this.RepoPath, txtFilePath)) && this.LibGit2Repository.Index[txtFilePath] is not null)
                {
                    this.LibGit2Repository.Index.Remove(txtFilePath);
                }
            }

            Commit? result = this.LibGit2Repository.Commit($"Add/write {relativeFilePath} set to {version ?? "Inherited"}", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
            this.SetContextToHead();
            return result;
        }

        return null;
    }
}
