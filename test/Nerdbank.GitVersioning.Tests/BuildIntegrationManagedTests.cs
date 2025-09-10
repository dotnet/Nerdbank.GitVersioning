// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Nodes;
using Microsoft.Build.Construction;
using Microsoft.Build.Framework;
using Nerdbank.GitVersioning;
using Xunit;

[Trait("Engine", EngineString)]
[Collection("Build")] // msbuild sets current directory in the process, so we can't have it be concurrent with other build tests.
public class BuildIntegrationManagedTests : SomeGitBuildIntegrationTests
{
    protected const string EngineString = "Managed";

    public BuildIntegrationManagedTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <summary>
    /// Verifies that MCP server.json files get version stamping when PackageType=McpServer.
    /// </summary>
    [Fact]
    public async Task McpServerJson_VersionStamping()
    {
        // Create a sample server.json file
        string serverJsonContent = @"{
  ""name"": ""test-mcp-server"",
  ""version"": ""0.0.0"",
  ""description"": ""Test MCP server"",
  ""runtime"": ""dotnet""
}";

        string serverJsonPath = Path.Combine(this.projectDirectory, "server.json");
        File.WriteAllText(serverJsonPath, serverJsonContent);

        // Set PackageType to McpServer
        ProjectPropertyGroupElement propertyGroup = this.testProject.CreatePropertyGroupElement();
        this.testProject.AppendChild(propertyGroup);
        propertyGroup.AddProperty("PackageType", "McpServer");

        this.WriteVersionFile();
        BuildResults result = await this.BuildAsync("NBGV_StampMcpServerJson", logVerbosity: LoggerVerbosity.Detailed);

        // Verify the build succeeded
        Assert.Empty(result.LoggedEvents.OfType<BuildErrorEventArgs>());

        // Verify the stamped server.json was created
        string stampedServerJsonPath = Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("IntermediateOutputPath"), "server.json");
        Assert.True(File.Exists(stampedServerJsonPath), $"Expected stamped server.json at: {stampedServerJsonPath}");

        // Verify the version was correctly stamped
        string stampedContent = File.ReadAllText(stampedServerJsonPath);
        var stampedJson = JsonNode.Parse(stampedContent) as JsonObject;
        Assert.NotNull(stampedJson);

        string expectedVersion = result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("Version");
        Assert.Equal(expectedVersion, stampedJson["version"]?.ToString());

        // Verify other properties were preserved
        Assert.Equal("test-mcp-server", stampedJson["name"]?.ToString());
        Assert.Equal("Test MCP server", stampedJson["description"]?.ToString());
        Assert.Equal("dotnet", stampedJson["runtime"]?.ToString());
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, GitContext.Engine.ReadOnly);

    protected override void ApplyGlobalProperties(IDictionary<string, string> globalProperties)
        => globalProperties["NBGV_GitEngine"] = EngineString;
}
