using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Xunit;
using Xunit.Abstractions;

public class VersionSchemaTests
{
    private readonly ITestOutputHelper Logger;

    private readonly JSchema schema;

    private JObject json;

    public VersionSchemaTests(ITestOutputHelper logger)
    {
        this.Logger = logger;
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
}
