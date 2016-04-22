using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.Tests;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using Version = System.Version;

public class VersionFileTests : RepoTestBase
{
    private string versionTxtPath;
    private string versionJsonPath;

    public VersionFileTests(ITestOutputHelper logger)
        : base(logger)
    {
        this.versionTxtPath = Path.Combine(this.RepoPath, VersionFile.TxtFileName);
        this.versionJsonPath = Path.Combine(this.RepoPath, VersionFile.JsonFileName);
    }

    [Fact]
    public void IsVersionDefined_Commit_Null()
    {
        Assert.False(VersionFile.IsVersionDefined((Commit)null));
    }

    [Fact]
    public void IsVersionDefined_String_NullOrEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => VersionFile.IsVersionDefined((string)null));
        Assert.Throws<ArgumentException>(() => VersionFile.IsVersionDefined(string.Empty));
    }

    [Fact]
    public void IsVersionDefined_Commit()
    {
        this.InitializeSourceControl();
        this.AddCommits();
        Assert.False(VersionFile.IsVersionDefined(this.Repo.Head.Commits.First()));

        this.WriteVersionFile();

        // Verify that we can find the version.txt file in the most recent commit,
        // But not in the initial commit.
        Assert.True(VersionFile.IsVersionDefined(this.Repo.Head.Commits.First()));
        Assert.False(VersionFile.IsVersionDefined(this.Repo.Head.Commits.Last()));
    }

    [Fact]
    public void IsVersionDefined_String_ConsiderAncestorFolders()
    {
        // Construct a repo where versions are defined like this:
        /*   root <- 1.0
                a             (inherits 1.0)
                    b <- 1.1
                         c    (inherits 1.1)
        */
        VersionFile.SetVersion(this.RepoPath, new Version(1, 0));
        string subDirA = Path.Combine(this.RepoPath, "a");
        string subDirAB = Path.Combine(subDirA, "b");
        string subDirABC = Path.Combine(subDirAB, "c");
        Directory.CreateDirectory(subDirABC);
        VersionFile.SetVersion(subDirAB, new Version(1, 1));

        Assert.True(VersionFile.IsVersionDefined(subDirABC));
        Assert.True(VersionFile.IsVersionDefined(subDirAB));
        Assert.True(VersionFile.IsVersionDefined(subDirA));
        Assert.True(VersionFile.IsVersionDefined(this.RepoPath));
    }

    [Theory]
    [InlineData("2.3", null, null, 0, null, @"{""version"":""2.3""}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, 0, null, @"{""version"":""2.3"",""assemblyVersion"":""2.2""}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, -1, new[] { "refs/heads/master" }, @"{""version"":""2.3"",""assemblyVersion"":""2.2"",""buildNumberOffset"":-1,""publicReleaseRefSpec"":[""refs/heads/master""]}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, 0, null, @"{""version"":""2.3"",""assemblyVersion"":{""version"":""2.2""}}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Revision, 0, null, @"{""version"":""2.3"",""assemblyVersion"":{""version"":""2.2"", ""precision"":""revision""}}")]
    public void GetVersion_JsonCompatibility(string version, string assemblyVersion, object precision, int buildNumberOffset, string[] publicReleaseRefSpec, string json)
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.JsonFileName), json);

        var options = VersionFile.GetVersion(this.RepoPath);
        Assert.NotNull(options);
        Assert.Equal(version, options.Version?.ToString());
        Assert.Equal(assemblyVersion, options.AssemblyVersion?.Version?.ToString());
        Assert.Equal(precision, options.AssemblyVersion?.Precision);
        Assert.Equal(buildNumberOffset, options.BuildNumberOffset);
        Assert.Equal(publicReleaseRefSpec, options.PublicReleaseRefSpec);
    }

    [Theory]
    [InlineData("2.3", "")]
    [InlineData("2.3", null)]
    [InlineData("2.3", "-beta")]
    [InlineData("2.3.0", "")]
    [InlineData("2.3.0", "-rc")]
    public void SetVersion_GetVersionFromFile(string expectedVersion, string expectedPrerelease)
    {
        string pathWritten = VersionFile.SetVersion(this.RepoPath, new Version(expectedVersion), expectedPrerelease);
        Assert.Equal(Path.Combine(this.RepoPath, VersionFile.JsonFileName), pathWritten);

        string actualFileContent = File.ReadAllText(pathWritten);
        this.Logger.WriteLine(actualFileContent);

        VersionOptions actualVersion = VersionFile.GetVersion(this.RepoPath);

        Assert.Equal(new Version(expectedVersion), actualVersion.Version.Version);
        Assert.Equal(expectedPrerelease ?? string.Empty, actualVersion.Version.Prerelease);
    }

    [Theory]
    [InlineData("2.3", null, VersionOptions.VersionPrecision.Minor, 0, @"{""version"":""2.3""}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, 0, @"{""version"":""2.3"",""assemblyVersion"":""2.2""}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, -1, @"{""version"":""2.3"",""assemblyVersion"":""2.2"",""buildNumberOffset"":-1}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Revision, -1, @"{""version"":""2.3"",""assemblyVersion"":{""version"":""2.2"",""precision"":""revision""},""buildNumberOffset"":-1}")]
    public void SetVersion_WritesSimplestFile(string version, string assemblyVersion, VersionOptions.VersionPrecision precision, int buildNumberOffset, string expectedJson)
    {
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse(version),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions(assemblyVersion != null ? new Version(assemblyVersion) : null, precision),
            BuildNumberOffset = buildNumberOffset,
        };
        string pathWritten = VersionFile.SetVersion(this.RepoPath, versionOptions);
        string actualFileContent = File.ReadAllText(pathWritten);
        this.Logger.WriteLine(actualFileContent);

        string normalizedFileContent = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(actualFileContent));
        Assert.Equal(expectedJson, normalizedFileContent);
    }

    [Theory]
    [InlineData(@"{""cloudBuild"":{""buildNumber"":{""enabled"":false,""includeCommitId"":{""when"":""nonPublicReleaseOnly"",""where"":""buildMetadata""}}}}", @"{}")]
    [InlineData(@"{""cloudBuild"":{""buildNumber"":{""enabled"":true,""includeCommitId"":{""when"":""nonPublicReleaseOnly"",""where"":""buildMetadata""}}}}", @"{""cloudBuild"":{""buildNumber"":{""enabled"":true}}}")]
    [InlineData(@"{""cloudBuild"":{""buildNumber"":{""enabled"":true,""includeCommitId"":{""when"":""always"",""where"":""buildMetadata""}}}}", @"{""cloudBuild"":{""buildNumber"":{""enabled"":true,""includeCommitId"":{""when"":""always""}}}}")]
    [InlineData(@"{""cloudBuild"":{""buildNumber"":{""enabled"":true,""includeCommitId"":{""when"":""nonPublicReleaseOnly"",""where"":""fourthVersionComponent""}}}}", @"{""cloudBuild"":{""buildNumber"":{""enabled"":true,""includeCommitId"":{""where"":""fourthVersionComponent""}}}}")]
    [InlineData(@"{""cloudBuild"":{""setVersionVariables"":true}}", @"{}")]
    public void JsonMinification(string full, string minimal)
    {
        var settings = VersionOptions.JsonSettings;
        settings.Formatting = Formatting.None;

        // Assert that the two representations are equivalent.
        var fullVersion = JsonConvert.DeserializeObject<VersionOptions>(full, settings);
        var minimalVersion = JsonConvert.DeserializeObject<VersionOptions>(minimal, settings);
        Assert.Equal(fullVersion, minimalVersion);

        string fullVersionSerialized = JsonConvert.SerializeObject(fullVersion, settings);
        Assert.Equal(minimal, fullVersionSerialized);
    }

    [Fact]
    public void GetVersion_CanReadSpecConformantJsonFile()
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.JsonFileName), "{ version: \"1.2-pre\" }");
        VersionOptions actualVersion = VersionFile.GetVersion(this.RepoPath);
        Assert.NotNull(actualVersion);
        Assert.Equal(new Version(1, 2), actualVersion.Version.Version);
        Assert.Equal("-pre", actualVersion.Version.Prerelease);
    }

    [Fact]
    public void GetVersion_CanReadSpecConformantTxtFile_SingleLine()
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.TxtFileName), "1.2-pre");
        VersionOptions actualVersion = VersionFile.GetVersion(this.RepoPath);
        Assert.NotNull(actualVersion);
        Assert.Equal(new Version(1, 2), actualVersion.Version.Version);
        Assert.Equal("-pre", actualVersion.Version.Prerelease);
    }

    [Fact]
    public void GetVersion_CanReadSpecConformantTxtFile_MultiLine()
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.TxtFileName), "1.2\n-pre");
        VersionOptions actualVersion = VersionFile.GetVersion(this.RepoPath);
        Assert.NotNull(actualVersion);
        Assert.Equal(new Version(1, 2), actualVersion.Version.Version);
        Assert.Equal("-pre", actualVersion.Version.Prerelease);
    }

    [Fact]
    public void GetVersion_CanReadSpecConformantTxtFile_MultiLineNoHyphen()
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.TxtFileName), "1.2\npre");
        VersionOptions actualVersion = VersionFile.GetVersion(this.RepoPath);
        Assert.NotNull(actualVersion);
        Assert.Equal(new Version(1, 2), actualVersion.Version.Version);
        Assert.Equal("-pre", actualVersion.Version.Prerelease);
    }

    [Fact]
    public void GetVersion_Commit()
    {
        Assert.Null(VersionFile.GetVersion((Commit)null));

        this.InitializeSourceControl();
        this.WriteVersionFile();
        VersionOptions fromCommit = VersionFile.GetVersion(this.Repo.Head.Commits.First());
        VersionOptions fromFile = VersionFile.GetVersion(this.RepoPath);
        Assert.NotNull(fromCommit);
        Assert.Equal(fromFile, fromCommit);
    }

    [Fact]
    public void GetVersion_String_FindsNearestFileInAncestorDirectories()
    {
        // Construct a repo where versions are defined like this:
        /*   root <- 1.0
                a             (inherits 1.0)
                    b <- 1.1
                         c    (inherits 1.1)
        */
        var rootVersionSpec = new VersionOptions { Version = SemanticVersion.Parse("1.0") };
        var subdirVersionSpec = new VersionOptions { Version = SemanticVersion.Parse("1.1") };

        VersionFile.SetVersion(this.RepoPath, rootVersionSpec);
        string subDirA = Path.Combine(this.RepoPath, "a");
        string subDirAB = Path.Combine(subDirA, "b");
        string subDirABC = Path.Combine(subDirAB, "c");
        Directory.CreateDirectory(subDirABC);
        VersionFile.SetVersion(subDirAB, new Version(1, 1));
        this.InitializeSourceControl();
        var commit = this.Repo.Head.Commits.First();

        AssertPathHasVersion(commit, subDirABC, subdirVersionSpec);
        AssertPathHasVersion(commit, subDirAB, subdirVersionSpec);
        AssertPathHasVersion(commit, subDirA, rootVersionSpec);
        AssertPathHasVersion(commit, this.RepoPath, rootVersionSpec);
    }

    [Fact]
    public void GetVersion_String_FindsNearestFileInAncestorDirectories_WithAssemblyVersion()
    {
        // Construct a repo where versions are defined like this:
        /*   root <- 14.0
                a             (inherits 14.0)
                    b <- 11.0
                         c    (inherits 11.0)
        */
        var rootVersionSpec = new VersionOptions
        {
            Version = SemanticVersion.Parse("14.1"),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions(new Version(14, 0)),
        };
        var subdirVersionSpec = new VersionOptions { Version = SemanticVersion.Parse("11.0") };

        VersionFile.SetVersion(this.RepoPath, rootVersionSpec);
        string subDirA = Path.Combine(this.RepoPath, "a");
        string subDirAB = Path.Combine(subDirA, "b");
        string subDirABC = Path.Combine(subDirAB, "c");
        Directory.CreateDirectory(subDirABC);
        VersionFile.SetVersion(subDirAB, subdirVersionSpec);
        this.InitializeSourceControl();
        var commit = this.Repo.Head.Commits.First();

        AssertPathHasVersion(commit, subDirABC, subdirVersionSpec);
        AssertPathHasVersion(commit, subDirAB, subdirVersionSpec);
        AssertPathHasVersion(commit, subDirA, rootVersionSpec);
        AssertPathHasVersion(commit, this.RepoPath, rootVersionSpec);
    }

    [Fact]
    public void GetVersion_String_MissingFile()
    {
        Assert.Null(VersionFile.GetVersion(this.RepoPath));
    }

    private void AssertPathHasVersion(Commit commit, string absolutePath, VersionOptions expected)
    {
        var actual = VersionFile.GetVersion(absolutePath);
        Assert.Equal(expected, actual);

        // Pass in the repo-relative path to ensure the commit is used as the data source.
        string relativePath = absolutePath.Substring(this.RepoPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        actual = VersionFile.GetVersion(commit, relativePath);
        Assert.Equal(expected, actual);
    }
}
