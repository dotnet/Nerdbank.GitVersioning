using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.Tests;
using Xunit;
using Xunit.Abstractions;

using ReleaseOptions = Nerdbank.GitVersioning.VersionOptions.ReleaseOptions;
using ReleasePreparationError = Nerdbank.GitVersioning.ReleaseManager.ReleasePreparationError;
using ReleasePreparationException = Nerdbank.GitVersioning.ReleaseManager.ReleasePreparationException;
using ReleaseVersionIncrement = Nerdbank.GitVersioning.VersionOptions.ReleaseVersionIncrement;

public class ReleaseManagerTests : RepoTestBase
{
    public ReleaseManagerTests(ITestOutputHelper logger) : base(logger)
    {
    }

    [Fact]
    public void PrepareRelease_NoGitRepo()
    {
        // running PrepareRelease should result in an error 
        // because the repo directory is not a git repository
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath), ReleasePreparationError.NoGitRepo);
    }

    [Fact]
    public void PrepareRelease_DirtyWorkingDirecotory()
    {
        this.InitializeSourceControl();

        // create a file
        File.WriteAllText(Path.Combine(this.RepoPath, "file1.txt"), "");

        // running PrepareRelease should result in an error 
        // because there is a new file not under version control
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath), ReleasePreparationError.UncommittedChanges);
    }

    [Fact]
    public void PrepareRelease_DirtyIndex()
    {
        this.InitializeSourceControl();

        // create a file and stage it
        var filePath = Path.Combine(this.RepoPath, "file1.txt");
        File.WriteAllText(filePath, "");
        Commands.Stage(this.Repo, filePath);

        // running PrepareRelease should result in an error 
        // because there are uncommitted changes
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath), ReleasePreparationError.UncommittedChanges);
    }

    [Fact]
    public void PrepareRelease_NoVersionFile()
    {
        this.InitializeSourceControl();

        // running PrepareRelease should result in an error 
        // because there is no version.json
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath), ReleasePreparationError.NoVersionFile);
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

        // running PrepareRelease should result in an error 
        // because the branchName does not have a placeholder for the version
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath), ReleasePreparationError.InvalidBranchNameSetting);
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
    public void PrepareRelease_OnReleaseBranch(string initialVersion, string releaseOptionsBranchName, string releaseUnstableTag, string resultingVersion)
    {
        releaseOptionsBranchName = releaseOptionsBranchName ?? new ReleaseOptions().BranchNameOrDefault;

        // create and configure repository
        this.InitializeSourceControl();
        this.Repo.Config.Set("user.name", this.Signer.Name, ConfigurationLevel.Local);
        this.Repo.Config.Set("user.email", this.Signer.Email, ConfigurationLevel.Local);

        var initialVersionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse(initialVersion),
            Release = new ReleaseOptions()
            {
                BranchName = releaseOptionsBranchName
            }
        };

        var expectedVersionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse(resultingVersion),
            Release = new ReleaseOptions()
            {
                BranchName = releaseOptionsBranchName
            }
        };

        // create version.json 
        this.WriteVersionFile(initialVersionOptions);

        // switch to release branch
        var branchName = string.Format(releaseOptionsBranchName, initialVersionOptions.Version.Version);
        Commands.Checkout(this.Repo, this.Repo.CreateBranch(branchName));

        var tipBeforePrepareRelease = this.Repo.Head.Tip;

        // run PrepareRelease
        var releaseManager = new ReleaseManager();
        releaseManager.PrepareRelease(this.RepoPath, releaseUnstableTag);

        // Check if a commit was created
        {
            var updateVersionCommit = this.Repo.Head.Tip;
            Assert.NotEqual(tipBeforePrepareRelease.Id, updateVersionCommit.Id);
            Assert.Single(updateVersionCommit.Parents);
            Assert.Equal(updateVersionCommit.Parents.Single().Id, tipBeforePrepareRelease.Id);
        }

        // check version on release branch
        {
            var actualVersionOptions = VersionFile.GetVersion(this.Repo.Branches[branchName].Tip);
            Assert.Equal(expectedVersionOptions, actualVersionOptions);
        }
    }

    [Theory]
    [InlineData("1.2", "rc")]
    [InlineData("1.2+metadata", "rc")]
    public void PrepeareRelease_OnReleaseBranchWithVersionDecrement(string initialVersion, string releaseUnstableTag)
    {
        // create and configure repository
        this.InitializeSourceControl();
        this.Repo.Config.Set("user.name", this.Signer.Name, ConfigurationLevel.Local);
        this.Repo.Config.Set("user.email", this.Signer.Email, ConfigurationLevel.Local);

        // create version.json
        var versionOptions = new VersionOptions() { Version = SemanticVersion.Parse(initialVersion) };
        this.WriteVersionFile(versionOptions);

        // switch to release branch
        var branchName = string.Format(versionOptions.ReleaseOrDefault.BranchNameOrDefault, versionOptions.Version.Version);
        Commands.Checkout(this.Repo, this.Repo.CreateBranch(branchName));

        // running PrepareRelease should result in an error 
        // because we're trying to add a prerelease tag to a version without prerelease tag
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath, releaseUnstableTag), ReleasePreparationError.VersionDecrement);
    }


    [Theory]
    [InlineData("1.2-pre", null, ReleaseVersionIncrement.Minor, "pre", null, "1.2", "1.3-pre")]
    [InlineData("1.2-pre", null, ReleaseVersionIncrement.Major, "pre", null, "1.2", "2.2-pre")]
    [InlineData("1.2-pre", "v{0}", ReleaseVersionIncrement.Minor, "pre", null, "1.2", "1.3-pre")]
    [InlineData("1.2-pre+metadata", null, ReleaseVersionIncrement.Minor, "pre", null, "1.2+metadata", "1.3-pre+metadata")]
    [InlineData("1.2-rc.{height}", null, ReleaseVersionIncrement.Minor, "beta", null, "1.2", "1.3-beta.{height}")]
    [InlineData("1.2-rc.{height}", null, ReleaseVersionIncrement.Minor, "-beta", null, "1.2", "1.3-beta.{height}")]
    [InlineData("1.2-beta.{height}", null, ReleaseVersionIncrement.Minor, "alpha", "rc", "1.2-rc.{height}", "1.3-alpha.{height}")]
    [InlineData("1.2-beta", null, ReleaseVersionIncrement.Minor, "alpha", "rc", "1.2-rc", "1.3-alpha")]
    [InlineData("1.2-beta+metadata", null, ReleaseVersionIncrement.Minor, "alpha", "rc", "1.2-rc+metadata", "1.3-alpha+metadata")]
    public void PrepareRelease_OnMaster(
        // data for initial setup (version and release options configured in version.json)
        string initialVersion,
        string releaseOptionsBranchName,
        ReleaseVersionIncrement releaseOptionsVersionIncrement,
        string releaseOptionsFirstUnstableTag,
        // arguments passed to PrepareRelease()
        string releaseUnstableTag,
        // expected versions after running PrepareRelease()
        string resultingReleaseVersion,
        string resultingMainVersion)
    {
        releaseOptionsBranchName = releaseOptionsBranchName ?? new ReleaseOptions().BranchNameOrDefault;

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
                VersionIncrement = releaseOptionsVersionIncrement,
                BranchName = releaseOptionsBranchName,
                FirstUnstableTag = releaseOptionsFirstUnstableTag
            }
        };
        this.WriteVersionFile(initialVersionOptions);

        var expectedVersionOptionsReleaseBranch = new VersionOptions()
        {
            Version = SemanticVersion.Parse(resultingReleaseVersion),
            Release = new ReleaseOptions()
            {
                VersionIncrement = releaseOptionsVersionIncrement,
                BranchName = releaseOptionsBranchName,
                FirstUnstableTag = releaseOptionsFirstUnstableTag
            }
        };

        var expectedVersionOptionsCurrentBrach = new VersionOptions()
        {
            Version = SemanticVersion.Parse(resultingMainVersion),
            Release = new ReleaseOptions()
            {
                VersionIncrement = releaseOptionsVersionIncrement,
                BranchName = releaseOptionsBranchName,
                FirstUnstableTag = releaseOptionsFirstUnstableTag
            }
        };

        var expectedBranchName = string.Format(releaseOptionsBranchName, expectedVersionOptionsReleaseBranch.Version.Version);
        var initialBranchName = this.Repo.Head.FriendlyName;
        var tipBeforePrepareRelease = this.Repo.Head.Tip;

        // prepare release
        var releaseManager = new ReleaseManager();
        releaseManager.PrepareRelease(this.RepoPath, releaseUnstableTag);

        // check if a branch was created
        Assert.Contains(this.Repo.Branches, branch => branch.FriendlyName == expectedBranchName);

        // PrepareRelease should switch back to the initial branch
        Assert.Equal(initialBranchName, this.Repo.Head.FriendlyName);

        // check if release branch contains a new commit 
        // parent of new commit must be the commit before preparing the release
        var releaseBranch = this.Repo.Branches[expectedBranchName];
        {
            Assert.NotEqual(releaseBranch.Tip.Id, tipBeforePrepareRelease.Id);
            Assert.Equal(releaseBranch.Tip.Parents.Single().Id, tipBeforePrepareRelease.Id);
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
            Assert.Equal(updateVersionCommit.Parents.Single().Id, tipBeforePrepareRelease.Id);
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
    public void PrepareRelease_OnMasterWithVersionDecrement(string initialVersion, string releaseUnstableTag)
    {
        // create and configure repository
        this.InitializeSourceControl();
        this.Repo.Config.Set("user.name", this.Signer.Name, ConfigurationLevel.Local);
        this.Repo.Config.Set("user.email", this.Signer.Email, ConfigurationLevel.Local);

        // create version.json
        var versionOptions = new VersionOptions() { Version = SemanticVersion.Parse(initialVersion) };
        this.WriteVersionFile(versionOptions);

        // running PrepareRelease should result in an error 
        // because we're trying to add a prerelease tag to a version without prerelease tag
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath, releaseUnstableTag), ReleasePreparationError.VersionDecrement);
    }


    private void AssertError(Action testCode, ReleasePreparationError expectedError)
    {
        var exception = Record.Exception(testCode);

        Assert.NotNull(exception);
        Assert.IsType<ReleasePreparationException>(exception);

        Assert.Equal(expectedError, ((ReleasePreparationException)exception).Error);
    }
}
