// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.LibGit2;
using Nerdbank.GitVersioning.Tool;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class AdapterVersionInfoTests : RepoTestBase
{
    public AdapterVersionInfoTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public void SerializationHasStableVersionedShape()
    {
        this.InitializeSourceControl(withInitialCommit: false);
        LibGit2Sharp.Commit commit = this.WriteVersionFile("1.2", "-beta");

        using GitContext context = this.CreateGitContext(this.RepoPath, "HEAD");
        var oracle = new VersionOracle(context) { PublicRelease = false };
        var contract = new AdapterVersionInfo(oracle);

        string serializedContract = JsonConvert.SerializeObject(contract, Formatting.Indented);
        using var jsonReader = new JsonTextReader(new StringReader(serializedContract)) { DateParseHandling = DateParseHandling.None };
        JObject json = JObject.Load(jsonReader);

        Assert.Equal(
            ["schemaVersion", "semVer2", "version", "versionHeight", "gitCommitId", "gitCommitIdShort", "gitCommitDate", "publicRelease"],
            json.Properties().Select(property => property.Name));
        Assert.Equal(AdapterVersionInfo.CurrentSchemaVersion, json.Value<int>("schemaVersion"));
        Assert.Equal(oracle.SemVer2, json.Value<string>("semVer2"));
        Assert.Equal(oracle.Version.ToString(), json.Value<string>("version"));
        Assert.Equal(oracle.VersionHeight, json.Value<int>("versionHeight"));
        Assert.Equal(commit.Id.Sha, json.Value<string>("gitCommitId"));
        Assert.Equal(commit.Id.Sha.Substring(0, 10), json.Value<string>("gitCommitIdShort"));
        Assert.Equal("2015-08-02T00:00:00Z", json.Value<string>("gitCommitDate"));
        Assert.False(json.Value<bool>("publicRelease"));
        Assert.Equal(JTokenType.Integer, json["schemaVersion"]!.Type);
        Assert.Equal(JTokenType.Integer, json["versionHeight"]!.Type);
        Assert.Equal(JTokenType.Boolean, json["publicRelease"]!.Type);
    }

    [Fact]
    public void LegacyJsonRemainsPascalCasedAndUnversioned()
    {
        this.InitializeSourceControl(withInitialCommit: false);
        this.WriteVersionFile("1.2", "-beta");

        using GitContext context = this.CreateGitContext(this.RepoPath, "HEAD");
        var oracle = new VersionOracle(context);
        var converters = new JsonConverter[] { new Newtonsoft.Json.Converters.VersionConverter() };

        JObject json = JObject.Parse(JsonConvert.SerializeObject(oracle, Formatting.Indented, converters));

        Assert.NotNull(json["SemVer2"]);
        Assert.NotNull(json["Version"]);
        Assert.NotNull(json["VersionHeight"]);
        Assert.NotNull(json["GitCommitId"]);
        Assert.NotNull(json["GitCommitIdShort"]);
        Assert.NotNull(json["GitCommitDate"]);
        Assert.NotNull(json["PublicRelease"]);
        Assert.Null(json["schemaVersion"]);
        Assert.Null(json["semVer2"]);
    }

    [Fact]
    public void DisabledGitEmitsNullGitProperties()
    {
        this.InitializeSourceControl();

        using GitContext context = GitContext.Create(this.RepoPath, engine: GitContext.Engine.Disabled);
        var contract = new AdapterVersionInfo(new VersionOracle(context));
        JObject json = JObject.FromObject(contract);

        Assert.Equal("0.0.0-g", json.Value<string>("semVer2"));
        Assert.Equal("0.0", json.Value<string>("version"));
        Assert.Equal(0, json.Value<int>("versionHeight"));
        Assert.Equal(JTokenType.Null, json["gitCommitId"]!.Type);
        Assert.Equal(JTokenType.Null, json["gitCommitIdShort"]!.Type);
        Assert.Equal(JTokenType.Null, json["gitCommitDate"]!.Type);
        Assert.False(json.Value<bool>("publicRelease"));
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, engine: GitContext.Engine.ReadWrite);
}
