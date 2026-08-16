// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        Assert.Contains("internal const string GitCommitId = \"Unavailable\";", generatedCode);
        Assert.Contains("internal static readonly global::System.DateTime GitCommitDate = new global::System.DateTime(626347596600000000L, global::System.DateTimeKind.Utc);", generatedCode);
        Assert.Contains("internal static readonly global::System.DateTime GitCommitAuthorDate = new global::System.DateTime(626347596600000000L, global::System.DateTimeKind.Utc);", generatedCode);
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

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, GitContext.Engine.Disabled);

    protected override void ApplyGlobalProperties(IDictionary<string, string> globalProperties)
        => globalProperties["NBGV_GitEngine"] = EngineString;
}
