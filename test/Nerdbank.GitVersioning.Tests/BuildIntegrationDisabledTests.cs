// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Construction;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Nerdbank.GitVersioning;
using Xunit;

[Trait("Engine", EngineString)]
[Collection("Build")] // msbuild sets current directory in the process, so we can't have it be concurrent with other build tests.
public class BuildIntegrationDisabledTests : BuildIntegrationTests
{
    private const string EngineString = "Disabled";

    public BuildIntegrationDisabledTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public async Task ThisAssemblyGitPropertiesHavePlaceholders()
    {
        this.WriteVersionFile();

        BuildResults result = await this.BuildAsync(Targets.GenerateAssemblyNBGVVersionInfo);
        string versionSourceFile = result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile");
        string generatedCode = File.ReadAllText(Path.Combine(this.projectDirectory, versionSourceFile));

        Assert.Contains("internal const string GitCommitId = \"Unavailable from a shallow git clone\";", generatedCode);
        Assert.Contains("internal static readonly global::System.DateTime GitCommitDate = new global::System.DateTime(626347344600000000L, global::System.DateTimeKind.Utc);", generatedCode);
        Assert.Contains("internal static readonly global::System.DateTime GitCommitAuthorDate = new global::System.DateTime(626347344600000000L, global::System.DateTimeKind.Utc);", generatedCode);
        BuildWarningEventArgs warning = Assert.Single(result.LoggedEvents.OfType<BuildWarningEventArgs>(), warning => warning.Code == "NBGV1001");
        Assert.Contains("contain placeholder values", warning.Message);
    }

    [Fact]
    public async Task PlaceholderWarningCanBeSuppressed()
    {
        this.WriteVersionFile();
        this.globalProperties["MSBuildWarningsAsMessages"] = "NBGV1001";

        BuildResults result = await this.BuildAsync(Targets.GenerateAssemblyNBGVVersionInfo);

        Assert.DoesNotContain(result.LoggedEvents.OfType<BuildWarningEventArgs>(), warning => warning.Code == "NBGV1001");
    }

    [Fact]
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
