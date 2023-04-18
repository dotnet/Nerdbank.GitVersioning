// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Xml;
using LibGit2Sharp;
using Microsoft.Build.Construction;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Nerdbank.GitVersioning;
using Validation;
using Xunit;
using Xunit.Abstractions;
using Version = System.Version;

/// <summary>
/// The base class for tests that require some actual git implementation behind it.
/// In other words, NOT the disabled engine implementation.
/// </summary>
public abstract class SomeGitBuildIntegrationTests : BuildIntegrationTests
{
    protected SomeGitBuildIntegrationTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public async Task GetBuildVersion_WithThreeVersionIntegers()
    {
        VersionOptions workingCopyVersion = new VersionOptions
        {
            Version = SemanticVersion.Parse("7.8.9-beta.3"),
            SemVer1NumericIdentifierPadding = 1,
        };
        this.WriteVersionFile(workingCopyVersion);
        this.InitializeSourceControl();
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(workingCopyVersion, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_OutsideGit_PointingToGit()
    {
        // Write a version file to the 'virtualized' repo.
        string version = "3.4";
        this.WriteVersionFile(version);

        string repoRelativeProjectPath = this.testProject.FullPath.Substring(this.RepoPath.Length + 1);

        // Update the repo path so we create the 'normal' one elsewhere
        this.RepoPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        this.InitializeSourceControl();

        // Write the same version file to the 'real' repo
        this.WriteVersionFile(version);

        // Point the project to the 'real' repo
        this.testProject.AddProperty("GitRepoRoot", this.RepoPath);
        this.testProject.AddProperty("ProjectPathRelativeToGitRepoRoot", repoRelativeProjectPath);

        BuildResults buildResult = await this.BuildAsync();

        var workingCopyVersion = VersionOptions.FromVersion(new Version(version));

        this.AssertStandardProperties(workingCopyVersion, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_But_Without_Commits()
    {
        Repository.Init(this.RepoPath);
        var repo = new Repository(this.RepoPath); // do not assign Repo property to avoid commits being generated later
        this.WriteVersionFile("3.4");
        Assumes.False(repo.Head.Commits.Any()); // verification that the test is doing what it claims
        BuildResults buildResult = await this.BuildAsync();
        Assert.Equal("3.4.0.0", buildResult.BuildVersion);
        Assert.Equal("3.4.0", buildResult.AssemblyInformationalVersion);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_But_Head_Lacks_VersionFile()
    {
        Repository.Init(this.RepoPath);
        var repo = new Repository(this.RepoPath); // do not assign Repo property to avoid commits being generated later
        repo.Commit("empty", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        this.WriteVersionFile("3.4");
        Assumes.True(repo.Index[VersionFile.JsonFileName] is null);
        BuildResults buildResult = await this.BuildAsync();
        Assert.Equal("3.4.0." + this.GetVersion().Revision, buildResult.BuildVersion);
        Assert.Equal("3.4.0+" + repo.Head.Tip.Id.Sha.Substring(0, VersionOptions.DefaultGitCommitIdShortFixedLength), buildResult.AssemblyInformationalVersion);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_But_WorkingCopy_Has_Changes()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        var workingCopyVersion = VersionOptions.FromVersion(new Version("6.0"));
        this.Context.VersionFile.SetVersion(this.RepoPath, workingCopyVersion);
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(workingCopyVersion, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_No_VersionFile_At_All()
    {
        Repository.Init(this.RepoPath);
        var repo = new Repository(this.RepoPath); // do not assign Repo property to avoid commits being generated later
        repo.Commit("empty", this.Signer, this.Signer, new CommitOptions { AllowEmptyCommit = true });
        BuildResults buildResult = await this.BuildAsync();
        Assert.Equal("0.0.0." + this.GetVersion().Revision, buildResult.BuildVersion);
        Assert.Equal("0.0.0+" + repo.Head.Tip.Id.Sha.Substring(0, VersionOptions.DefaultGitCommitIdShortFixedLength), buildResult.AssemblyInformationalVersion);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_With_Version_File_In_Subdirectory_Works()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "";
        const string subdirectory = "projdir";

        this.WriteVersionFile(majorMinorVersion, prerelease, subdirectory);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(VersionOptions.FromVersion(new Version(majorMinorVersion)), buildResult, subdirectory);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_With_Version_File_In_Root_And_Subdirectory_Works()
    {
        var rootVersionSpec = new VersionOptions
        {
            Version = SemanticVersion.Parse("14.1"),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions(new Version(14, 0)),
        };
        var subdirVersionSpec = new VersionOptions { Version = SemanticVersion.Parse("11.0") };
        const string subdirectory = "projdir";

        this.WriteVersionFile(rootVersionSpec);
        this.WriteVersionFile(subdirVersionSpec, subdirectory);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(subdirVersionSpec, buildResult, subdirectory);
    }

    [Fact]
    public async Task GetBuildVersion_In_Git_With_Version_File_In_Root_And_Project_In_Root_Works()
    {
        var rootVersionSpec = new VersionOptions
        {
            Version = SemanticVersion.Parse("14.1"),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions(new Version(14, 0)),
        };

        this.WriteVersionFile(rootVersionSpec);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        this.testProject = this.CreateProjectRootElement(this.RepoPath, "root.proj");
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(rootVersionSpec, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_StablePreRelease()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(VersionOptions.FromVersion(new Version(majorMinorVersion)), buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_StableRelease()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        this.globalProperties["PublicRelease"] = "true";
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(VersionOptions.FromVersion(new Version(majorMinorVersion)), buildResult);

        Version version = this.GetVersion();
        Assert.Equal($"{version.Major}.{version.Minor}.{buildResult.GitVersionHeight}", buildResult.NuGetPackageVersion);
    }

    [Fact]
    public async Task GetBuildVersion_UnstablePreRelease()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "-beta";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(VersionOptions.FromVersion(new Version(majorMinorVersion), prerelease), buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_UnstableRelease()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "-beta";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        this.globalProperties["PublicRelease"] = "true";
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(VersionOptions.FromVersion(new Version(majorMinorVersion), prerelease), buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_CustomAssemblyVersion()
    {
        this.WriteVersionFile("14.0");
        this.InitializeSourceControl();
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion(new Version(14, 1)),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions(new Version(14, 0)),
        };
        this.WriteVersionFile(versionOptions);
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Theory]
    [InlineData(VersionOptions.VersionPrecision.Major)]
    [InlineData(VersionOptions.VersionPrecision.Build)]
    [InlineData(VersionOptions.VersionPrecision.Revision)]
    public async Task GetBuildVersion_CustomAssemblyVersionWithPrecision(VersionOptions.VersionPrecision precision)
    {
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion("14.1"),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions
            {
                Version = new Version("15.2"),
                Precision = precision,
            },
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Theory]
    [InlineData(VersionOptions.VersionPrecision.Major)]
    [InlineData(VersionOptions.VersionPrecision.Build)]
    [InlineData(VersionOptions.VersionPrecision.Revision)]
    public async Task GetBuildVersion_CustomAssemblyVersionPrecision(VersionOptions.VersionPrecision precision)
    {
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion("14.1"),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions
            {
                Precision = precision,
            },
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_CustomBuildNumberOffset()
    {
        this.WriteVersionFile("14.0");
        this.InitializeSourceControl();
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion(new Version(14, 1)),
            VersionHeightOffset = 5,
        };
        this.WriteVersionFile(versionOptions);
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_OverrideBuildNumberOffset()
    {
        this.WriteVersionFile("14.0");
        this.InitializeSourceControl();
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion(new Version(14, 1)),
        };
        this.WriteVersionFile(versionOptions);
        this.testProject.AddProperty("OverrideBuildNumberOffset", "10");
        BuildResults buildResult = await this.BuildAsync();
        Assert.StartsWith("14.1.11.", buildResult.AssemblyFileVersion);
    }

    [Fact]
    public async Task GetBuildVersion_Minus1BuildOffset_NotYetCommitted()
    {
        this.WriteVersionFile("14.0");
        this.InitializeSourceControl();
        var versionOptions = new VersionOptions
        {
            Version = new SemanticVersion(new Version(14, 1)),
            VersionHeightOffset = -1,
        };
        this.Context.VersionFile.SetVersion(this.RepoPath, versionOptions);
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task GetBuildVersion_BuildNumberSpecifiedInVersionJson(int buildNumber)
    {
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("14.0." + buildNumber),
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();
        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Fact]
    public async Task PublicRelease_RegEx_Unsatisfied()
    {
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.0"),
            PublicReleaseRefSpec = new string[] { "^refs/heads/release$" },
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();

        // Just build "master", which doesn't conform to the regex.
        BuildResults buildResult = await this.BuildAsync();
        Assert.False(buildResult.PublicRelease);
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Theory]
    [MemberData(nameof(CloudBuildOfBranch), "release")]
    public async Task PublicRelease_RegEx_SatisfiedByCI(IReadOnlyDictionary<string, string> serverProperties)
    {
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.0"),
            PublicReleaseRefSpec = new string[]
            {
                "^refs/heads/release$",
                "^refs/tags/release$",
            },
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();

        // Don't actually switch the checked out branch in git. CI environment variables
        // should take precedence over actual git configuration. (Why? because these variables may
        // retain information about which tag was checked out on a detached head).
        using (ApplyEnvironmentVariables(serverProperties))
        {
            BuildResults buildResult = await this.BuildAsync();
            Assert.True(buildResult.PublicRelease);
            this.AssertStandardProperties(versionOptions, buildResult);
        }
    }

    [Theory]
    [Trait("TestCategory", "FailsInCloudTest")]
    [MemberData(nameof(CloudBuildVariablesData))]
    public async Task CloudBuildVariables_SetInCI(IReadOnlyDictionary<string, string> properties, string expectedMessage, bool setAllVariables)
    {
        using (ApplyEnvironmentVariables(properties))
        {
            string keyName = "n1";
            string value = "v1";
            this.testProject.AddItem("CloudBuildVersionVars", keyName, new Dictionary<string, string> { { "Value", value } });

            string alwaysExpectedMessage = UnitTestCloudBuildPrefix + expectedMessage
                .Replace("{NAME}", keyName)
                .Replace("{VALUE}", value);

            var versionOptions = new VersionOptions
            {
                Version = SemanticVersion.Parse("1.0"),
                CloudBuild = new VersionOptions.CloudBuildOptions { SetAllVariables = setAllVariables, SetVersionVariables = true },
            };
            this.WriteVersionFile(versionOptions);
            this.InitializeSourceControl();

            BuildResults buildResult = await this.BuildAsync();
            this.AssertStandardProperties(versionOptions, buildResult);

            // Assert GitBuildVersion was set
            string conditionallyExpectedMessage = UnitTestCloudBuildPrefix + expectedMessage
                .Replace("{NAME}", "GitBuildVersion")
                .Replace("{VALUE}", buildResult.BuildVersion);
            Assert.Contains(alwaysExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
            Assert.Contains(conditionallyExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));

            // Assert GitBuildVersionSimple was set
            conditionallyExpectedMessage = UnitTestCloudBuildPrefix + expectedMessage
                .Replace("{NAME}", "GitBuildVersionSimple")
                .Replace("{VALUE}", buildResult.BuildVersionSimple);
            Assert.Contains(alwaysExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
            Assert.Contains(conditionallyExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));

            // Assert that project properties are also set.
            Assert.Equal(buildResult.BuildVersion, buildResult.GitBuildVersion);
            Assert.Equal(buildResult.BuildVersionSimple, buildResult.GitBuildVersionSimple);
            Assert.Equal(buildResult.AssemblyInformationalVersion, buildResult.GitAssemblyInformationalVersion);

            if (setAllVariables)
            {
                // Assert that some project properties were set as build properties prefaced with "NBGV_".
                Assert.Equal(buildResult.GitCommitIdShort, buildResult.NBGV_GitCommitIdShort);
                Assert.Equal(buildResult.NuGetPackageVersion, buildResult.NBGV_NuGetPackageVersion);
            }
            else
            {
                // Assert that the NBGV_ prefixed properties are *not* set.
                Assert.Equal(string.Empty, buildResult.NBGV_GitCommitIdShort);
                Assert.Equal(string.Empty, buildResult.NBGV_NuGetPackageVersion);
            }

            // Assert that env variables were also set in context of the build.
            Assert.Contains(
                buildResult.LoggedEvents,
                e => string.Equals(e.Message, $"n1=v1", StringComparison.OrdinalIgnoreCase) || string.Equals(e.Message, $"n1='v1'", StringComparison.OrdinalIgnoreCase));

            versionOptions.CloudBuild.SetVersionVariables = false;
            this.WriteVersionFile(versionOptions);
            this.SetContextToHead();
            buildResult = await this.BuildAsync();
            this.AssertStandardProperties(versionOptions, buildResult);

            // Assert GitBuildVersion was not set
            conditionallyExpectedMessage = UnitTestCloudBuildPrefix + expectedMessage
                .Replace("{NAME}", "GitBuildVersion")
                .Replace("{VALUE}", buildResult.BuildVersion);
            Assert.Contains(alwaysExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
            Assert.DoesNotContain(conditionallyExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
            Assert.NotEqual(buildResult.BuildVersion, buildResult.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitBuildVersion"));

            // Assert GitBuildVersionSimple was not set
            conditionallyExpectedMessage = UnitTestCloudBuildPrefix + expectedMessage
                .Replace("{NAME}", "GitBuildVersionSimple")
                .Replace("{VALUE}", buildResult.BuildVersionSimple);
            Assert.Contains(alwaysExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
            Assert.DoesNotContain(conditionallyExpectedMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
            Assert.NotEqual(buildResult.BuildVersionSimple, buildResult.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitBuildVersionSimple"));
        }
    }

    [Theory]
    [MemberData(nameof(BuildNumberData))]
    public async Task BuildNumber_SetInCI(VersionOptions versionOptions, IReadOnlyDictionary<string, string> properties, string expectedBuildNumberMessage)
    {
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();
        using (ApplyEnvironmentVariables(properties))
        {
            BuildResults buildResult = await this.BuildAsync();
            this.AssertStandardProperties(versionOptions, buildResult);
            expectedBuildNumberMessage = expectedBuildNumberMessage.Replace("{CLOUDBUILDNUMBER}", buildResult.CloudBuildNumber);
            Assert.Contains(UnitTestCloudBuildPrefix + expectedBuildNumberMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
        }

        versionOptions.CloudBuild.BuildNumber.Enabled = false;
        this.WriteVersionFile(versionOptions);
        using (ApplyEnvironmentVariables(properties))
        {
            BuildResults buildResult = await this.BuildAsync();
            this.AssertStandardProperties(versionOptions, buildResult);
            expectedBuildNumberMessage = expectedBuildNumberMessage.Replace("{CLOUDBUILDNUMBER}", buildResult.CloudBuildNumber);
            Assert.DoesNotContain(UnitTestCloudBuildPrefix + expectedBuildNumberMessage, buildResult.LoggedEvents.Select(e => e.Message.TrimEnd()));
        }
    }

    [Theory]
    [PairwiseData]
    public async Task BuildNumber_VariousOptions(bool isPublic, VersionOptions.CloudBuildNumberCommitWhere where, VersionOptions.CloudBuildNumberCommitWhen when, [CombinatorialValues(0, 1, 2)] int extraBuildMetadataCount, [CombinatorialValues(1, 2)] int semVer)
    {
        VersionOptions versionOptions = BuildNumberVersionOptionsBasis;
        versionOptions.CloudBuild.BuildNumber.IncludeCommitId.Where = where;
        versionOptions.CloudBuild.BuildNumber.IncludeCommitId.When = when;
        versionOptions.NuGetPackageVersion = new VersionOptions.NuGetPackageVersionOptions
        {
            SemVer = semVer,
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();

        this.globalProperties["PublicRelease"] = isPublic.ToString();
        for (int i = 0; i < extraBuildMetadataCount; i++)
        {
            this.testProject.AddItem("BuildMetadata", $"A{i}");
        }

        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    [Fact]
    public void GitLab_BuildTag()
    {
        // Based on the values defined in https://docs.gitlab.com/ee/ci/variables/#syntax-of-environment-variables-in-job-scripts
        using (ApplyEnvironmentVariables(
            CloudBuild.SuppressEnvironment.SetItems(
                new Dictionary<string, string>()
                {
                    { "CI_COMMIT_TAG", "1.0.0" },
                    { "CI_COMMIT_SHA", "1ecfd275763eff1d6b4844ea3168962458c9f27a" },
                    { "GITLAB_CI", "true" },
                    { "SYSTEM_TEAMPROJECTID", string.Empty },
                })))
        {
            ICloudBuild activeCloudBuild = Nerdbank.GitVersioning.CloudBuild.Active;
            Assert.NotNull(activeCloudBuild);
            Assert.Null(activeCloudBuild.BuildingBranch);
            Assert.Equal("refs/tags/1.0.0", activeCloudBuild.BuildingTag);
            Assert.Equal("1ecfd275763eff1d6b4844ea3168962458c9f27a", activeCloudBuild.GitCommitId);
            Assert.True(activeCloudBuild.IsApplicable);
            Assert.False(activeCloudBuild.IsPullRequest);
        }
    }

    [Fact]
    public void GitLab_BuildBranch()
    {
        // Based on the values defined in https://docs.gitlab.com/ee/ci/variables/#syntax-of-environment-variables-in-job-scripts
        using (ApplyEnvironmentVariables(
            CloudBuild.SuppressEnvironment.SetItems(
                new Dictionary<string, string>()
                {
                    { "CI_COMMIT_REF_NAME", "master" },
                    { "CI_COMMIT_SHA", "1ecfd275763eff1d6b4844ea3168962458c9f27a" },
                    { "GITLAB_CI", "true" },
                })))
        {
            ICloudBuild activeCloudBuild = Nerdbank.GitVersioning.CloudBuild.Active;
            Assert.NotNull(activeCloudBuild);
            Assert.Equal("refs/heads/master", activeCloudBuild.BuildingBranch);
            Assert.Null(activeCloudBuild.BuildingTag);
            Assert.Equal("1ecfd275763eff1d6b4844ea3168962458c9f27a", activeCloudBuild.GitCommitId);
            Assert.True(activeCloudBuild.IsApplicable);
            Assert.False(activeCloudBuild.IsPullRequest);
        }
    }

    [Fact]
    public async Task PublicRelease_RegEx_SatisfiedByCheckedOutBranch()
    {
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.0"),
            PublicReleaseRefSpec = new string[] { "^refs/heads/release$" },
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();

        using (ApplyEnvironmentVariables(CloudBuild.SuppressEnvironment))
        {
            // Check out a branch that conforms.
            Branch releaseBranch = this.LibGit2Repository.CreateBranch("release");
            Commands.Checkout(this.LibGit2Repository, releaseBranch);
            BuildResults buildResult = await this.BuildAsync();
            Assert.True(buildResult.PublicRelease);
            this.AssertStandardProperties(versionOptions, buildResult);
        }
    }

    // This test builds projects using 'classic' MSBuild projects, which target net45.
    // This is not supported on Linux.
    [WindowsTheory]
    [PairwiseData]
    public async Task AssemblyInfo(bool isVB, bool includeNonVersionAttributes, bool gitRepo, bool isPrerelease, bool isPublicRelease)
    {
        this.WriteVersionFile(prerelease: isPrerelease ? "-beta" : string.Empty);
        if (gitRepo)
        {
            this.InitializeSourceControl();
        }

        if (isVB)
        {
            this.MakeItAVBProject();
        }

        if (includeNonVersionAttributes)
        {
            this.testProject.AddProperty("NBGV_EmitNonVersionCustomAttributes", "true");
        }

        this.globalProperties["PublicRelease"] = isPublicRelease ? "true" : "false";

        BuildResults result = await this.BuildAsync("Build", logVerbosity: LoggerVerbosity.Minimal);
        string assemblyPath = result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("TargetPath");
        string versionFileContent = File.ReadAllText(Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile")));
        this.Logger.WriteLine(versionFileContent);

        var assembly = Assembly.LoadFile(assemblyPath);

        AssemblyFileVersionAttribute assemblyFileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
        AssemblyInformationalVersionAttribute assemblyInformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        AssemblyTitleAttribute assemblyTitle = assembly.GetCustomAttribute<AssemblyTitleAttribute>();
        AssemblyProductAttribute assemblyProduct = assembly.GetCustomAttribute<AssemblyProductAttribute>();
        AssemblyCompanyAttribute assemblyCompany = assembly.GetCustomAttribute<AssemblyCompanyAttribute>();
        AssemblyCopyrightAttribute assemblyCopyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
        Type thisAssemblyClass = assembly.GetType("ThisAssembly") ?? assembly.GetType("TestNamespace.ThisAssembly");
        Assert.NotNull(thisAssemblyClass);

        Assert.Equal(new Version(result.AssemblyVersion), assembly.GetName().Version);
        Assert.Equal(result.AssemblyFileVersion, assemblyFileVersion.Version);
        Assert.Equal(result.AssemblyInformationalVersion, assemblyInformationalVersion.InformationalVersion);
        if (includeNonVersionAttributes)
        {
            Assert.Equal(result.AssemblyTitle, assemblyTitle.Title);
            Assert.Equal(result.AssemblyProduct, assemblyProduct.Product);
            Assert.Equal(result.AssemblyCompany, assemblyCompany.Company);
            Assert.Equal(result.AssemblyCopyright, assemblyCopyright.Copyright);
        }
        else
        {
            Assert.Null(assemblyTitle);
            Assert.Null(assemblyProduct);
            Assert.Null(assemblyCompany);
            Assert.Null(assemblyCopyright);
        }

        const BindingFlags fieldFlags = BindingFlags.Static | BindingFlags.NonPublic;
        Assert.Equal(result.AssemblyVersion, thisAssemblyClass.GetField("AssemblyVersion", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyFileVersion, thisAssemblyClass.GetField("AssemblyFileVersion", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyInformationalVersion, thisAssemblyClass.GetField("AssemblyInformationalVersion", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyName, thisAssemblyClass.GetField("AssemblyName", fieldFlags).GetValue(null));
        Assert.Equal(result.RootNamespace, thisAssemblyClass.GetField("RootNamespace", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyConfiguration, thisAssemblyClass.GetField("AssemblyConfiguration", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyTitle, thisAssemblyClass.GetField("AssemblyTitle", fieldFlags)?.GetValue(null));
        Assert.Equal(result.AssemblyProduct, thisAssemblyClass.GetField("AssemblyProduct", fieldFlags)?.GetValue(null));
        Assert.Equal(result.AssemblyCompany, thisAssemblyClass.GetField("AssemblyCompany", fieldFlags)?.GetValue(null));
        Assert.Equal(result.AssemblyCopyright, thisAssemblyClass.GetField("AssemblyCopyright", fieldFlags)?.GetValue(null));
        Assert.Equal(result.GitCommitId, thisAssemblyClass.GetField("GitCommitId", fieldFlags)?.GetValue(null) ?? string.Empty);
        Assert.Equal(result.PublicRelease, thisAssemblyClass.GetField("IsPublicRelease", fieldFlags)?.GetValue(null));
        Assert.Equal(!string.IsNullOrEmpty(result.PrereleaseVersion), thisAssemblyClass.GetField("IsPrerelease", fieldFlags)?.GetValue(null));

        if (gitRepo)
        {
            Assert.True(long.TryParse(result.GitCommitDateTicks, out _), $"Invalid value for GitCommitDateTicks: '{result.GitCommitDateTicks}'");
            var gitCommitDate = new DateTime(long.Parse(result.GitCommitDateTicks), DateTimeKind.Utc);
            Assert.Equal(gitCommitDate, thisAssemblyClass.GetProperty("GitCommitDate", fieldFlags)?.GetValue(null) ?? thisAssemblyClass.GetField("GitCommitDate", fieldFlags)?.GetValue(null) ?? string.Empty);
        }
        else
        {
            Assert.Empty(result.GitCommitDateTicks);
            Assert.Null(thisAssemblyClass.GetProperty("GitCommitDate", fieldFlags));
        }

        // Verify that it doesn't have key fields
        Assert.Null(thisAssemblyClass.GetField("PublicKey", fieldFlags));
        Assert.Null(thisAssemblyClass.GetField("PublicKeyToken", fieldFlags));
    }

    [Fact]
    [Trait("TestCategory", "FailsInCloudTest")]
    public async Task AssemblyInfo_IncrementalBuild()
    {
        this.WriteVersionFile(prerelease: "-beta");
        await this.BuildAsync("Build", logVerbosity: LoggerVerbosity.Minimal);
        this.WriteVersionFile(prerelease: "-rc"); // two characters SHORTER, to test file truncation.
        await this.BuildAsync("Build", logVerbosity: LoggerVerbosity.Minimal);
    }

#if !NETCOREAPP
    /// <summary>
    /// Create a native resource .dll and verify that its version
    ///  information is set correctly.
    /// </summary>
    [Fact]
    public async Task NativeVersionInfo_CreateNativeResourceDll()
    {
        this.testProject = this.CreateNativeProjectRootElement(this.projectDirectory, "test.vcxproj");
        this.WriteVersionFile();
        BuildResults result = await this.BuildAsync(Targets.Build, logVerbosity: LoggerVerbosity.Minimal);
        Assert.Empty(result.LoggedEvents.OfType<BuildErrorEventArgs>());

        string targetFile = Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("TargetPath"));
        Assert.True(File.Exists(targetFile));

        var fileInfo = FileVersionInfo.GetVersionInfo(targetFile);
        Assert.Equal("1.2", fileInfo.FileVersion);
        Assert.Equal("1.2.0", fileInfo.ProductVersion);
        Assert.Equal("test", fileInfo.InternalName);
        Assert.Equal("Nerdbank", fileInfo.CompanyName);
        Assert.Equal($"Copyright (c) {DateTime.Now.Year}. All rights reserved.", fileInfo.LegalCopyright);
    }
#endif

    /// <inheritdoc/>
    protected override GitContext CreateGitContext(string path, string committish = null) => throw new NotImplementedException();
}
