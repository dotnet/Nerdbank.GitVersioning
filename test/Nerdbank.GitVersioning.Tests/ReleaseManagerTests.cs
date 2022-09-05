// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

using ReleaseOptions = Nerdbank.GitVersioning.VersionOptions.ReleaseOptions;
using ReleasePreparationError = Nerdbank.GitVersioning.ReleaseManager.ReleasePreparationError;
using ReleasePreparationException = Nerdbank.GitVersioning.ReleaseManager.ReleasePreparationException;
using ReleaseVersionIncrement = Nerdbank.GitVersioning.VersionOptions.ReleaseVersionIncrement;
using Version = System.Version;

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1649 // File name should match first type name

[Trait("Engine", "Managed")]
public class ReleaseManagerManagedTests : ReleaseManagerTests
{
    public ReleaseManagerManagedTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <inheritdoc/>
    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, writable: false);
}

[Trait("Engine", "LibGit2")]
public class ReleaseManagerLibGit2Tests : ReleaseManagerTests
{
    public ReleaseManagerLibGit2Tests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <inheritdoc/>
    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, writable: true);
}

public abstract class ReleaseManagerTests : RepoTestBase
{
    public ReleaseManagerTests(ITestOutputHelper logger)
        : base(logger)
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
        File.WriteAllText(Path.Combine(this.RepoPath, "file1.txt"), string.Empty);

        // running PrepareRelease should result in an error
        // because there is a new file not under version control
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath), ReleasePreparationError.UncommittedChanges);
    }

    [Fact]
    public void PrepareRelease_DirtyIndex()
    {
        this.InitializeSourceControl();

        // create a file and stage it
        string filePath = Path.Combine(this.RepoPath, "file1.txt");
        File.WriteAllText(filePath, string.Empty);
        Commands.Stage(this.LibGit2Repository, filePath);

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
            },
        };
        this.WriteVersionFile(versionOptions);

        // running PrepareRelease should result in an error
        // because the branchName does not have a placeholder for the version
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath), ReleasePreparationError.InvalidBranchNameSetting);
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
            },
        };

        this.WriteVersionFile(versionOptions);

        this.LibGit2Repository.CreateBranch("release/v1.2");

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

        var initialVersionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse(initialVersion),
            Release = new ReleaseOptions()
            {
                BranchName = releaseOptionsBranchName,
            },
        };

        var expectedVersionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse(resultingVersion),
            Release = new ReleaseOptions()
            {
                BranchName = releaseOptionsBranchName,
            },
        };

        // create version.json
        this.WriteVersionFile(initialVersionOptions);

        // switch to release branch
        string branchName = releaseBranchName;
        Commands.Checkout(this.LibGit2Repository, this.LibGit2Repository.CreateBranch(branchName));

        Commit tipBeforePrepareRelease = this.LibGit2Repository.Head.Tip;

        // run PrepareRelease
        var releaseManager = new ReleaseManager();
        releaseManager.PrepareRelease(this.RepoPath, releaseUnstableTag);

        // Check if a commit was created
        {
            Commit updateVersionCommit = this.LibGit2Repository.Head.Tip;
            Assert.NotEqual(tipBeforePrepareRelease.Id, updateVersionCommit.Id);
            Assert.Single(updateVersionCommit.Parents);
            Assert.Equal(updateVersionCommit.Parents.Single().Id, tipBeforePrepareRelease.Id);
        }

        // check version on release branch
        {
            VersionOptions actualVersionOptions = this.GetVersionOptions(committish: this.LibGit2Repository.Branches[branchName].Tip.Sha);
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

        // create version.json
        var versionOptions = new VersionOptions() { Version = SemanticVersion.Parse(initialVersion) };
        this.WriteVersionFile(versionOptions);

        // switch to release branch
        Commands.Checkout(this.LibGit2Repository, this.LibGit2Repository.CreateBranch(branchName));

        // running PrepareRelease should result in an error
        // because we're trying to add a prerelease tag to a version without prerelease tag
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath, releaseUnstableTag), ReleasePreparationError.VersionDecrement);
    }

