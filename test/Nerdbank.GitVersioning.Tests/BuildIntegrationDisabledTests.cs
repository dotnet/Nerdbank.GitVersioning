// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Construction;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Nerdbank.GitVersioning;
using Xunit;

[Property("Engine", EngineString)]
[InheritsTests]
public class BuildIntegrationDisabledTests : BuildIntegrationTests
{
    private const string EngineString = "Disabled";

    public BuildIntegrationDisabledTests()
        : base(TestOutputHelper.Instance)
    {
    }

    [Test]
    public async Task ThisAssemblyGitPropertiesHavePlaceholders()
    {
        this.WriteVersionFile();

        BuildResults result = await this.BuildAsync(Targets.GenerateAssemblyNBGVVersionInfo);
        string versionSourceFile = result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile");
        string generatedCode = File.ReadAllText(Path.Combine(this.projectDirectory, versionSourceFile));

        Assert.Contains("internal const string GitCommitId = \"Unavailable\";", generatedCode);
        Assert.Contains("internal static readonly global::System.DateTime GitCommitDate = new global::System.DateTime(626347596600000000L, global::System.DateTimeKind.Utc);", generatedCode);
        Assert.Contains("internal static readonly global::System.DateTime GitCommitAuthorDate = new global::System.DateTime(626347596600000000L, global::System.DateTimeKind.Utc);", generatedCode);
        BuildWarningEventArgs warning = Assert.Single(result.LoggedEvents.OfType<BuildWarningEventArgs>(), warning => warning.Code == "NBGV1001");
        Assert.Contains("contain placeholder values", warning.Message);
    }

    [Test]
    public async Task PlaceholderWarningCanBeSuppressed()
    {
        this.WriteVersionFile();
        this.globalProperties["MSBuildWarningsAsMessages"] = "NBGV1001";

        BuildResults result = await this.BuildAsync(Targets.GenerateAssemblyNBGVVersionInfo);

        Assert.DoesNotContain(result.LoggedEvents.OfType<BuildWarningEventArgs>(), warning => warning.Code == "NBGV1001");
    }

    [Test]
    [Arguments("MSBuildTargetCaching", "NoWarn", false)]
    [Arguments("InProject", "NoWarn", false)]
    [Arguments("MSBuildTargetCaching", "MSBuildWarningsAsMessages", false)]
    [Arguments("InProject", "MSBuildWarningsAsMessages", false)]
    [Arguments("MSBuildTargetCaching", "MSBuildWarningsAsErrors", false)]
    [Arguments("InProject", "MSBuildWarningsAsErrors", false)]
    [Arguments("MSBuildTargetCaching", "NoWarn", true)]
    [Arguments("InProject", "NoWarn", true)]
    [Arguments("MSBuildTargetCaching", "MSBuildWarningsAsMessages", true)]
    [Arguments("InProject", "MSBuildWarningsAsMessages", true)]
    [Arguments("MSBuildTargetCaching", "MSBuildWarningsAsErrors", true)]
    [Arguments("InProject", "MSBuildWarningsAsErrors", true)]
    public async Task PlaceholderWarningHonorsProjectSeverity(string cacheMode, string severityProperty, bool designTimeBuild)
    {
        this.WriteVersionFile();
        this.testProject.AddProperty("NBGV_CacheMode", cacheMode);
        this.testProject.AddProperty(severityProperty, "OTHER0001;NBGV1001;OTHER0002");
        this.globalProperties["DesignTimeBuild"] = designTimeBuild.ToString();

        bool expectError = severityProperty == "MSBuildWarningsAsErrors";
        BuildResults result = await this.BuildAsync(Targets.GenerateAssemblyNBGVVersionInfo, assertSuccessfulBuild: !expectError);

        Assert.DoesNotContain(result.LoggedEvents.OfType<BuildWarningEventArgs>(), warning => warning.Code == "NBGV1001");
        if (expectError)
        {
            Assert.Equal(BuildResultCode.Failure, result.BuildResult.OverallResult);
            Assert.Single(result.LoggedEvents.OfType<BuildErrorEventArgs>(), error => error.Code == "NBGV1001");
        }
        else
        {
            Assert.DoesNotContain(result.LoggedEvents.OfType<BuildErrorEventArgs>(), error => error.Code == "NBGV1001");
            Assert.Equal("Unavailable", result.GitCommitId);
            if (severityProperty == "MSBuildWarningsAsMessages")
            {
                Assert.Contains(result.LoggedEvents.OfType<BuildMessageEventArgs>(), message => message.Message.Contains("contain placeholder values"));
            }
            else
            {
                Assert.DoesNotContain(result.LoggedEvents.OfType<BuildMessageEventArgs>(), message => message.Message.Contains("contain placeholder values"));
            }
        }
    }

    [Test]
    [Arguments("MSBuildTargetCaching", "OTHER0001;NBGV10010", false)]
    [Arguments("InProject", "OTHER0001;NBGV10010", false)]
    [Arguments("MSBuildTargetCaching", "OTHER0001;nbgv1001", true)]
    [Arguments("InProject", "OTHER0001;nbgv1001", true)]
    public async Task PlaceholderWarningNoWarnMatchesCode(string cacheMode, string noWarn, bool suppressed)
    {
        this.WriteVersionFile();
        this.testProject.AddProperty("NBGV_CacheMode", cacheMode);
        this.testProject.AddProperty("NoWarn", noWarn);

        BuildResults result = await this.BuildAsync();

        Assert.Equal(suppressed ? 0 : 1, result.LoggedEvents.OfType<BuildWarningEventArgs>().Count(warning => warning.Code == "NBGV1001"));
    }

    [Test]
    public async Task GetPackageVersionWithEmptyTargetFrameworkGlobalProperty()
    {
        this.WriteVersionFile("3.4");
        ProjectRootElement sdkProject = ProjectRootElement.Create(this.projectCollection);
        sdkProject.Sdk = "Microsoft.NET.Sdk";
        sdkProject.FullPath = Path.Combine(this.projectDirectory, "sdk.csproj");
        sdkProject.AddProperty("TargetFramework", "net10.0");
        sdkProject.AddImport(Path.Combine(this.RepoPath, GitVersioningPropsFileName));
        sdkProject.AddImport(Path.Combine(this.RepoPath, GitVersioningTargetsFileName));
        sdkProject.Save();

        var globalProperties = new Dictionary<string, string>(this.globalProperties)
        {
            ["TargetFramework"] = string.Empty,
        };
        this.ApplyGlobalProperties(globalProperties);
        BuildResult result = await this.buildManager.BuildAsync(
            this.Logger,
            this.projectCollection,
            sdkProject,
            "_GetProjectVersion",
            globalProperties,
            additionalLoggers: Array.Empty<ILogger>());

        Assert.Equal(BuildResultCode.Success, result.OverallResult);
        Assert.Equal("3.4.0-g", result.ProjectStateAfterBuild.GetPropertyValue("PackageVersion"));
        Assert.Empty(result.ProjectStateAfterBuild.GetPropertyValue("BuildVersion"));
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, GitContext.Engine.Disabled);

    protected override void ApplyGlobalProperties(IDictionary<string, string> globalProperties)
        => globalProperties["NBGV_GitEngine"] = EngineString;
}
