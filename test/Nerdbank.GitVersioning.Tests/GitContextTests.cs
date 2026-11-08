// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using Nerdbank.GitVersioning;
using Xunit;

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1649 // File name should match first type name

[Property("Engine", "Managed")]
[InheritsTests]
public class GitContextManagedTests : GitContextTests
{
    public GitContextManagedTests()
        : base(TestOutputHelper.Instance)
    {
    }

    [Test]
    [Arguments("HEAD~999")]
    [Arguments("HEAD~-1")]
    [Arguments("HEAD~2147483648")]
    public void SelectInvalidFirstParentAncestor(string committish)
    {
        Assert.False(this.Context.TrySelectCommit(committish));
    }

    [Test]
    public void SelectFirstParentAncestorInShallowRepository()
    {
        this.AddCommits();

        string firstCommitSha = this.LibGit2Repository.Head.Tip.Parents.Single().Sha;
        string firstCommitPath = Path.Combine(this.RepoPath, ".git", "objects", firstCommitSha.Substring(0, 2), firstCommitSha.Substring(2));
        File.SetAttributes(firstCommitPath, FileAttributes.Normal);
        File.Delete(firstCommitPath);
        File.WriteAllText(Path.Combine(this.RepoPath, ".git", "shallow"), firstCommitSha);

        GitException exception = Assert.Throws<GitException>(() => this.Context.TrySelectCommit("HEAD~1"));

        Assert.True(exception.IsShallowClone);
        Assert.Equal(GitException.ErrorCodes.ObjectNotFound, exception.ErrorCode);
    }

    /// <inheritdoc/>
    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, engine: GitContext.Engine.ReadOnly);
}

[Property("Engine", "LibGit2")]
[InheritsTests]
public class GitContextLibGit2Tests : GitContextTests
{
    public GitContextLibGit2Tests()
        : base(TestOutputHelper.Instance)
    {
    }

    /// <inheritdoc/>
    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, engine: GitContext.Engine.ReadWrite);
}

