// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Xunit;
using Xunit.Abstractions;

public class VersionSchemaTests
{
    private readonly ITestOutputHelper logger;

    private readonly JSchema schema;

    private JObject json;

    public VersionSchemaTests(ITestOutputHelper logger)
    {
        this.logger = logger;
        using (var schemaStream = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream($"{ThisAssembly.RootNamespace}.version.schema.json")))
        {
            this.schema = JSchema.Load(new JsonTextReader(schemaStream));
        }
    }

    [Fact]
    public void VersionField_BasicScenarios()
    {
        this.json = JObject.Parse(@"{ ""version"": ""2.3"" }");
        Assert.True(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""2.3-beta"" }");
        Assert.True(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""2.3-beta-final"" }");
        Assert.True(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""2.3-beta.2"" }");
        Assert.True(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""2.3-beta.0"" }");
        Assert.True(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""2.3-beta.01"" }");
        Assert.True(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""1.2.3"" }");
        Assert.True(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""1.2.3.4"" }");
        Assert.True(this.json.IsValid(this.schema));

        this.json = JObject.Parse(@"{ ""version"": ""02.3"" }");
        Assert.False(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""2.03"" }");
        Assert.False(this.json.IsValid(this.schema));
    }

    [Fact]
    public void VersionField_HeightMacroPlacement()
    {
        // Valid uses
        this.json = JObject.Parse(@"{ ""version"": ""2.3.0-{height}"" }");
        Assert.True(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""2.3.0-{height}.beta"" }");
        Assert.True(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""2.3.0-beta.{height}"" }");
        Assert.True(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""2.3.0-beta+{height}"" }");
        Assert.True(this.json.IsValid(this.schema));

        // Invalid uses
        this.json = JObject.Parse(@"{ ""version"": ""2.3.{height}-beta"" }");
        Assert.False(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""2.3.0-beta-{height}"" }");
        Assert.False(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""version"": ""2.3.0-beta+height-{height}"" }");
        Assert.False(this.json.IsValid(this.schema));
    }

    [Fact]
    public void Inherit_AllowsOmissionOfVersion()
    {
        this.json = JObject.Parse(@"{ ""inherit"": false, ""version"": ""1.2"" }");
        Assert.True(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ ""inherit"": false }");
        Assert.False(this.json.IsValid(this.schema));
        this.json = JObject.Parse(@"{ }");
        Assert.False(this.json.IsValid(this.schema));

        this.json = JObject.Parse(@"{ ""inherit"": true }");
        Assert.True(this.json.IsValid(this.schema));
    }

    [Theory]
    [InlineData(@"{ ""version"": ""2.3"", ""release"":  { } }")]
    [InlineData(@"{ ""version"": ""2.3"", ""release"":  { ""branchName"" : ""{version}"" } }")]
    [InlineData(@"{ ""version"": ""2.3"", ""release"":  { ""branchName"" : ""release/v{version}"" } }")]
    [InlineData(@"{ ""version"": ""2.3"", ""release"":  { ""branchName"" : ""prefix{version}suffix"" } }")]
    [InlineData(@"{ ""version"": ""2.3"", ""release"":  { ""branchName"" : ""{version}"", ""versionIncrement"" : ""major"" } }")]
    [InlineData(@"{ ""version"": ""2.3"", ""release"":  { ""branchName"" : ""{version}"", ""versionIncrement"" : ""minor"" } }")]
    [InlineData(@"{ ""version"": ""2.3"", ""release"":  { ""branchName"" : ""{version}"", ""versionIncrement"" : ""build"" } }")]
    [InlineData(@"{ ""version"": ""2.3"", ""release"":  { ""firstUnstableTag"" : ""pre"" } }")]
    public void ReleaseProperty_ValidJson(string json)
    {
        this.json = JObject.Parse(json);
        Assert.True(this.json.IsValid(this.schema));
    }

    [Theory]
    [InlineData(@"{ ""version"": ""2.3"", ""release"":  { ""versionIncrement"" : ""revision"" } }")]
    [InlineData(@"{ ""version"": ""2.3"", ""release"":  { ""branchName"" : ""formatWithoutPlaceholder"" } }")]
    [InlineData(@"{ ""version"": ""2.3"", ""release"":  { ""branchName"" : ""formatWithoutPlaceholder{0}"" } }")]
    [InlineData(@"{ ""version"": ""2.3"", ""release"":  { ""unknownProperty"" : ""value"" } }")]
    public void ReleaseProperty_InvalidJson(string json)
    {
        this.json = JObject.Parse(json);
        Assert.False(this.json.IsValid(this.schema));
    }
}