#pragma warning disable SA1114 // Parameter list should follow declaration
    [Theory]
    // base test cases
    [InlineData("1.2-beta", null, null, null, null, null, null, "v1.2", "1.2", "1.3-alpha")]
    [InlineData("1.2-beta", null, null, null, "rc", null, null, "v1.2", "1.2-rc", "1.3-alpha")]
    [InlineData("1.2-beta.{height}", null, null, null, null, null, null, "v1.2", "1.2", "1.3-alpha.{height}")]
    [InlineData("1.2-beta.{height}", null, null, null, "rc", null, null, "v1.2", "1.2-rc.{height}", "1.3-alpha.{height}")]
    // modify release.branchName
    [InlineData("1.2-beta", "v{version}release", ReleaseVersionIncrement.Minor, "alpha", null, null, null, "v1.2release", "1.2", "1.3-alpha")]
    [InlineData("1.2-beta", "v{version}release", ReleaseVersionIncrement.Minor, "alpha", "rc", null, null, "v1.2release", "1.2-rc", "1.3-alpha")]
    [InlineData("1.2-beta.{height}", "v{version}release", ReleaseVersionIncrement.Minor, "alpha", null, null, null, "v1.2release", "1.2", "1.3-alpha.{height}")]
    [InlineData("1.2-beta.{height}", "v{version}release", ReleaseVersionIncrement.Minor, "alpha", "rc", null, null, "v1.2release", "1.2-rc.{height}", "1.3-alpha.{height}")]
    // modify release.versionIncrement: "Major"
    [InlineData("1.2-beta", null, ReleaseVersionIncrement.Major, "alpha", null, null, null, "v1.2", "1.2", "2.0-alpha")]
    [InlineData("1.2-beta", null, ReleaseVersionIncrement.Major, "alpha", "rc", null, null, "v1.2", "1.2-rc", "2.0-alpha")]
    [InlineData("1.2-beta.{height}", null, ReleaseVersionIncrement.Major, "alpha", null, null, null, "v1.2", "1.2", "2.0-alpha.{height}")]
    [InlineData("1.2-beta.{height}", null, ReleaseVersionIncrement.Major, "alpha", "rc", null, null, "v1.2", "1.2-rc.{height}", "2.0-alpha.{height}")]
    // modify release.versionIncrement: "Build"
    [InlineData("1.2.3-beta", null, ReleaseVersionIncrement.Build, "alpha", null, null, null, "v1.2.3", "1.2.3", "1.2.4-alpha")]
    [InlineData("1.2.3-beta", null, ReleaseVersionIncrement.Build, "alpha", "rc", null, null, "v1.2.3", "1.2.3-rc", "1.2.4-alpha")]
    [InlineData("1.2.3-beta.{height}", null, ReleaseVersionIncrement.Build, "alpha", null, null, null, "v1.2.3", "1.2.3", "1.2.4-alpha.{height}")]
    [InlineData("1.2.3-beta.{height}", null, ReleaseVersionIncrement.Build, "alpha", "rc", null, null, "v1.2.3", "1.2.3-rc.{height}", "1.2.4-alpha.{height}")]
    // modify release.firstUnstableTag
    [InlineData("1.2-beta", null, ReleaseVersionIncrement.Minor, "preview", null, null, null, "v1.2", "1.2", "1.3-preview")]
    [InlineData("1.2-beta", null, ReleaseVersionIncrement.Minor, "preview", "rc", null, null, "v1.2", "1.2-rc", "1.3-preview")]
    [InlineData("1.2-beta.{height}", null, ReleaseVersionIncrement.Minor, "preview", null, null, null, "v1.2", "1.2", "1.3-preview.{height}")]
    [InlineData("1.2-beta.{height}", null, ReleaseVersionIncrement.Minor, "preview", "rc", null, null, "v1.2", "1.2-rc.{height}", "1.3-preview.{height}")]
    // include build metadata in version
    [InlineData("1.2-beta+metadata", null, ReleaseVersionIncrement.Minor, "alpha", null, null, null, "v1.2", "1.2+metadata", "1.3-alpha+metadata")]
    [InlineData("1.2-beta+metadata", null, ReleaseVersionIncrement.Minor, "alpha", "rc", null, null, "v1.2", "1.2-rc+metadata", "1.3-alpha+metadata")]
    [InlineData("1.2-beta.{height}+metadata", null, ReleaseVersionIncrement.Minor, "alpha", null, null, null, "v1.2", "1.2+metadata", "1.3-alpha.{height}+metadata")]
    [InlineData("1.2-beta.{height}+metadata", null, ReleaseVersionIncrement.Minor, "alpha", "rc", null, null, "v1.2", "1.2-rc.{height}+metadata", "1.3-alpha.{height}+metadata")]
    // versions without prerelease tags
    [InlineData("1.2", null, ReleaseVersionIncrement.Minor, "alpha", null, null, null, "v1.2", "1.2", "1.3-alpha")]
    [InlineData("1.2", null, ReleaseVersionIncrement.Major, "alpha", null, null, null, "v1.2", "1.2", "2.0-alpha")]
    // explicitly set next version
    [InlineData("1.2-beta", null, null, null, null, "4.5", null, "v1.2", "1.2", "4.5-alpha")]
    [InlineData("1.2-beta.{height}", null, null, null, null, "4.5", null, "v1.2", "1.2", "4.5-alpha.{height}")]
    [InlineData("1.2-beta.{height}", null, null, "pre", null, "4.5.6", null, "v1.2", "1.2", "4.5.6-pre.{height}")]
    // explicitly set version increment overriding the setting from ReleaseOptions
    [InlineData("1.2-beta", null, ReleaseVersionIncrement.Minor, null, null, null, ReleaseVersionIncrement.Major, "v1.2", "1.2", "2.0-alpha")]
    [InlineData("1.2.3-beta", null, ReleaseVersionIncrement.Minor, null, null, null, ReleaseVersionIncrement.Build, "v1.2.3", "1.2.3", "1.2.4-alpha")]
    public void PrepareRelease_Master(
        // data for initial setup (version and release options configured in version.json)
        string initialVersion,
        string releaseOptionsBranchName,
        ReleaseVersionIncrement? releaseOptionsVersionIncrement,
        string releaseOptionsFirstUnstableTag,
        // arguments passed to PrepareRelease()
        string releaseUnstableTag,
        string nextVersion,
        ReleaseVersionIncrement? parameterVersionIncrement,
        // expected versions and branch name after running PrepareRelease()
        string expectedBranchName,
        string resultingReleaseVersion,
        string resultingMainVersion)
    {
#pragma warning restore SA1114 // Parameter list should follow declaration
        // create and configure repository
        this.InitializeSourceControl();

        // create version.json
        var initialVersionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse(initialVersion),
            Release = new ReleaseOptions()
            {
                VersionIncrement = releaseOptionsVersionIncrement,
                BranchName = releaseOptionsBranchName,
                FirstUnstableTag = releaseOptionsFirstUnstableTag,
            },
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
            },
        };

        var expectedVersionOptionsCurrentBrach = new VersionOptions()
        {
            Version = SemanticVersion.Parse(resultingMainVersion),
            Release = new ReleaseOptions()
            {
                VersionIncrement = releaseOptionsVersionIncrement,
                BranchName = releaseOptionsBranchName,
                FirstUnstableTag = releaseOptionsFirstUnstableTag,
            },
        };

        string initialBranchName = this.LibGit2Repository.Head.FriendlyName;
        Commit tipBeforePrepareRelease = this.LibGit2Repository.Head.Tip;

        // prepare release
        var releaseManager = new ReleaseManager();
        releaseManager.PrepareRelease(this.RepoPath, releaseUnstableTag, nextVersion is null ? null : Version.Parse(nextVersion), parameterVersionIncrement);

        // check if a branch was created
        Assert.Contains(this.LibGit2Repository.Branches, branch => branch.FriendlyName == expectedBranchName);

        // PrepareRelease should switch back to the initial branch
        Assert.Equal(initialBranchName, this.LibGit2Repository.Head.FriendlyName);

        // check if release branch contains a new commit
        // parent of new commit must be the commit before preparing the release
        Branch releaseBranch = this.LibGit2Repository.Branches[expectedBranchName];
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
            Commit incrementCommit = this.LibGit2Repository.Head.Tip;
            Assert.Single(incrementCommit.Parents);
            Assert.Equal(tipBeforePrepareRelease.Id, incrementCommit.Parents.Single().Id);
        }
        else
        {
            // check if current branch contains new commits
            // - one commit that updates the version (parent must be the commit before preparing the release)
            // - one commit merging the release branch back to master and resolving the conflict
            Commit mergeCommit = this.LibGit2Repository.Head.Tip;
            Assert.Equal(2, mergeCommit.Parents.Count());
            Assert.Equal(releaseBranch.Tip.Id, mergeCommit.Parents.Skip(1).First().Id);

            Commit updateVersionCommit = mergeCommit.Parents.First();
            Assert.Single(updateVersionCommit.Parents);
            Assert.Equal(tipBeforePrepareRelease.Id, updateVersionCommit.Parents.First().Id);
        }

        // check version on release branch
        {
            VersionOptions releaseBranchVersion = this.GetVersionOptions(committish: releaseBranch.Tip.Sha);
            Assert.Equal(expectedVersionOptionsReleaseBranch, releaseBranchVersion);
        }

        // check version on master branch
        {
            VersionOptions currentBranchVersion = this.GetVersionOptions(committish: this.LibGit2Repository.Head.Tip.Sha);
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

        // create version.json
        var versionOptions = new VersionOptions() { Version = SemanticVersion.Parse(initialVersion) };
        this.WriteVersionFile(versionOptions);

        // running PrepareRelease should result in an error
        // because we're setting the version on master to a lower version
        this.AssertError(
            () => new ReleaseManager().PrepareRelease(this.RepoPath, releaseUnstableTag, nextVersion is null ? null : Version.Parse(nextVersion)),
            ReleasePreparationError.VersionDecrement);
    }

    [Theory]
    [InlineData("1.2", "1.2")]
    public void PrepareRelease_MasterWithoutVersionIncrement(string initialVersion, string nextVersion)
    {
        // create and configure repository
        this.InitializeSourceControl();

        // create version.json
        var versionOptions = new VersionOptions() { Version = SemanticVersion.Parse(initialVersion) };
        this.WriteVersionFile(versionOptions);

        // running PrepareRelease should result in an error
        // because we're trying to set master to the version it already has
        this.AssertError(
            () => new ReleaseManager().PrepareRelease(this.RepoPath, null, nextVersion is null ? null : Version.Parse(nextVersion)),
            ReleasePreparationError.NoVersionIncrement);
    }

    [Fact]
    public void PrepareRelease_DetachedHead()
    {
        this.InitializeSourceControl();
        this.WriteVersionFile("1.0", "-alpha");
        Commands.Checkout(this.LibGit2Repository, this.LibGit2Repository.Head.Commits.First());
        ReleasePreparationException ex = Assert.Throws<ReleasePreparationException>(() => new ReleaseManager().PrepareRelease(this.RepoPath));
        Assert.Equal(ReleasePreparationError.DetachedHead, ex.Error);
    }

    [Fact]
    public void PrepareRelease_InvalidVersionIncrement()
    {
        // create and configure repository
        this.InitializeSourceControl();

        // create version.json
        var versionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse("1.2"),
            Release = new ReleaseOptions() { VersionIncrement = ReleaseVersionIncrement.Build },
        };
        this.WriteVersionFile(versionOptions);

        // running PrepareRelease should result in an error
        // because a 2-segment version is incompatibale with a increment setting of "build"
        this.AssertError(() => new ReleaseManager().PrepareRelease(this.RepoPath), ReleasePreparationError.InvalidVersionIncrementSetting);
    }

    [Fact]
    public void PrepareRelease_TextOutput()
    {
        // create and configure repository
        this.InitializeSourceControl();

        // create version.json
        var versionOptions = new VersionOptions() { Version = SemanticVersion.Parse("1.0") };
        this.WriteVersionFile(versionOptions);

        var stdout = new StringWriter();
        var releaseManager = new ReleaseManager(stdout);
        releaseManager.PrepareRelease(this.RepoPath);

        // by default, text output mode should be used => trying to parse it as JSON should fail
        Assert.ThrowsAny<JsonException>(() => JsonConvert.DeserializeObject(stdout.ToString()));
    }

    [Fact]
    public void PrepareRelease_JsonOutput()
    {
        // create and configure repository
        this.InitializeSourceControl();

        // create version.json
        var versionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse("1.0"),
            Release = new ReleaseOptions()
            {
                BranchName = "v{version}",
                VersionIncrement = ReleaseVersionIncrement.Minor,
            },
        };
        this.WriteVersionFile(versionOptions);

        string currentBranchName = this.LibGit2Repository.Head.FriendlyName;
        string releaseBranchName = "v1.0";

        // run release preparation
        var stdout = new StringWriter();
        var releaseManager = new ReleaseManager(stdout);
        releaseManager.PrepareRelease(this.RepoPath, outputMode: ReleaseManager.ReleaseManagerOutputMode.Json);

        // Expected output:
        // {
        //     "CurrentBranch" : {
        //         "Name" : "<NAME-OF-CURRENT-BRANCH>",
        //         "Commit" : "<HEAD-COMMIT-OF-CURRENT-BRANCH>",
        //         "Version" : "<UPDATED-VERSION-ON-CURRENT-BRANCH>",
        //     },
        //     "NewBranch" : {
        //         "Name" : "<NAME-OF-CREATED-BRANCH>",
        //         "Commit" : "<HEAD-COMMIT-OF-CREATED-BRANCH>",
        //         "Version" : "<VERSION-ON-CREATED-BRANCH>",
        //     }
        // }
        var jsonOutput = JObject.Parse(stdout.ToString());

        // check "CurrentBranch" output
        {
            string expectedCommitId = this.LibGit2Repository.Branches[currentBranchName].Tip.Sha;
            string expectedVersion = this.GetVersionOptions(committish: this.LibGit2Repository.Branches[currentBranchName].Tip.Sha).Version.ToString();

            var currentBranchOutput = jsonOutput.Property("CurrentBranch")?.Value as JObject;
            Assert.NotNull(currentBranchOutput);

            Assert.Equal(currentBranchName, currentBranchOutput.GetValue("Name")?.ToString());
            Assert.Equal(expectedCommitId, currentBranchOutput.GetValue("Commit")?.ToString());
            Assert.Equal(expectedVersion, currentBranchOutput.GetValue("Version")?.ToString());
        }

        // Check "NewBranch" output
        {
            string expectedCommitId = this.LibGit2Repository.Branches[releaseBranchName].Tip.Sha;
            string expectedVersion = this.GetVersionOptions(committish: this.LibGit2Repository.Branches[releaseBranchName].Tip.Sha).Version.ToString();

            var newBranchOutput = jsonOutput.Property("NewBranch")?.Value as JObject;
            Assert.NotNull(newBranchOutput);

            Assert.Equal(releaseBranchName, newBranchOutput.GetValue("Name")?.ToString());
            Assert.Equal(expectedCommitId, newBranchOutput.GetValue("Commit")?.ToString());
            Assert.Equal(expectedVersion, newBranchOutput.GetValue("Version")?.ToString());
        }
    }

    [Fact]
    public void PrepareRelease_JsonOutputWhenUpdatingReleaseBranch()
    {
        // create and configure repository
        this.InitializeSourceControl();

        // create version.json
        var versionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse("1.0"),
            Release = new ReleaseOptions()
            {
                BranchName = "v{version}",
                VersionIncrement = ReleaseVersionIncrement.Minor,
            },
        };
        this.WriteVersionFile(versionOptions);
        string branchName = "v1.0";

        // switch to release branch
        Commands.Checkout(this.LibGit2Repository, this.LibGit2Repository.CreateBranch(branchName));

        // run release preparation
        var stdout = new StringWriter();
        var releaseManager = new ReleaseManager(stdout);
        releaseManager.PrepareRelease(this.RepoPath, outputMode: ReleaseManager.ReleaseManagerOutputMode.Json);

        // Expected output:
        // {
        //     "CurrentBranch" : {
        //         "Name" : "<NAME>",
        //         "Commit" : "<COMMIT>",
        //         "Version" : "<VERSION>",
        //     },
        //     "NewBranch" : null
        // }
        var jsonOutput = JObject.Parse(stdout.ToString());

        // check "CurrentBranch"  output
        {
            string expectedCommitId = this.LibGit2Repository.Branches[branchName].Tip.Sha;
            string expectedVersion = this.GetVersionOptions(committish: this.LibGit2Repository.Branches[branchName].Tip.Sha).Version.ToString();

            var currentBranchOutput = jsonOutput.Property("CurrentBranch")?.Value as JObject;
            Assert.NotNull(currentBranchOutput);

            Assert.Equal(branchName, currentBranchOutput.GetValue("Name")?.ToString());
            Assert.Equal(expectedCommitId, currentBranchOutput.GetValue("Commit")?.ToString());
            Assert.Equal(expectedVersion, currentBranchOutput.GetValue("Version")?.ToString());
        }

        // Check "NewBranch" output
        {
            // no new branch was created, so "NewBranch" should be null
            var newBranchOutput = jsonOutput.Property("NewBranch")?.Value as JObject;
            Assert.Null(newBranchOutput);
        }
    }

    [Fact]
    public void PrepareRelease_ResetsVersionHeightOffset()
    {
        // create and configure repository
        this.InitializeSourceControl();

        var initialVersionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse("1.0-beta"),
            VersionHeightOffset = 5,
        };

        var expectedReleaseVersionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse("1.0"),
            VersionHeightOffset = 5,
        };

        var expectedMainVersionOptions = new VersionOptions()
        {
            Version = SemanticVersion.Parse("1.1-alpha"),
        };

        // create version.json
        this.WriteVersionFile(initialVersionOptions);

        Commit tipBeforePrepareRelease = this.LibGit2Repository.Head.Tip;

        var releaseManager = new ReleaseManager();
        releaseManager.PrepareRelease(this.RepoPath);

        this.SetContextToHead();
        VersionOptions newVersion = this.Context.VersionFile.GetVersion();
        Assert.Equal(expectedMainVersionOptions, newVersion);

        VersionOptions releaseVersion = this.GetVersionOptions(committish: this.LibGit2Repository.Branches["v1.0"].Tip.Sha);
        Assert.Equal(expectedReleaseVersionOptions, releaseVersion);
    }

    /// <inheritdoc/>
    protected override void InitializeSourceControl(bool withInitialCommit = true)
    {
        base.InitializeSourceControl(withInitialCommit);
        this.Ignore_git2_UntrackedFile();
    }

    private void AssertError(Action testCode, ReleasePreparationError expectedError)
    {
        ReleasePreparationException ex = Assert.Throws<ReleasePreparationException>(testCode);
        Assert.Equal(expectedError, ex.Error);
    }
}
