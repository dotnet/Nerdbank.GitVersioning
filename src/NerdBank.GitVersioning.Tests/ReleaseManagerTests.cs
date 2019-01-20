using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.Tests;
using Xunit;
using Xunit.Abstractions;
using static Nerdbank.GitVersioning.ReleaseManager;

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

    [Theory]
    [InlineData(null)]
    [InlineData("v{0}")]
    public void PrepareRelease_OnReleaseBranch(string releaseBranchFormat)
    {
        var version = "1.2";
        releaseBranchFormat = releaseBranchFormat ?? new VersionOptions.ReleaseOptions().BranchNameOrDefault;

        this.InitializeSourceControl();
        this.WriteVersionFile(new VersionOptions()
        {
            Version = new SemanticVersion(version),
            Release = new VersionOptions.ReleaseOptions()
            {
                BranchName = releaseBranchFormat
            }
        });
        

        var branch = this.Repo.CreateBranch(String.Format(releaseBranchFormat, version));
        Commands.Checkout(this.Repo, branch);

        this.AssertError(() => ReleaseManager.PrepareRelease(this.RepoPath), ReleasePreparationError.OnReleaseBranch);
    }

    [Fact]
    //TODO: more test cases (different release settings)
    public void PrepareRelease_OnMaster()
    {
        var version = SemanticVersion.Parse("1.2-pre");
        var nextVersion = SemanticVersion.Parse("1.3-pre");

        // create and configure repository
        this.InitializeSourceControl();
        this.Repo.Config.Set("user.name", this.Signer.Name, ConfigurationLevel.Local);
        this.Repo.Config.Set("user.email", this.Signer.Email, ConfigurationLevel.Local);
        
        // create version.json
        var versionOptions = new VersionOptions()
        {
            Version = version,
            Release = new VersionOptions.ReleaseOptions()
        };
        this.WriteVersionFile(versionOptions);

        var expectedBranchName = string.Format(versionOptions.ReleaseOrDefault.BranchNameOrDefault, version.Version);

        var tipBeforeRelease = this.Repo.Branches["master"].Tip;

        // prepare release
        ReleaseManager.PrepareRelease(this.RepoPath);

        // check if a branch was created
        Assert.Contains(this.Repo.Branches, branch => branch.FriendlyName == expectedBranchName);        
        
        // check if release branch contains a new commit 
        // parent of new commit must be the commit before preparing the release
        var releaseBranch = this.Repo.Branches[expectedBranchName];
        Assert.NotEqual(releaseBranch.Tip.Id, tipBeforeRelease.Id);
        Assert.Equal(releaseBranch.Tip.Parents.Single().Id, tipBeforeRelease.Id);

        // check if master branch contains a new commit
        var masterBranch = this.Repo.Branches["master"];
        Assert.NotEqual(masterBranch.Tip.Id, tipBeforeRelease.Id);
        Assert.Equal(masterBranch.Tip.Parents.Single().Id, tipBeforeRelease.Id);

        // check version on release branch
        var releaseBranchVersion = VersionFile.GetVersion(releaseBranch.Tip);
        Assert.Equal(version.Version.ToString(), releaseBranchVersion.Version.ToString());

        // check version on master branch
        var masterBranchVersion = VersionFile.GetVersion(masterBranch.Tip);
        Assert.Equal(nextVersion.ToString(), masterBranchVersion.Version.ToString());
    }


    private void AssertError(Action testCode, ReleasePreparationError expectedError)
    {
        var exception = Record.Exception(testCode);

        Assert.NotNull(exception);
        Assert.IsType<ReleasePreparationException>(exception);

        Assert.Equal(expectedError, ((ReleasePreparationException)exception).Error);
    }
}
