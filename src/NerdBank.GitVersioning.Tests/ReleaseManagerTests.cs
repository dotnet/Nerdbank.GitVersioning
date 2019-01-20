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
    [InlineData(null)]
    [InlineData("v{0}")]
    public void PrepareRelease_OnReleaseBranch(string releaseBranchFormat)
    {
        var version = "1.2";
        releaseBranchFormat = releaseBranchFormat ?? new ReleaseOptions().BranchNameOrDefault;

        this.InitializeSourceControl();
        this.WriteVersionFile(new VersionOptions()
        {
            Version = new SemanticVersion(version),
            Release = new ReleaseOptions()
            {
                BranchName = releaseBranchFormat
            }
        });
        

        var branch = this.Repo.CreateBranch(string.Format(releaseBranchFormat, version));
        Commands.Checkout(this.Repo, branch);

        this.AssertError(() => ReleaseManager.PrepareRelease(this.RepoPath), ReleasePreparationError.OnReleaseBranch);
    }

    [Theory]
    [InlineData("1.2-pre", "1.2", "1.3-pre", ReleaseVersionIncrement.Minor, null, "pre")]
    [InlineData("1.2-pre", "1.2", "2.2-pre", ReleaseVersionIncrement.Major, null, "pre")]
    [InlineData("1.2-pre", "1.2", "1.3-pre", ReleaseVersionIncrement.Minor, "v{0}", "pre")]
    [InlineData("1.2-pre+metadata", "1.2", "1.3-pre+metadata", ReleaseVersionIncrement.Minor, null, "pre")]
    [InlineData("1.2-rc.{height}", "1.2", "1.3-beta.{height}", ReleaseVersionIncrement.Minor, null, "beta")]
    [InlineData("1.2-rc.{height}", "1.2", "1.3-beta.{height}", ReleaseVersionIncrement.Minor, null, "-beta")]
    //TODO: more test cases (different release settings)
    public void PrepareRelease_OnMaster(
        string initialVersion, 
        string releaseVersion, 
        string nextVersion, 
        ReleaseVersionIncrement versionIncrement, 
        string releaseBranchName,
        string firstUnstableTag)
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

        var expectedBranchName = string.Format(releaseBranchName, releaseVersion);
        var initialBranchName = this.Repo.Head.FriendlyName;
        var tipBeforeRelease = this.Repo.Head.Tip;

        // prepare release
        ReleaseManager.PrepareRelease(this.RepoPath);

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


    private void AssertError(Action testCode, ReleasePreparationError expectedError)
    {
        var exception = Record.Exception(testCode);

        Assert.NotNull(exception);
        Assert.IsType<ReleasePreparationException>(exception);

        Assert.Equal(expectedError, ((ReleasePreparationException)exception).Error);
    }
}