public abstract class GitContextTests : RepoTestBase
{
    protected GitContextTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.InitializeSourceControl();
        this.AddCommits();
    }

    [Test]
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

    [Test]
    public void DefaultBranchUsesOnlyLocalBranch()
    {
        this.LibGit2Repository.Refs.Rename("refs/heads/master", "refs/heads/release/v1.0");
        this.LibGit2Repository.Refs.UpdateTarget("HEAD", "refs/heads/release/v1.0");
        this.RecreateContext();

        Assert.Equal("release/v1.0", this.Context.GetDefaultBranch());
    }

    [Test]
    public void DefaultBranchPrefersUpstreamRemote()
    {
        this.AddRemoteDefaultBranch("origin", "main");
        this.AddRemoteDefaultBranch("upstream", "develop");
        this.RecreateContext();

        Assert.Equal("develop", this.Context.GetDefaultBranch());
    }

    [Test]
    public void DefaultBranchUsesArbitraryRemote()
    {
        this.AddRemoteDefaultBranch("fork", "trunk");
        this.RecreateContext();

        Assert.Equal("trunk", this.Context.GetDefaultBranch());
    }

    [Test]
    public void DefaultBranchUsesConfiguredBranch()
    {
        this.LibGit2Repository.Branches.Add("configured", this.LibGit2Repository.Head.Tip);
        this.LibGit2Repository.Config.Set("init.defaultBranch", "configured", LibGit2Sharp.ConfigurationLevel.Local);
        this.RecreateContext();

        Assert.Equal("configured", this.Context.GetDefaultBranch());
    }

    [Test]
    public void DefaultBranchUsesConventionalBranchOrder()
    {
        this.LibGit2Repository.Branches.Add("develop", this.LibGit2Repository.Head.Tip);
        this.LibGit2Repository.Branches.Add("main", this.LibGit2Repository.Head.Tip);
        this.RecreateContext();

        Assert.Equal("master", this.Context.GetDefaultBranch());
    }

    [Test]
    public void SelectHead()
    {
        Assert.True(this.Context.TrySelectCommit("HEAD"));
        Assert.Equal(this.LibGit2Repository.Head.Tip.Sha, this.Context.GitCommitId);
    }

    [Test]
    [Arguments("HEAD~2", 2)]
    [Arguments("HEAD~", 1)]
    public void SelectFirstParentAncestor(string committish, int generations)
    {
        this.AddCommits(2);
        LibGit2Sharp.Commit expectedCommit = this.LibGit2Repository.Head.Tip;
        for (int i = 0; i < generations; i++)
        {
            expectedCommit = expectedCommit.Parents.First();
        }

        Assert.True(this.Context.TrySelectCommit(committish));
        Assert.Equal(expectedCommit.Sha, this.Context.GitCommitId);
    }

    [Test, MatrixDataSource]
    public void SelectCommitByFullId(bool uppercase)
    {
        Assert.True(this.Context.TrySelectCommit(uppercase ? this.Context.GitCommitId.ToUpperInvariant() : this.Context.GitCommitId));
        Assert.Equal(this.LibGit2Repository.Head.Tip.Sha, this.Context.GitCommitId);
    }

    [Test, MatrixDataSource]
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
            this.RecreateContext();
        }

        Assert.True(this.Context.TrySelectCommit(this.Context.GitCommitId.Substring(0, oddLength ? 11 : 12)));
        Assert.Equal(this.LibGit2Repository.Head.Tip.Sha, this.Context.GitCommitId);
    }

    [Test]
    [Arguments(4)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(11)]
    public void GetShortUniqueCommitId(int length)
    {
        Skip.When(length < 7 && this.Context is Nerdbank.GitVersioning.LibGit2.LibGit2Context, "LibGit2Sharp never returns commit IDs with fewer than 7 characters.");
        Assert.Equal(this.Context.GitCommitId.Substring(0, length), this.Context.GetShortUniqueCommitId(length));
    }

    [Test, MatrixDataSource]
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

    [Test, MatrixDataSource]
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

    [Test, MatrixDataSource]
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

    [Test]
    public void SelectDirectory_Empty()
    {
        this.Context.RepoRelativeProjectDirectory = string.Empty;
        Assert.Equal(string.Empty, this.Context.RepoRelativeProjectDirectory);
    }

    [Test]
    public void SelectDirectory_SubDir()
    {
        string absolutePath = Path.Combine(this.RepoPath, "sub");
        Directory.CreateDirectory(absolutePath);
        this.Context.RepoRelativeProjectDirectory = "sub";
        Assert.Equal("sub", this.Context.RepoRelativeProjectDirectory);
        Assert.Equal(absolutePath, this.Context.AbsoluteProjectDirectory);
    }

    [Test]
    public void GetVersion_PackedHead()
    {
        using TestUtilities.ExpandedRepo expandedRepo = TestUtilities.ExtractRepoArchive("PackedHeadRef");
        using GitContext context = this.CreateGitContext(Path.Combine(expandedRepo.RepoPath));
        var oracle = new VersionOracle(context);
        Assert.Equal("1.0.1", oracle.SimpleVersion.ToString());
        context.TrySelectCommit("HEAD");
        Assert.Equal("1.0.1", oracle.SimpleVersion.ToString());
    }

    [Test]
    public void HeadCanonicalName_PackedHead()
    {
        using TestUtilities.ExpandedRepo expandedRepo = TestUtilities.ExtractRepoArchive("PackedHeadRef");
        using GitContext context = this.CreateGitContext(Path.Combine(expandedRepo.RepoPath));
        Assert.Equal("refs/heads/main", context.HeadCanonicalName);
    }

    [Test]
    public void GetEffectiveGitEngine_DefaultBehavior()
    {
        // Arrange: Clear all environment variables
        var originalDependabot = Environment.GetEnvironmentVariable("DEPENDABOT");
        var originalNbgvGitEngine = Environment.GetEnvironmentVariable("NBGV_GitEngine");
        var originalGitHubActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalGitHubWorkflowRef = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF");
        try
        {
            Environment.SetEnvironmentVariable("DEPENDABOT", null);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", null);
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", null);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", null);

            // Act & Assert: With no environment variables, should return default ReadOnly
            Assert.Equal(GitContext.Engine.ReadOnly, GitContext.GetEffectiveGitEngine());
            Assert.Equal(GitContext.Engine.ReadWrite, GitContext.GetEffectiveGitEngine(GitContext.Engine.ReadWrite));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEPENDABOT", originalDependabot);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", originalNbgvGitEngine);
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalGitHubActor);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", originalGitHubWorkflowRef);
        }
    }

    [Test]
    [Arguments("true")]
    [Arguments("TRUE")]
    [Arguments("True")]
    public void GetEffectiveGitEngine_DependabotEnvironment_DisablesEngine(string dependabotValue)
    {
        // Arrange: Set DEPENDABOT=true and clear NBGV_GitEngine and GITHUB_ACTOR
        var originalDependabot = Environment.GetEnvironmentVariable("DEPENDABOT");
        var originalNbgvGitEngine = Environment.GetEnvironmentVariable("NBGV_GitEngine");
        var originalGitHubActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalGitHubWorkflowRef = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF");
        try
        {
            Environment.SetEnvironmentVariable("DEPENDABOT", dependabotValue);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", null);
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", null);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", null);

            // Act & Assert: Should return Disabled regardless of requested engine
            Assert.Equal(GitContext.Engine.Disabled, GitContext.GetEffectiveGitEngine());
            Assert.Equal(GitContext.Engine.Disabled, GitContext.GetEffectiveGitEngine(GitContext.Engine.ReadOnly));
            Assert.Equal(GitContext.Engine.Disabled, GitContext.GetEffectiveGitEngine(GitContext.Engine.ReadWrite));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEPENDABOT", originalDependabot);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", originalNbgvGitEngine);
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalGitHubActor);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", originalGitHubWorkflowRef);
        }
    }

    [Test]
    [Arguments("false")]
    [Arguments("False")]
    [Arguments("0")]
    [Arguments("")]
    public void GetEffectiveGitEngine_DependabotNotTrue_UsesDefault(string dependabotValue)
    {
        // Arrange: Set DEPENDABOT to non-true value and clear NBGV_GitEngine and GITHUB_ACTOR
        var originalDependabot = Environment.GetEnvironmentVariable("DEPENDABOT");
        var originalNbgvGitEngine = Environment.GetEnvironmentVariable("NBGV_GitEngine");
        var originalGitHubActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalGitHubWorkflowRef = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF");
        try
        {
            Environment.SetEnvironmentVariable("DEPENDABOT", dependabotValue);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", null);
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", null);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", null);

            // Act & Assert: Should use default behavior
            Assert.Equal(GitContext.Engine.ReadOnly, GitContext.GetEffectiveGitEngine());
            Assert.Equal(GitContext.Engine.ReadWrite, GitContext.GetEffectiveGitEngine(GitContext.Engine.ReadWrite));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEPENDABOT", originalDependabot);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", originalNbgvGitEngine);
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalGitHubActor);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", originalGitHubWorkflowRef);
        }
    }

    [Test]
    [Arguments("LibGit2", GitContext.Engine.ReadWrite)]
    [Arguments("Managed", GitContext.Engine.ReadOnly)]
    [Arguments("Disabled", GitContext.Engine.Disabled)]
    public void GetEffectiveGitEngine_NbgvGitEngineOverridesDependabot(string nbgvValue, GitContext.Engine expectedEngine)
    {
        // Arrange: Set both DEPENDABOT and NBGV_GitEngine, clear GITHUB_ACTOR
        var originalDependabot = Environment.GetEnvironmentVariable("DEPENDABOT");
        var originalNbgvGitEngine = Environment.GetEnvironmentVariable("NBGV_GitEngine");
        var originalGitHubActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalGitHubWorkflowRef = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF");
        try
        {
            Environment.SetEnvironmentVariable("DEPENDABOT", "true");
            Environment.SetEnvironmentVariable("NBGV_GitEngine", nbgvValue);
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", null);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", null);

            // Act & Assert: NBGV_GitEngine should take precedence and be parsed correctly
            Assert.Equal(expectedEngine, GitContext.GetEffectiveGitEngine());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEPENDABOT", originalDependabot);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", originalNbgvGitEngine);
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalGitHubActor);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", originalGitHubWorkflowRef);
        }
    }

    [Test]
    public void GetEffectiveGitEngine_GitHubCopilotEnvironment_DisablesEngine()
    {
        // Arrange: Set GITHUB_ACTOR to copilot-swe-agent[bot] and clear NBGV_GitEngine and DEPENDABOT
        var originalGitHubActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalNbgvGitEngine = Environment.GetEnvironmentVariable("NBGV_GitEngine");
        var originalDependabot = Environment.GetEnvironmentVariable("DEPENDABOT");
        var originalGitHubWorkflowRef = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF");
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", "copilot-swe-agent[bot]");
            Environment.SetEnvironmentVariable("NBGV_GitEngine", null);
            Environment.SetEnvironmentVariable("DEPENDABOT", null);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", null);

            // Act & Assert: Should return Disabled regardless of requested engine
            Assert.Equal(GitContext.Engine.Disabled, GitContext.GetEffectiveGitEngine());
            Assert.Equal(GitContext.Engine.Disabled, GitContext.GetEffectiveGitEngine(GitContext.Engine.ReadOnly));
            Assert.Equal(GitContext.Engine.Disabled, GitContext.GetEffectiveGitEngine(GitContext.Engine.ReadWrite));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalGitHubActor);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", originalNbgvGitEngine);
            Environment.SetEnvironmentVariable("DEPENDABOT", originalDependabot);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", originalGitHubWorkflowRef);
        }
    }

    [Test]
    [Arguments("user")]
    [Arguments("dependabot[bot]")]
    [Arguments("copilot-swe-agent")]
    [Arguments("COPILOT-SWE-AGENT[BOT]")]
    [Arguments("")]
    public void GetEffectiveGitEngine_GitHubActorNotCopilot_UsesDefault(string gitHubActorValue)
    {
        // Arrange: Set GITHUB_ACTOR to non-copilot value and clear NBGV_GitEngine and DEPENDABOT
        var originalGitHubActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalNbgvGitEngine = Environment.GetEnvironmentVariable("NBGV_GitEngine");
        var originalDependabot = Environment.GetEnvironmentVariable("DEPENDABOT");
        var originalGitHubWorkflowRef = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF");
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", gitHubActorValue);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", null);
            Environment.SetEnvironmentVariable("DEPENDABOT", null);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", null);

            // Act & Assert: Should use default behavior
            Assert.Equal(GitContext.Engine.ReadOnly, GitContext.GetEffectiveGitEngine());
            Assert.Equal(GitContext.Engine.ReadWrite, GitContext.GetEffectiveGitEngine(GitContext.Engine.ReadWrite));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalGitHubActor);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", originalNbgvGitEngine);
            Environment.SetEnvironmentVariable("DEPENDABOT", originalDependabot);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", originalGitHubWorkflowRef);
        }
    }

    [Test]
    [Arguments("LibGit2", GitContext.Engine.ReadWrite)]
    [Arguments("Managed", GitContext.Engine.ReadOnly)]
    [Arguments("Disabled", GitContext.Engine.Disabled)]
    public void GetEffectiveGitEngine_NbgvGitEngineOverridesGitHubCopilot(string nbgvValue, GitContext.Engine expectedEngine)
    {
        // Arrange: Set both GITHUB_ACTOR=copilot-swe-agent[bot] and NBGV_GitEngine
        var originalGitHubActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalNbgvGitEngine = Environment.GetEnvironmentVariable("NBGV_GitEngine");
        var originalGitHubWorkflowRef = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF");
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", "copilot-swe-agent[bot]");
            Environment.SetEnvironmentVariable("NBGV_GitEngine", nbgvValue);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", null);

            // Act & Assert: NBGV_GitEngine should take precedence and be parsed correctly
            Assert.Equal(expectedEngine, GitContext.GetEffectiveGitEngine());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalGitHubActor);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", originalNbgvGitEngine);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", originalGitHubWorkflowRef);
        }
    }

    [Test]
    [Arguments("owner/repo/.github/workflows/copilot-setup-steps.yml@refs/heads/main")]
    [Arguments("owner/repo/.github/workflows/copilot-setup-steps.yml@refs/pull/42/merge")]
    public void GetEffectiveGitEngine_GitHubCopilotSetupWorkflow_DisablesEngine(string gitHubWorkflowRef)
    {
        var originalGitHubActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalGitHubWorkflowRef = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF");
        var originalNbgvGitEngine = Environment.GetEnvironmentVariable("NBGV_GitEngine");
        var originalDependabot = Environment.GetEnvironmentVariable("DEPENDABOT");
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", null);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", gitHubWorkflowRef);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", null);
            Environment.SetEnvironmentVariable("DEPENDABOT", null);

            Assert.Equal(GitContext.Engine.Disabled, GitContext.GetEffectiveGitEngine());
            Assert.Equal(GitContext.Engine.Disabled, GitContext.GetEffectiveGitEngine(GitContext.Engine.ReadOnly));
            Assert.Equal(GitContext.Engine.Disabled, GitContext.GetEffectiveGitEngine(GitContext.Engine.ReadWrite));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalGitHubActor);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", originalGitHubWorkflowRef);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", originalNbgvGitEngine);
            Environment.SetEnvironmentVariable("DEPENDABOT", originalDependabot);
        }
    }

    [Test]
    [Arguments("owner/repo/.github/workflows/build.yml@refs/heads/main")]
    [Arguments("owner/repo/.github/workflows/Copilot-Setup-Steps.yml@refs/heads/main")]
    [Arguments("owner/repo/.github/workflows/my-copilot-setup-steps.yml@refs/heads/main")]
    [Arguments("owner/repo/.github/workflows/copilot-setup-steps.yml.backup@refs/heads/main")]
    [Arguments("")]
    public void GetEffectiveGitEngine_GitHubWorkflowRefNotCopilotSetup_UsesDefault(string gitHubWorkflowRef)
    {
        var originalGitHubActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalGitHubWorkflowRef = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF");
        var originalNbgvGitEngine = Environment.GetEnvironmentVariable("NBGV_GitEngine");
        var originalDependabot = Environment.GetEnvironmentVariable("DEPENDABOT");
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", null);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", gitHubWorkflowRef);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", null);
            Environment.SetEnvironmentVariable("DEPENDABOT", null);

            Assert.Equal(GitContext.Engine.ReadOnly, GitContext.GetEffectiveGitEngine());
            Assert.Equal(GitContext.Engine.ReadWrite, GitContext.GetEffectiveGitEngine(GitContext.Engine.ReadWrite));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalGitHubActor);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", originalGitHubWorkflowRef);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", originalNbgvGitEngine);
            Environment.SetEnvironmentVariable("DEPENDABOT", originalDependabot);
        }
    }

    [Test]
    [Arguments("LibGit2", GitContext.Engine.ReadWrite)]
    [Arguments("Managed", GitContext.Engine.ReadOnly)]
    [Arguments("Disabled", GitContext.Engine.Disabled)]
    public void GetEffectiveGitEngine_NbgvGitEngineOverridesGitHubCopilotSetupWorkflow(string nbgvValue, GitContext.Engine expectedEngine)
    {
        var originalGitHubActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalGitHubWorkflowRef = Environment.GetEnvironmentVariable("GITHUB_WORKFLOW_REF");
        var originalNbgvGitEngine = Environment.GetEnvironmentVariable("NBGV_GitEngine");
        var originalDependabot = Environment.GetEnvironmentVariable("DEPENDABOT");
        try
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", null);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", "owner/repo/.github/workflows/copilot-setup-steps.yml@refs/heads/main");
            Environment.SetEnvironmentVariable("NBGV_GitEngine", nbgvValue);
            Environment.SetEnvironmentVariable("DEPENDABOT", null);

            Assert.Equal(expectedEngine, GitContext.GetEffectiveGitEngine());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalGitHubActor);
            Environment.SetEnvironmentVariable("GITHUB_WORKFLOW_REF", originalGitHubWorkflowRef);
            Environment.SetEnvironmentVariable("NBGV_GitEngine", originalNbgvGitEngine);
            Environment.SetEnvironmentVariable("DEPENDABOT", originalDependabot);
        }
    }

    private void AddRemoteDefaultBranch(string remoteName, string branchName)
    {
        this.LibGit2Repository.Network.Remotes.Add(remoteName, $"https://example.com/{remoteName}.git");
        string remoteDirectory = Path.Combine(this.RepoPath, ".git", "refs", "remotes", remoteName);
        string branchPath = Path.Combine(remoteDirectory, branchName.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(branchPath));
        File.WriteAllText(branchPath, $"{this.LibGit2Repository.Head.Tip.Sha}\n");
        File.WriteAllText(Path.Combine(remoteDirectory, "HEAD"), $"ref: refs/remotes/{remoteName}/{branchName}\n");
    }

    private void RecreateContext()
    {
        this.ReplaceContext(this.CreateGitContext(this.RepoPath));
    }
}
