using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Xunit;
using Xunit.Abstractions;

using ReleaseOptions = Nerdbank.GitVersioning.VersionOptions.ReleaseOptions;
using ReleasePreparationError = Nerdbank.GitVersioning.ReleaseManager.ReleasePreparationError;
using ReleasePreparationException = Nerdbank.GitVersioning.ReleaseManager.ReleasePreparationException;
using ReleaseVersionIncrement = Nerdbank.GitVersioning.VersionOptions.ReleaseVersionIncrement;
using Version = System.Version;

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

    [Fact]
    public void PrepareRelease_InvalidVersionFieldCountSetting()
    {
        this.InitializeSourceControl();

        // create version.json
        var versionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse("1.2-pre"),
            Release = new ReleaseOptions()
            {
                VersionFieldCount = 3
            }
        };
        this.WriteVersionFile(versionOptions);

        // running PrepareRelease should result in an error
        // because the branchName does not have a placeholder for the version
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath), ReleasePreparationError.InvalidVersionFieldCountSetting);
    }

    [Fact]
    public void PrepareRelease_ReleaseBranchAlreadyExists()
    {
        this.InitializeSourceControl();

        // create version.json
        var versionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse("1.2-pre"),
            Release = new ReleaseOptions()
            {
                BranchName = "release/v{version}",
            }
        };

        this.WriteVersionFile(versionOptions);

        this.Repo.CreateBranch("release/v1.2");

        // running PrepareRelease should result in an error
        // because the release branch already exists
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath), ReleasePreparationError.BranchAlreadyExists);
    }

    [Theory]
    // base test cases
    [InlineData("1.2-beta", null, null, "v1.2", "1.2")]
    [InlineData("1.2-beta.{height}", null, null, "v1.2", "1.2")]
    [InlineData("1.2-beta", null, "rc", "v1.2", "1.2-rc")]
    [InlineData("1.2-beta.{height}", null, "rc", "v1.2", "1.2-rc.{height}")]
    // modify release.branchName
    [InlineData("1.2-beta", "v{version}release", null, "v1.2release", "1.2")]
    [InlineData("1.2-beta.{height}", "v{version}release", null, "v1.2release", "1.2")]
    [InlineData("1.2-beta", "v{version}release", "rc", "v1.2release", "1.2-rc")]
    [InlineData("1.2-beta.{height}", "v{version}release", "rc", "v1.2release", "1.2-rc.{height}")]
    public void PrepareRelease_ReleaseBranch(string initialVersion, string releaseOptionsBranchName, string releaseUnstableTag, string releaseBranchName, string resultingVersion)
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
        var branchName = releaseBranchName;
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
    [InlineData("1.2", "rc", "release/v1.2")]
    [InlineData("1.2+metadata", "rc", "release/v1.2")]
    public void PrepeareRelease_ReleaseBranchWithVersionDecrement(string initialVersion, string releaseUnstableTag, string branchName)
    {
        // create and configure repository
        this.InitializeSourceControl();
        this.Repo.Config.Set("user.name", this.Signer.Name, ConfigurationLevel.Local);
        this.Repo.Config.Set("user.email", this.Signer.Email, ConfigurationLevel.Local);

        // create version.json
        var versionOptions = new VersionOptions() { Version = SemanticVersion.Parse(initialVersion) };
        this.WriteVersionFile(versionOptions);

        // switch to release branch
        Commands.Checkout(this.Repo, this.Repo.CreateBranch(branchName));

        // running PrepareRelease should result in an error
        // because we're trying to add a prerelease tag to a version without prerelease tag
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath, releaseUnstableTag), ReleasePreparationError.VersionDecrement);
    }


    [Theory]
    // base test cases
    [InlineData("1.2-beta", null, null, null, 0, null, null, null, "v1.2", "1.2", "1.3-alpha")]
    [InlineData("1.2-beta", null, null, null, 0, "rc", null, null, "v1.2", "1.2-rc", "1.3-alpha")]
    [InlineData("1.2-beta.{height}", null, null, null, 0, null, null, null, "v1.2", "1.2", "1.3-alpha.{height}")]
    [InlineData("1.2-beta.{height}", null, null, null, 0, "rc", null, null, "v1.2", "1.2-rc.{height}", "1.3-alpha.{height}")]
    // modify release.branchName
    [InlineData("1.2-beta", "v{version}release", ReleaseVersionIncrement.Minor, "alpha", 0, null, null, null, "v1.2release", "1.2", "1.3-alpha")]
    [InlineData("1.2-beta", "v{version}release", ReleaseVersionIncrement.Minor, "alpha", 0, "rc", null, null, "v1.2release", "1.2-rc", "1.3-alpha")]
    [InlineData("1.2-beta.{height}", "v{version}release", ReleaseVersionIncrement.Minor, "alpha", 0, null, null, null, "v1.2release", "1.2", "1.3-alpha.{height}")]
    [InlineData("1.2-beta.{height}", "v{version}release", ReleaseVersionIncrement.Minor, "alpha", 0, "rc", null, null, "v1.2release", "1.2-rc.{height}", "1.3-alpha.{height}")]
    // modify release.versionIncrement: "Major"
    [InlineData("1.2-beta", null, ReleaseVersionIncrement.Major, "alpha", 0, null, null, null, "v1.2", "1.2", "2.0-alpha")]
    [InlineData("1.2-beta", null, ReleaseVersionIncrement.Major, "alpha", 0, "rc", null, null, "v1.2", "1.2-rc", "2.0-alpha")]
    [InlineData("1.2-beta.{height}", null, ReleaseVersionIncrement.Major, "alpha", 0, null, null, null, "v1.2", "1.2", "2.0-alpha.{height}")]
    [InlineData("1.2-beta.{height}", null, ReleaseVersionIncrement.Major, "alpha", 0, "rc", null, null, "v1.2", "1.2-rc.{height}", "2.0-alpha.{height}")]
    // modify release.versionIncrement: "Build"
    [InlineData("1.2.3-beta", null, ReleaseVersionIncrement.Build, "alpha", 0, null, null, null, "v1.2.3", "1.2.3", "1.2.4-alpha")]
    [InlineData("1.2.3-beta", null, ReleaseVersionIncrement.Build, "alpha", 0, "rc", null, null, "v1.2.3", "1.2.3-rc", "1.2.4-alpha")]
    [InlineData("1.2.3-beta.{height}", null, ReleaseVersionIncrement.Build, "alpha", 0, null, null, null, "v1.2.3", "1.2.3", "1.2.4-alpha.{height}")]
    [InlineData("1.2.3-beta.{height}", null, ReleaseVersionIncrement.Build, "alpha", 0, "rc", null, null, "v1.2.3", "1.2.3-rc.{height}", "1.2.4-alpha.{height}")]
    // modify release.firstUnstableTag
    [InlineData("1.2-beta", null, ReleaseVersionIncrement.Minor, "preview", 0, null, null, null, "v1.2", "1.2", "1.3-preview")]
    [InlineData("1.2-beta", null, ReleaseVersionIncrement.Minor, "preview", 0, "rc", null, null, "v1.2", "1.2-rc", "1.3-preview")]
    [InlineData("1.2-beta.{height}", null, ReleaseVersionIncrement.Minor, "preview", 0, null, null, null, "v1.2", "1.2", "1.3-preview.{height}")]
    [InlineData("1.2-beta.{height}", null, ReleaseVersionIncrement.Minor, "preview", 0, "rc", null, null, "v1.2", "1.2-rc.{height}", "1.3-preview.{height}")]
    // include build metadata in version
    [InlineData("1.2-beta+metadata", null, ReleaseVersionIncrement.Minor, "alpha", 0, null, null, null, "v1.2", "1.2+metadata", "1.3-alpha+metadata")]
    [InlineData("1.2-beta+metadata", null, ReleaseVersionIncrement.Minor, "alpha", 0, "rc", null, null, "v1.2", "1.2-rc+metadata", "1.3-alpha+metadata")]
    [InlineData("1.2-beta.{height}+metadata", null, ReleaseVersionIncrement.Minor, "alpha", 0, null, null, null, "v1.2", "1.2+metadata", "1.3-alpha.{height}+metadata")]
    [InlineData("1.2-beta.{height}+metadata", null, ReleaseVersionIncrement.Minor, "alpha", 0, "rc", null, null, "v1.2", "1.2-rc.{height}+metadata", "1.3-alpha.{height}+metadata")]
    // versions without prerelease tags
    [InlineData("1.2", null, ReleaseVersionIncrement.Minor, "alpha", 0, null, null, null, "v1.2", "1.2", "1.3-alpha")]
    [InlineData("1.2", null, ReleaseVersionIncrement.Major, "alpha", 0, null, null, null, "v1.2", "1.2", "2.0-alpha")]
    // explicitly set next version
    [InlineData("1.2-beta", null, null, null, 0, null, "4.5", null, "v1.2", "1.2", "4.5-alpha")]
    [InlineData("1.2-beta.{height}", null, null, null, 0, null, "4.5", null, "v1.2", "1.2", "4.5-alpha.{height}")]
    [InlineData("1.2-beta.{height}", null, null, "pre", 0, null, "4.5.6", null, "v1.2", "1.2", "4.5.6-pre.{height}")]
    // explicitly set version increment overriding the setting from ReleaseOptions 
    [InlineData("1.2-beta", null, ReleaseVersionIncrement.Minor, null, 0, null, null, ReleaseVersionIncrement.Major, "v1.2", "1.2", "2.0-alpha")]
    [InlineData("1.2.3-beta", null, ReleaseVersionIncrement.Minor, null, 0, null, null, ReleaseVersionIncrement.Build, "v1.2.3", "1.2.3", "1.2.4-alpha")]
    // Verify branch names take into account the VersionFieldCount setting
    [InlineData("1.2.3-beta", null, ReleaseVersionIncrement.Minor, null, 2, null, null, ReleaseVersionIncrement.Build, "v1.2", "1.2.3", "1.2.4-alpha")]
    [InlineData("1.2-beta", null, ReleaseVersionIncrement.Minor, null, 1, null, null, ReleaseVersionIncrement.Major, "v1", "1.2", "2.0-alpha")]
    [InlineData("1.2.0-beta.{height}", null, null, "pre", 2, null, "4.5.6", null, "v1.2", "1.2.0", "4.5.6-pre.{height}")]
    [InlineData("1.2.0-beta.{height}", null, null, "pre", 3, null, "4.5.6", null, "v1.2.0", "1.2.0", "4.5.6-pre.{height}")]
    public void PrepareRelease_Master(
        // data for initial setup (version and release options configured in version.json)
        string initialVersion,
        string releaseOptionsBranchName,
        ReleaseVersionIncrement? releaseOptionsVersionIncrement,
        string releaseOptionsFirstUnstableTag,
        int releaseOptionsVersionFieldCount,
        // arguments passed to PrepareRelease()
        string releaseUnstableTag,
        string nextVersion,
        ReleaseVersionIncrement? parameterVersionIncrement,
        // expected versions and branch name after running PrepareRelease()
        string expectedBranchName,
        string resultingReleaseVersion,
        string resultingMainVersion)
    {
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
                FirstUnstableTag = releaseOptionsFirstUnstableTag,
                VersionFieldCount = releaseOptionsVersionFieldCount
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
                FirstUnstableTag = releaseOptionsFirstUnstableTag,
                VersionFieldCount = releaseOptionsVersionFieldCount
            }
        };

        var expectedVersionOptionsCurrentBrach = new VersionOptions()
        {
            Version = SemanticVersion.Parse(resultingMainVersion),
            Release = new ReleaseOptions()
            {
                VersionIncrement = releaseOptionsVersionIncrement,
                BranchName = releaseOptionsBranchName,
                FirstUnstableTag = releaseOptionsFirstUnstableTag,
                VersionFieldCount = releaseOptionsVersionFieldCount
            }
        };

        var initialBranchName = this.Repo.Head.FriendlyName;
        var tipBeforePrepareRelease = this.Repo.Head.Tip;

        // prepare release
        var releaseManager = new ReleaseManager();
        releaseManager.PrepareRelease(this.RepoPath, releaseUnstableTag, (nextVersion == null ? null : Version.Parse(nextVersion)), parameterVersionIncrement);

        // check if a branch was created
        Assert.Contains(this.Repo.Branches, branch => branch.FriendlyName == expectedBranchName);

        // PrepareRelease should switch back to the initial branch
        Assert.Equal(initialBranchName, this.Repo.Head.FriendlyName);

        // check if release branch contains a new commit
        // parent of new commit must be the commit before preparing the release
        var releaseBranch = this.Repo.Branches[expectedBranchName];
        {
            // If the original branch had no -prerelease tag, the release branch has no commit to author.
            if (string.IsNullOrEmpty(initialVersionOptions.Version.Prerelease))
            {
                Assert.Equal(releaseBranch.Tip.Id, tipBeforePrepareRelease.Id);
            }
            else
            {
                Assert.NotEqual(releaseBranch.Tip.Id, tipBeforePrepareRelease.Id);
                Assert.Equal(releaseBranch.Tip.Parents.Single().Id, tipBeforePrepareRelease.Id);
            }
        }

        if (string.IsNullOrEmpty(initialVersionOptions.Version.Prerelease))
        {
            // Verify that one commit was authored.
            var incrementCommit = this.Repo.Head.Tip;
            Assert.Single(incrementCommit.Parents);
            Assert.Equal(tipBeforePrepareRelease.Id, incrementCommit.Parents.Single().Id);
        }
        else
        {
            // check if current branch contains new commits
            // - one commit that updates the version (parent must be the commit before preparing the release)
            // - one commit merging the release branch back to master and resolving the conflict
            var mergeCommit = this.Repo.Head.Tip;
            Assert.Equal(2, mergeCommit.Parents.Count());
            Assert.Equal(releaseBranch.Tip.Id, mergeCommit.Parents.Skip(1).First().Id);

            var updateVersionCommit = mergeCommit.Parents.First();
            Assert.Single(updateVersionCommit.Parents);
            Assert.Equal(tipBeforePrepareRelease.Id, updateVersionCommit.Parents.First().Id);
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
    [InlineData("1.2", "rc", null)]
    [InlineData("1.2+metadata", "rc", null)]
    [InlineData("1.2+metadata", null, "0.9")]
    public void PrepareRelease_MasterWithVersionDecrement(string initialVersion, string releaseUnstableTag, string nextVersion)
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
        this.AssertError(
            () => new ReleaseManager().PrepareRelease(this.RepoPath, releaseUnstableTag, (nextVersion == null ? null : Version.Parse(nextVersion))),
            ReleasePreparationError.VersionDecrement);
    }

    [Fact]
    public void PrepareRelease_DetachedHead()
    {
        this.InitializeSourceControl();
        this.WriteVersionFile("1.0", "-alpha");
        Commands.Checkout(this.Repo, this.Repo.Head.Commits.First());
        var ex = Assert.Throws<ReleasePreparationException>(() => new ReleaseManager().PrepareRelease(this.RepoPath));
        Assert.Equal(ReleasePreparationError.DetachedHead, ex.Error);
    }

    [Fact]
    public void PrepareRelease_InvalidVersionIncrement()
    {
        // create and configure repository
        this.InitializeSourceControl();
        this.Repo.Config.Set("user.name", this.Signer.Name, ConfigurationLevel.Local);
        this.Repo.Config.Set("user.email", this.Signer.Email, ConfigurationLevel.Local);

        // create version.json
        var versionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse("1.2"),
            Release = new ReleaseOptions() { VersionIncrement = ReleaseVersionIncrement.Build }
        };
        this.WriteVersionFile(versionOptions);

        // running PrepareRelease should result in an error
        // because a 2-segment version is incompatibale with a increment setting of "build"
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath), ReleasePreparationError.InvalidVersionIncrementSetting);
    }

    private void AssertError(Action testCode, ReleasePreparationError expectedError)
    {
        var ex = Assert.Throws<ReleasePreparationException>(testCode);
        Assert.Equal(expectedError, ex.Error);
    }
}
