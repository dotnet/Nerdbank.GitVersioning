using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.Tests;
using Xunit;
using Xunit.Abstractions;
using ReleaseOptions = Nerdbank.GitVersioning.VersionOptions.ReleaseOptions;
using ReleaseVersionIncrement = Nerdbank.GitVersioning.VersionOptions.ReleaseVersionIncrement;
using ReleasePreparationError = Nerdbank.GitVersioning.ReleaseManager.ReleasePreparationError;
using ReleasePreparationException = Nerdbank.GitVersioning.ReleaseManager.ReleasePreparationException;

public class ReleaseManagerTests : RepoTestBase
{
    public ReleaseManagerTests(ITestOutputHelper logger) : base(logger)
    {
    }

    [Fact]
    public void PrepareRelease_NoGitRepo()
    {
        this.AssertError(() => ReleaseManager.PrepareRelease(this.RepoPath), ReleasePreparationError.NoGitRepo);        
    }

    [Fact]
    public void PrepareRelease_DirtyWorkingDirecotory()
    {       
        this.InitializeSourceControl();

        File.WriteAllText(Path.Combine(this.RepoPath, "file1.txt"), "");

        this.AssertError(() => ReleaseManager.PrepareRelease(this.RepoPath), ReleasePreparationError.UncommittedChanges);  
    }

    [Fact]
    public void PrepareRelease_DirtyIndex()
    {
        this.InitializeSourceControl();

        var filePath = Path.Combine(this.RepoPath, "file1.txt");
        File.WriteAllText(filePath, "");

        Commands.Stage(this.Repo, filePath);
    
        this.AssertError(() => ReleaseManager.PrepareRelease(this.RepoPath), ReleasePreparationError.UncommittedChanges);
    }

    [Fact]
    public void PrepareRelease_NoVersionFile()
    {
        this.InitializeSourceControl();
        this.AssertError(() => ReleaseManager.PrepareRelease(this.RepoPath), ReleasePreparationError.NoVersionFile);
    }

    [Fact]
    public void PrepareRelease_InvalidBranchNameSetting()
    {
        this.InitializeSourceControl();

        // create version.json
        var versionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse("1.2-pre"),
            Release = new ReleaseOptions()
            {
                BranchName = "nameWithoutPlaceholder",
            }
        };
        this.WriteVersionFile(versionOptions);

