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
        // Create a sample server.json file based on the real MCP server template
        string serverJsonContent = @"{
  ""$schema"": ""https://modelcontextprotocol.io/schemas/draft/2025-07-09/server.json"",
  ""description"": ""Test .NET MCP Server"",
  ""name"": ""io.github.test/testmcpserver"",
  ""version"": ""__VERSION__"",
  ""packages"": [
    {
      ""registry_type"": ""nuget"",
      ""identifier"": ""Test.McpServer"",
      ""version"": ""__VERSION__"",
      ""transport"": {
        ""type"": ""stdio""
      },
      ""package_arguments"": [],
      ""environment_variables"": []
    }
  ],
  ""repository"": {
    ""url"": ""https://github.com/test/testmcpserver"",
    ""source"": ""github""
  }
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

        // Verify root version was stamped
        Assert.Equal(expectedVersion, stampedJson["version"]?.ToString());

        // Verify package version was also stamped
        JsonArray packages = stampedJson["packages"]?.AsArray();
        Assert.NotNull(packages);
        Assert.Single(packages);

        JsonObject package = packages[0]?.AsObject();
        Assert.NotNull(package);
        Assert.Equal(expectedVersion, package["version"]?.ToString());

        // Verify other properties were preserved
        Assert.Equal("io.github.test/testmcpserver", stampedJson["name"]?.ToString());
        Assert.Equal("Test .NET MCP Server", stampedJson["description"]?.ToString());
        Assert.Equal("Test.McpServer", package["identifier"]?.ToString());

        // Verify that no __VERSION__ placeholders remain in the entire JSON
        Assert.DoesNotContain("__VERSION__", stampedContent);
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, GitContext.Engine.ReadOnly);

    protected override void ApplyGlobalProperties(IDictionary<string, string> globalProperties)
        => globalProperties["NBGV_GitEngine"] = EngineString;
}
