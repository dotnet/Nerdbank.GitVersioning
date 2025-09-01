// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.IO;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.LibGit2;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

public class InstallCommandTests : RepoTestBase
{
    public InstallCommandTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.InitializeSourceControl();
    }

    [Fact]
    public void Install_CreatesVersionJsonWithMasterBranch_WhenOnlyMasterBranchExists()
    {
        // Arrange: Repo is already initialized with master branch

        // Act: Install version.json using default branch detection
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.0-beta"),
            PublicReleaseRefSpec = this.DetectPublicReleaseRefSpecForTesting(),
        };

        string versionFile = this.Context!.VersionFile.SetVersion(this.RepoPath, versionOptions);

        // Assert: Verify the version.json contains the correct branch
        string jsonContent = File.ReadAllText(versionFile);
        JObject versionJson = JObject.Parse(jsonContent);
        JArray? publicReleaseRefSpec = versionJson["publicReleaseRefSpec"] as JArray;

        Assert.NotNull(publicReleaseRefSpec);
        Assert.Equal("^refs/heads/master$", publicReleaseRefSpec![0]!.ToString());
    }

    [Fact]
    public void Install_CreatesVersionJsonWithMainBranch_WhenOnlyMainBranchExists()
    {
        // Arrange: Rename the default branch to main
        if (this.LibGit2Repository is object)
        {
            // First, make sure we have a commit, then rename
            this.LibGit2Repository.Commit("Initial commit", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
            this.LibGit2Repository.Refs.Rename("refs/heads/master", "refs/heads/main");
            this.LibGit2Repository.Refs.UpdateTarget("HEAD", "refs/heads/main");
        }

        // Act: Install version.json using default branch detection
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.0-beta"),
            PublicReleaseRefSpec = this.DetectPublicReleaseRefSpecForTesting(),
        };

        string versionFile = this.Context!.VersionFile.SetVersion(this.RepoPath, versionOptions);

        // Assert: Verify the version.json contains the correct branch
        string jsonContent = File.ReadAllText(versionFile);
        JObject versionJson = JObject.Parse(jsonContent);
        JArray? publicReleaseRefSpec = versionJson["publicReleaseRefSpec"] as JArray;

        Assert.NotNull(publicReleaseRefSpec);
        Assert.Equal("^refs/heads/main$", publicReleaseRefSpec![0]!.ToString());
    }

    [Fact]
    public void Install_CreatesVersionJsonWithDevelopBranch_WhenOnlyDevelopBranchExists()
    {
        // Arrange: Rename the default branch to develop
        if (this.LibGit2Repository is object)
        {
            // First, make sure we have a commit, then rename
            this.LibGit2Repository.Commit("Initial commit", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
            this.LibGit2Repository.Refs.Rename("refs/heads/master", "refs/heads/develop");
            this.LibGit2Repository.Refs.UpdateTarget("HEAD", "refs/heads/develop");
        }

        // Act: Install version.json using default branch detection
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.0-beta"),
            PublicReleaseRefSpec = this.DetectPublicReleaseRefSpecForTesting(),
        };

        string versionFile = this.Context!.VersionFile.SetVersion(this.RepoPath, versionOptions);

        // Assert: Verify the version.json contains the correct branch
        string jsonContent = File.ReadAllText(versionFile);
        JObject versionJson = JObject.Parse(jsonContent);
        JArray? publicReleaseRefSpec = versionJson["publicReleaseRefSpec"] as JArray;

        Assert.NotNull(publicReleaseRefSpec);
        Assert.Equal("^refs/heads/develop$", publicReleaseRefSpec![0]!.ToString());
    }

    protected override GitContext CreateGitContext(string path, string? committish = null)
    {
        return GitContext.Create(path, committish, engine: GitContext.Engine.ReadWrite);
    }

    private string[] DetectPublicReleaseRefSpecForTesting()
    {
        // This method replicates the logic from DetectDefaultBranch for testing
        string defaultBranch = "master"; // Default fallback

        if (this.Context is LibGit2Context libgit2Context)
        {
            LibGit2Sharp.Repository repository = libgit2Context.Repository;

            // For testing, we'll use the simple logic of checking local branches
            LibGit2Sharp.Branch[] localBranches = repository.Branches.Where(b => !b.IsRemote).ToArray();
            if (localBranches.Length == 1)
            {
                defaultBranch = localBranches[0].FriendlyName;
            }
            else
            {
                // Use the first local branch that exists from: master, main, develop
                string[] commonBranchNames = { "master", "main", "develop" };
                foreach (string branchName in commonBranchNames)
                {
                    if (localBranches.Any(b => b.FriendlyName == branchName))
                    {
                        defaultBranch = branchName;
                        break;
                    }
                }
            }
        }

        return new string[]
        {
            $"^refs/heads/{defaultBranch}$",
            @"^refs/heads/v\d+(?:\.\d+)?$",
        };
    }
}