        this.AssertError(() => ReleaseManager.PrepareRelease(this.RepoPath), ReleasePreparationError.InvalidBranchNameSetting);
    }

    [Theory]
    [InlineData("1.2-rc", null, null, "1.2")]
    [InlineData("1.2-rc", "v{0}", null, "1.2")]
    [InlineData("1.2-rc.{height}", null, null, "1.2")]
    [InlineData("1.2-rc.{height}", "v{0}", null, "1.2")]
    [InlineData("1.2-rc.{height}+metadata", null, null, "1.2+metadata")]
    [InlineData("1.2-beta", null, "rc", "1.2-rc")]
    [InlineData("1.2-beta", "v{0}", "rc", "1.2-rc")]
    [InlineData("1.2-beta.{height}", null, "rc", "1.2-rc.{height}")]
    [InlineData("1.2-beta.{height}", "v{0}", "rc", "1.2-rc.{height}")]
    [InlineData("1.2-beta.{height}+metadata", null, "rc", "1.2-rc.{height}+metadata")]
    public void PrepareRelease_OnReleaseBranch(string currentVersion, string releaseBranchName, string releaseUnstableTag, string expectedVersion)
    {
        releaseBranchName = releaseBranchName ?? new ReleaseOptions().BranchNameOrDefault;

        // create and configure repository
        this.InitializeSourceControl();
        this.Repo.Config.Set("user.name", this.Signer.Name, ConfigurationLevel.Local);
        this.Repo.Config.Set("user.email", this.Signer.Email, ConfigurationLevel.Local);

        var initialVersionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse(currentVersion),
            Release = new ReleaseOptions()
            {
                BranchName = releaseBranchName
            }
        };

        var expectedVersionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse(expectedVersion),
            Release = new ReleaseOptions()
            {
                BranchName = releaseBranchName
            }
        };

        this.WriteVersionFile(initialVersionOptions);

        // switch to release branch
        var branchName = string.Format(releaseBranchName, initialVersionOptions.Version.Version);        
        Commands.Checkout(this.Repo, this.Repo.CreateBranch(branchName));

        ReleaseManager.PrepareRelease(this.RepoPath, releaseUnstableTag);

        // TODO: Check if a commit was created
        
        // check version on release branch
        {
            var actualVersionOptions = VersionFile.GetVersion(this.Repo.Branches[branchName].Tip);
            Assert.Equal(expectedVersionOptions, actualVersionOptions);
        }
    }

    [Theory]
    [InlineData("1.2", "rc")]
    [InlineData("1.2+metadata", "rc")]
    public void PrepeareRelease_OnReleaseBranch_VersionDecrement(string currentVersion, string releaseUnstableTag)
    {
        // create and configure repository
        this.InitializeSourceControl();
        this.Repo.Config.Set("user.name", this.Signer.Name, ConfigurationLevel.Local);
        this.Repo.Config.Set("user.email", this.Signer.Email, ConfigurationLevel.Local);

        var versionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse(currentVersion)
        };

        this.WriteVersionFile(versionOptions);

        // switch to release branch
        var branchName = string.Format(versionOptions.ReleaseOrDefault.BranchNameOrDefault, versionOptions.Version.Version);
        Commands.Checkout(this.Repo, this.Repo.CreateBranch(branchName));

        this.AssertError(() => ReleaseManager.PrepareRelease(this.RepoPath, releaseUnstableTag), ReleasePreparationError.VersionDecrement);
    }


    [Theory]
    [InlineData("1.2-pre", "1.2", "1.3-pre", ReleaseVersionIncrement.Minor, null, "pre", null)]
    [InlineData("1.2-pre", "1.2", "2.2-pre", ReleaseVersionIncrement.Major, null, "pre", null)]
    [InlineData("1.2-pre", "1.2", "1.3-pre", ReleaseVersionIncrement.Minor, "v{0}", "pre", null)]
    [InlineData("1.2-pre+metadata", "1.2+metadata", "1.3-pre+metadata", ReleaseVersionIncrement.Minor, null, "pre", null)]
    [InlineData("1.2-rc.{height}", "1.2", "1.3-beta.{height}", ReleaseVersionIncrement.Minor, null, "beta", null)]
    [InlineData("1.2-rc.{height}", "1.2", "1.3-beta.{height}", ReleaseVersionIncrement.Minor, null, "-beta", null)]
    [InlineData("1.2-beta.{height}", "1.2-rc.{height}", "1.3-alpha.{height}", ReleaseVersionIncrement.Minor, null, "alpha", "rc")]
    [InlineData("1.2-beta", "1.2-rc", "1.3-alpha", ReleaseVersionIncrement.Minor, null, "alpha", "rc")]
    [InlineData("1.2-beta+metadata", "1.2-rc+metadata", "1.3-alpha+metadata", ReleaseVersionIncrement.Minor, null, "alpha", "rc")]
    public void PrepareRelease_OnMaster(
        string initialVersion, 
        string releaseVersion, 
        string nextVersion, 
        ReleaseVersionIncrement versionIncrement, 
        string releaseBranchName,
        string firstUnstableTag,
        string releaseUnstableTag)
    {
        releaseBranchName = releaseBranchName ?? new ReleaseOptions().BranchNameOrDefault;

        // create and configure repository
        this.InitializeSourceControl();
        this.Repo.Config.Set("user.name", this.Signer.Name, ConfigurationLevel.Local);
        this.Repo.Config.Set("user.email", this.Signer.Email, ConfigurationLevel.Local);

        // create version.json
        var initialVersionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse(initialVersion),
            Release = new ReleaseOptions()
            {
                VersionIncrement = versionIncrement,
                BranchName = releaseBranchName,
                FirstUnstableTag = firstUnstableTag
            }
        };
        this.WriteVersionFile(initialVersionOptions);

        var expectedVersionOptionsReleaseBranch = new VersionOptions()
        {
            Version = SemanticVersion.Parse(releaseVersion),
            Release = new ReleaseOptions()
            {
                VersionIncrement = versionIncrement,
                BranchName = releaseBranchName,
                FirstUnstableTag = firstUnstableTag
            }
        };

        var expectedVersionOptionsCurrentBrach = new VersionOptions()
        {
            Version = SemanticVersion.Parse(nextVersion),
            Release = new ReleaseOptions()
            {
                VersionIncrement = versionIncrement,
                BranchName = releaseBranchName,
                FirstUnstableTag = firstUnstableTag
            }
        };

        var expectedBranchName = string.Format(releaseBranchName, expectedVersionOptionsReleaseBranch.Version.Version);
        var initialBranchName = this.Repo.Head.FriendlyName;
        var tipBeforeRelease = this.Repo.Head.Tip;

        // prepare release
        ReleaseManager.PrepareRelease(this.RepoPath, releaseUnstableTag);

        // check if a branch was created
        Assert.Contains(this.Repo.Branches, branch => branch.FriendlyName == expectedBranchName);

        // PrepareRelease should switch back to the initial branch
        Assert.Equal(initialBranchName, this.Repo.Head.FriendlyName);

        // check if release branch contains a new commit 
        // parent of new commit must be the commit before preparing the release
        var releaseBranch = this.Repo.Branches[expectedBranchName];
        {
            Assert.NotEqual(releaseBranch.Tip.Id, tipBeforeRelease.Id);
            Assert.Equal(releaseBranch.Tip.Parents.Single().Id, tipBeforeRelease.Id);
        }

        // check if current branch contains new commits
        // - one commit that updates the version (parent must be the commit before preparing the release)
        // - one commit merging the release branch back to master and resolving the conflict        
        {
            var mergeCommit = this.Repo.Head.Tip;
            Assert.Equal(2, mergeCommit.Parents.Count());
            Assert.Contains(mergeCommit.Parents, c => c.Id == releaseBranch.Tip.Id);
            Assert.Contains(mergeCommit.Parents, c => c.Id != releaseBranch.Tip.Id);

            var updateVersionCommit = mergeCommit.Parents.Single(c => c.Id != releaseBranch.Tip.Id);
            Assert.Single(updateVersionCommit.Parents);
            Assert.Equal(updateVersionCommit.Parents.Single().Id, tipBeforeRelease.Id);
        }

        // check version on release branch
        {
            var releaseBranchVersion = VersionFile.GetVersion(releaseBranch.Tip);
            Assert.Equal(expectedVersionOptionsReleaseBranch, releaseBranchVersion);
        }

        // check version on master branch
        {
            var currentBranchVersion = VersionFile.GetVersion(this.Repo.Head.Tip);
            Assert.Equal(expectedVersionOptionsCurrentBrach, currentBranchVersion);
        }
    }

    [Theory]
    [InlineData("1.2", "rc")]
    [InlineData("1.2+metadata", "rc")]
    public void PrepareRelease_OnMaster_VersionDecrement(string currentVersion, string releaseUnstableTag)
    {
        // create and configure repository
        this.InitializeSourceControl();
        this.Repo.Config.Set("user.name", this.Signer.Name, ConfigurationLevel.Local);
        this.Repo.Config.Set("user.email", this.Signer.Email, ConfigurationLevel.Local);

        var versionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse(currentVersion)
        };

        this.WriteVersionFile(versionOptions);

        // switch to release branch        
        this.AssertError(() => ReleaseManager.PrepareRelease(this.RepoPath, releaseUnstableTag), ReleasePreparationError.VersionDecrement);
    }



    private void AssertError(Action testCode, ReleasePreparationError expectedError)
    {
        var exception = Record.Exception(testCode);

        Assert.NotNull(exception);
        Assert.IsType<ReleasePreparationException>(exception);

        Assert.Equal(expectedError, ((ReleasePreparationException)exception).Error);
    }
}
