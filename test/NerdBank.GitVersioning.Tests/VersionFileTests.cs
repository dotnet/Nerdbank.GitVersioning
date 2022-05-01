using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using Version = System.Version;

[Trait("Engine", "Managed")]
public class VersionFileManagedTests : VersionFileTests
{
    public VersionFileManagedTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, writable: false);
}

[Trait("Engine", "LibGit2")]
public class VersionFileLibGit2Tests : VersionFileTests
{
    public VersionFileLibGit2Tests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, writable: true);
}

public abstract class VersionFileTests : RepoTestBase
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
    public void IsVersionDefined_Commit()
    {
        this.InitializeSourceControl();
        this.AddCommits();
        Assert.False(this.Context.VersionFile.IsVersionDefined());

        this.WriteVersionFile();

        // Verify that we can find the version.txt file in the most recent commit,
        // But not in the initial commit.
        using var tipContext = this.CreateGitContext(this.RepoPath, this.LibGit2Repository.Head.Commits.First().Sha);
        using var initialContext = this.CreateGitContext(this.RepoPath, this.LibGit2Repository.Head.Commits.Last().Sha);
        Assert.True(tipContext.VersionFile.IsVersionDefined());
        Assert.False(initialContext.VersionFile.IsVersionDefined());
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
        this.Context.VersionFile.SetVersion(this.RepoPath, new Version(1, 0));
        string subDirA = Path.Combine(this.RepoPath, "a");
        string subDirAB = Path.Combine(subDirA, "b");
        string subDirABC = Path.Combine(subDirAB, "c");
        Directory.CreateDirectory(subDirABC);
        this.Context.VersionFile.SetVersion(subDirAB, new Version(1, 1));

        using var subDirABCContext = this.CreateGitContext(subDirABC);
        using var subDirABContext = this.CreateGitContext(subDirAB);
        using var subDirAContext = this.CreateGitContext(subDirA);
        Assert.True(subDirABCContext.VersionFile.IsVersionDefined());
        Assert.True(subDirABContext.VersionFile.IsVersionDefined());
        Assert.True(subDirAContext.VersionFile.IsVersionDefined());
        Assert.True(this.Context.VersionFile.IsVersionDefined());
    }

    [Theory]
    [InlineData("2.3", null, null, 0, null, @"{""version"":""2.3""}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, 0, null, @"{""version"":""2.3"",""assemblyVersion"":""2.2""}")]
    [InlineData("2.3", "1.2.3", VersionOptions.VersionPrecision.Minor, 0, null, @"{""version"":""2.3"",""assemblyVersion"":{""version"":""1.2.3""}}")]
    [InlineData("2.3", "1.2.3.4", VersionOptions.VersionPrecision.Minor, 0, null, @"{""version"":""2.3"",""assemblyVersion"":{""version"":""1.2.3.4""}}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, -1, new[] { "refs/heads/master" }, @"{""version"":""2.3"",""assemblyVersion"":""2.2"",""versionHeightOffset"":-1,""publicReleaseRefSpec"":[""refs/heads/master""]}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, -1, new[] { "refs/heads/master" }, @"{""version"":""2.3"",""assemblyVersion"":""2.2"",""buildNumberOffset"":-1,""publicReleaseRefSpec"":[""refs/heads/master""]}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, 0, null, @"{""version"":""2.3"",""assemblyVersion"":{""version"":""2.2""}}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Revision, 0, null, @"{""version"":""2.3"",""assemblyVersion"":{""version"":""2.2"", ""precision"":""revision""}}")]
    public void GetVersion_JsonCompatibility(string version, string assemblyVersion, object precision, int versionHeightOffset, string[] publicReleaseRefSpec, string json)
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.JsonFileName), json);

        var options = this.Context.VersionFile.GetVersion();
        Assert.NotNull(options);
        Assert.Equal(version, options.Version?.ToString());
        Assert.Equal(assemblyVersion, options.AssemblyVersion?.Version?.ToString());
        Assert.Equal(precision, options.AssemblyVersion?.PrecisionOrDefault);
        Assert.Equal(versionHeightOffset, options.VersionHeightOffsetOrDefault);
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
        string pathWritten = this.Context.VersionFile.SetVersion(this.RepoPath, new Version(expectedVersion), expectedPrerelease);
        Assert.Equal(Path.Combine(this.RepoPath, VersionFile.JsonFileName), pathWritten);

        string actualFileContent = File.ReadAllText(pathWritten);
        this.Logger.WriteLine(actualFileContent);

        VersionOptions actualVersion = this.Context.VersionFile.GetVersion();

        Assert.Equal(new Version(expectedVersion), actualVersion.Version.Version);
        Assert.Equal(expectedPrerelease ?? string.Empty, actualVersion.Version.Prerelease);
    }

    [Theory]
    [InlineData("2.3", null, VersionOptions.VersionPrecision.Minor, 0, false, @"{""version"":""2.3""}")]
    [InlineData("2.3", null, VersionOptions.VersionPrecision.Minor, null, true, @"{""version"":""2.3"",""assemblyVersion"":{""precision"":""minor""},""inherit"":true}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, 0, false, @"{""version"":""2.3"",""assemblyVersion"":""2.2""}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, -1, false, @"{""version"":""2.3"",""assemblyVersion"":""2.2"",""versionHeightOffset"":-1}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Revision, -1, false, @"{""version"":""2.3"",""assemblyVersion"":{""version"":""2.2"",""precision"":""revision""},""versionHeightOffset"":-1}")]
    public void SetVersion_WritesSimplestFile(string version, string assemblyVersion, VersionOptions.VersionPrecision? precision, int? versionHeightOffset, bool inherit, string expectedJson)
    {
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse(version),
            AssemblyVersion = assemblyVersion is not null || precision is not null ? new VersionOptions.AssemblyVersionOptions(assemblyVersion is not null ? new Version(assemblyVersion) : null, precision) : null,
            VersionHeightOffset = versionHeightOffset,
            Inherit = inherit,
        };
        string pathWritten = this.Context.VersionFile.SetVersion(this.RepoPath, versionOptions, includeSchemaProperty: false);
        string actualFileContent = File.ReadAllText(pathWritten);
        this.Logger.WriteLine(actualFileContent);

        string normalizedFileContent = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(actualFileContent));
        Assert.Equal(expectedJson, normalizedFileContent);
    }

    [Fact]
    public void SetVersion_PathFilters_OutsideGitRepo()
    {
        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.2"),
            PathFilters = new[]
            {
                new FilterPath("./foo", ""),
            }
        };

        this.Context.VersionFile.SetVersion(this.RepoPath, versionOptions);
    }

    [Fact]
    public void SetVersion_PathFilters_DifferentRelativePaths()
    {
        this.InitializeSourceControl();

        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.2"),
            PathFilters = new[]
            {
                new FilterPath("./foo", "bar"),
                new FilterPath("/absolute", "bar"),
            }
        };
        var expected = versionOptions.PathFilters.Select(x => x.RepoRelativePath).ToList();

        var projectDirectory = Path.Combine(this.RepoPath, "quux");
        this.Context.VersionFile.SetVersion(projectDirectory, versionOptions);

        using var projectContext = this.CreateGitContext(projectDirectory);
        var actualVersionOptions = projectContext.VersionFile.GetVersion();
        var actual = actualVersionOptions.PathFilters.Select(x => x.RepoRelativePath).ToList();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SetVersion_PathFilters_InheritRelativePaths()
    {
        this.InitializeSourceControl();

        var rootVersionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.2"),
            PathFilters = new[]
            {
                new FilterPath("./root-file.txt", ""),
                new FilterPath("/absolute", ""),
            }
        };
        this.Context.VersionFile.SetVersion(this.RepoPath, rootVersionOptions);

        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.2"),
            Inherit = true
        };
        var projectDirectory = Path.Combine(this.RepoPath, "quux");
        this.Context.VersionFile.SetVersion(projectDirectory, versionOptions);

        var expected = rootVersionOptions.PathFilters.Select(x => x.RepoRelativePath).ToList();

        using var projectContext = this.CreateGitContext(projectDirectory);
        var actualVersionOptions = projectContext.VersionFile.GetVersion();
        var actual = actualVersionOptions.PathFilters.Select(x => x.RepoRelativePath).ToList();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SetVersion_PathFilters_InheritOverride()
    {
        this.InitializeSourceControl();

        var rootVersionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.2"),
            PathFilters = new[]
            {
                new FilterPath("./root-file.txt", ""),
                new FilterPath("/absolute", ""),
            }
        };
        this.Context.VersionFile.SetVersion(this.RepoPath, rootVersionOptions);

        var versionOptions = new VersionOptions
        {
            Version = SemanticVersion.Parse("1.2"),
            Inherit = true,
            PathFilters = new[]
            {
                new FilterPath("./project-file.txt", "quux"),
                new FilterPath("/absolute", "quux"),
            }
        };
        var projectDirectory = Path.Combine(this.RepoPath, "quux");
        this.Context.VersionFile.SetVersion(projectDirectory, versionOptions);

        var expected = versionOptions.PathFilters.Select(x => x.RepoRelativePath).ToList();

        using var projectContext = this.CreateGitContext(projectDirectory);
        var actualVersionOptions = projectContext.VersionFile.GetVersion();
        var actual = actualVersionOptions.PathFilters.Select(x => x.RepoRelativePath).ToList();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(@"{""cloudBuild"":{""buildNumber"":{""enabled"":false,""includeCommitId"":{""when"":""nonPublicReleaseOnly"",""where"":""buildMetadata""}}}}", @"{}")]
    [InlineData(@"{""cloudBuild"":{""buildNumber"":{""enabled"":true,""includeCommitId"":{""when"":""nonPublicReleaseOnly"",""where"":""buildMetadata""}}}}", @"{""cloudBuild"":{""buildNumber"":{""enabled"":true}}}")]
    [InlineData(@"{""cloudBuild"":{""buildNumber"":{""enabled"":true,""includeCommitId"":{""when"":""always"",""where"":""buildMetadata""}}}}", @"{""cloudBuild"":{""buildNumber"":{""enabled"":true,""includeCommitId"":{""when"":""always""}}}}")]
    [InlineData(@"{""cloudBuild"":{""buildNumber"":{""enabled"":true,""includeCommitId"":{""when"":""nonPublicReleaseOnly"",""where"":""fourthVersionComponent""}}}}", @"{""cloudBuild"":{""buildNumber"":{""enabled"":true,""includeCommitId"":{""where"":""fourthVersionComponent""}}}}")]
    [InlineData(@"{""cloudBuild"":{""setVersionVariables"":true}}", @"{}")]
    [InlineData(@"{""cloudBuild"":{""setAllVariables"":false}}", @"{}")]
    [InlineData(@"{""release"":{""increment"":""minor""}}", @"{}")]
    [InlineData(@"{""release"":{""branchName"":""v{version}""}}", @"{}")]
    [InlineData(@"{""release"":{""firstUnstableTag"":""alpha""}}", @"{}")]
    [InlineData(@"{""release"":{""gitCommitIdPrefix"":""g""}}", @"{}")]
    [InlineData(@"{""release"":{""firstUnstableTag"":""tag""}}", @"{""release"":{""firstUnstableTag"":""tag""}}")]
    [InlineData(@"{""release"":{""branchName"":""v{version}"",""versionIncrement"":""minor"",""firstUnstableTag"":""alpha""}}", @"{}")]
    [InlineData(@"{""release"":{""versionIncrement"":""major""}}", @"{""release"":{""versionIncrement"":""major""}}")]
    [InlineData(@"{""release"":{""branchName"":""someName""}}", @"{""release"":{""branchName"":""someName""}}")]
    [InlineData(@"{""release"":{""branchName"":""someName"",""versionIncrement"":""major""}}", @"{""release"":{""branchName"":""someName"",""versionIncrement"":""major""}}")]
    [InlineData(@"{""release"":{""branchName"":""someName"",""versionIncrement"":""major"",""firstUnstableTag"":""alpha""}}", @"{""release"":{""branchName"":""someName"",""versionIncrement"":""major""}}")]
    [InlineData(@"{""release"":{""branchName"":""someName"",""versionIncrement"":""major"",""firstUnstableTag"":""pre""}}", @"{""release"":{""branchName"":""someName"",""versionIncrement"":""major"",""firstUnstableTag"":""pre""}}")]
    public void JsonMinification(string full, string minimal)
    {
        var settings = VersionOptions.GetJsonSettings();
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
        VersionOptions actualVersion = this.Context.VersionFile.GetVersion();
        Assert.NotNull(actualVersion);
        Assert.Equal(new Version(1, 2), actualVersion.Version.Version);
        Assert.Equal("-pre", actualVersion.Version.Prerelease);
    }

    [Fact]
    public void GetVersion_CanReadSpecConformantTxtFile_SingleLine()
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.TxtFileName), "1.2-pre");
        VersionOptions actualVersion = this.Context.VersionFile.GetVersion();
        Assert.NotNull(actualVersion);
        Assert.Equal(new Version(1, 2), actualVersion.Version.Version);
        Assert.Equal("-pre", actualVersion.Version.Prerelease);
    }

    [Fact]
    public void GetVersion_CanReadSpecConformantTxtFile_MultiLine()
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.TxtFileName), "1.2\n-pre");
        VersionOptions actualVersion = this.Context.VersionFile.GetVersion();
        Assert.NotNull(actualVersion);
        Assert.Equal(new Version(1, 2), actualVersion.Version.Version);
        Assert.Equal("-pre", actualVersion.Version.Prerelease);
    }

    [Fact]
    public void GetVersion_CanReadSpecConformantTxtFile_MultiLineNoHyphen()
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.TxtFileName), "1.2\npre");
        VersionOptions actualVersion = this.Context.VersionFile.GetVersion();
        Assert.NotNull(actualVersion);
        Assert.Equal(new Version(1, 2), actualVersion.Version.Version);
        Assert.Equal("-pre", actualVersion.Version.Prerelease);
    }

    [Fact]
    public void GetVersion_Commit()
    {
        Assert.Null(this.Context.VersionFile.GetVersion());
        Assert.False(this.Context.VersionFile.IsVersionDefined());

        this.InitializeSourceControl();
        this.WriteVersionFile();
        VersionOptions fromCommit = this.Context.VersionFile.GetVersion();
        VersionOptions fromFile = this.Context.VersionFile.GetVersion();
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

        this.Context.VersionFile.SetVersion(this.RepoPath, rootVersionSpec);
        string subDirA = Path.Combine(this.RepoPath, "a");
        string subDirAB = Path.Combine(subDirA, "b");
        string subDirABC = Path.Combine(subDirAB, "c");
        Directory.CreateDirectory(subDirABC);
        this.Context.VersionFile.SetVersion(subDirAB, new Version(1, 1));
        this.InitializeSourceControl();
        var commit = this.LibGit2Repository.Head.Commits.First().Sha;

        this.AssertPathHasVersion(commit, subDirABC, subdirVersionSpec);
        this.AssertPathHasVersion(commit, subDirAB, subdirVersionSpec);
        this.AssertPathHasVersion(commit, subDirA, rootVersionSpec);
        this.AssertPathHasVersion(commit, this.RepoPath, rootVersionSpec);
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

        this.Context.VersionFile.SetVersion(this.RepoPath, rootVersionSpec);
        string subDirA = Path.Combine(this.RepoPath, "a");
        string subDirAB = Path.Combine(subDirA, "b");
        string subDirABC = Path.Combine(subDirAB, "c");
        Directory.CreateDirectory(subDirABC);
        this.Context.VersionFile.SetVersion(subDirAB, subdirVersionSpec);
        this.InitializeSourceControl();
        var commit = this.LibGit2Repository.Head.Commits.First().Sha;

        this.AssertPathHasVersion(commit, subDirABC, subdirVersionSpec);
        this.AssertPathHasVersion(commit, subDirAB, subdirVersionSpec);
        this.AssertPathHasVersion(commit, subDirA, rootVersionSpec);
        this.AssertPathHasVersion(commit, this.RepoPath, rootVersionSpec);
    }


    [Fact]
    public void GetVersion_ReadReleaseSettings_VersionIncrement()
    {
        var json = @"{ ""version"" : ""1.2"", ""release"" : { ""versionIncrement"" : ""major""  } }";
        var path = Path.Combine(this.RepoPath, "version.json");
        File.WriteAllText(path, json);

        var versionOptions = this.Context.VersionFile.GetVersion();

        Assert.NotNull(versionOptions.Release);
        Assert.NotNull(versionOptions.Release.VersionIncrement);
        Assert.Equal(VersionOptions.ReleaseVersionIncrement.Major, versionOptions.Release.VersionIncrement);
    }

    [Fact]
    public void GetVersion_ReadReleaseSettings_FirstUnstableTag()
    {
        var json = @"{ ""version"" : ""1.2"", ""release"" : { ""firstUnstableTag"" : ""preview""  } }";
        var path = Path.Combine(this.RepoPath, "version.json");
        File.WriteAllText(path, json);

        var versionOptions = this.Context.VersionFile.GetVersion();

        Assert.NotNull(versionOptions.Release);
        Assert.NotNull(versionOptions.Release.FirstUnstableTag);
        Assert.Equal("preview", versionOptions.Release.FirstUnstableTag);
    }

    [Fact]
    public void GetVersion_ReadReleaseSettings_BranchName()
    {
        var json = @"{ ""version"" : ""1.2"", ""release"" : { ""branchName"" : ""someValue{version}""  } }";
        var path = Path.Combine(this.RepoPath, "version.json");
        File.WriteAllText(path, json);

        var versionOptions = this.Context.VersionFile.GetVersion();

        Assert.NotNull(versionOptions.Release);
        Assert.NotNull(versionOptions.Release.BranchName);
        Assert.Equal("someValue{version}", versionOptions.Release.BranchName);
    }

    [Fact]
    public void GetVersion_ReadPathFilters()
    {
        this.InitializeSourceControl();

        var json = @"{ ""version"" : ""1.2"", ""pathFilters"" : [ "":/root.txt"", ""./hello"" ] }";
        var path = Path.Combine(this.RepoPath, "version.json");
        File.WriteAllText(path, json);

        var repoRelativeBaseDirectory = ".";
        var versionOptions = this.Context.VersionFile.GetVersion();

        Assert.NotNull(versionOptions.PathFilters);
        Assert.Equal(new[] { "/root.txt", "./hello" }, versionOptions.PathFilters.Select(fp => fp.ToPathSpec(repoRelativeBaseDirectory)));
    }

    [Fact]
    public void GetVersion_WithPathFiltersOutsideOfGitRepo()
    {
        var json = @"{ ""version"" : ""1.2"", ""pathFilters"" : [ ""."" ] }";
        var path = Path.Combine(this.RepoPath, "version.json");
        File.WriteAllText(path, json);

        this.Context.VersionFile.GetVersion();
    }

    [Fact]
    public void GetVersion_String_MissingFile()
    {
        Assert.Null(this.Context.VersionFile.GetVersion());
    }

    [Fact]
    public void VersionJson_InheritButNoParentFileFound()
    {
        this.InitializeSourceControl();
        this.WriteVersionFile(
            new VersionOptions
            {
                Inherit = true,
                Version = SemanticVersion.Parse("14.2"),
            });
        Assert.Throws<InvalidOperationException>(() => this.Context.VersionFile.GetVersion());
    }

    [Fact]
    public void VersionJson_DoNotInheritButNoVersionSpecified()
    {
        this.InitializeSourceControl();
        Assert.Throws<ArgumentException>(() => this.WriteVersionFile(
            new VersionOptions
            {
                Inherit = false,
            }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VersionJson_Inheritance(bool commitInSourceControl)
    {
        if (commitInSourceControl)
        {
            this.InitializeSourceControl();
        }

        VersionOptions level1, level2, level3, level2NoInherit, level2InheritButResetVersion;
        this.WriteVersionFile(
            level1 = new VersionOptions
            {
                Version = SemanticVersion.Parse("14.2"),
                AssemblyVersion = new VersionOptions.AssemblyVersionOptions { Precision = VersionOptions.VersionPrecision.Major },
            });
        this.WriteVersionFile(
            level2 = new VersionOptions
            {
                Inherit = true,
                AssemblyVersion = new VersionOptions.AssemblyVersionOptions { Precision = VersionOptions.VersionPrecision.Minor },
            },
            "foo");
        this.WriteVersionFile(
            level3 = new VersionOptions
            {
                Inherit = true,
                VersionHeightOffset = 1,
            },
            "foo/bar");
        this.WriteVersionFile(
            level2NoInherit = new VersionOptions
            {
                Version = SemanticVersion.Parse("10.1"),
            },
            "noInherit");
        this.WriteVersionFile(
            level2InheritButResetVersion = new VersionOptions
            {
                Inherit = true,
                Version = SemanticVersion.Parse("8.2"),
            },
            "inheritWithVersion");

        VersionOptions GetOption(string path)
        {
            using var context = this.CreateGitContext(Path.Combine(this.RepoPath, path));
            return context.VersionFile.GetVersion();
        }
        
        var level1Options = GetOption(string.Empty);
        Assert.False(level1Options.Inherit);

        var level2Options = GetOption("foo");
        Assert.Equal(level1.Version.Version.Major, level2Options.Version.Version.Major);
        Assert.Equal(level1.Version.Version.Minor, level2Options.Version.Version.Minor);
        Assert.Equal(level2.AssemblyVersion.Precision, level2Options.AssemblyVersion.Precision);
        Assert.True(level2Options.Inherit);

        var level3Options = GetOption("foo/bar");
        Assert.Equal(level1.Version.Version.Major, level3Options.Version.Version.Major);
        Assert.Equal(level1.Version.Version.Minor, level3Options.Version.Version.Minor);
        Assert.Equal(level2.AssemblyVersion.Precision, level3Options.AssemblyVersion.Precision);
        Assert.Equal(level2.AssemblyVersion.Precision, level3Options.AssemblyVersion.Precision);
        Assert.Equal(level3.VersionHeightOffset, level3Options.VersionHeightOffset);
        Assert.True(level3Options.Inherit);

        var level2NoInheritOptions = GetOption("noInherit");
        Assert.Equal(level2NoInherit.Version, level2NoInheritOptions.Version);
        Assert.Equal(VersionOptions.DefaultVersionPrecision, level2NoInheritOptions.AssemblyVersionOrDefault.PrecisionOrDefault);
        Assert.False(level2NoInheritOptions.Inherit);

        var level2InheritButResetVersionOptions = GetOption("inheritWithVersion");
        Assert.Equal(level2InheritButResetVersion.Version, level2InheritButResetVersionOptions.Version);
        Assert.True(level2InheritButResetVersionOptions.Inherit);

        if (commitInSourceControl)
        {
            int totalCommits = this.LibGit2Repository.Head.Commits.Count();

            // The version height should be the same for all those that inherit the version from the base,
            // even though the inheriting files were introduced in successive commits.
            Assert.Equal(totalCommits, this.GetVersionHeight());
            Assert.Equal(totalCommits, this.GetVersionHeight("foo"));
            Assert.Equal(totalCommits, this.GetVersionHeight("foo/bar"));

            // These either don't inherit, or inherit but reset versions, so the commits were reset.
            Assert.Equal(2, this.GetVersionHeight("noInherit"));
            Assert.Equal(1, this.GetVersionHeight("inheritWithVersion"));
        }
    }

    [Fact]
    public void GetVersion_ProducesAbsolutePath()
    {
        this.InitializeSourceControl();
        this.WriteVersionFile();
        Assert.NotNull(this.Context.VersionFile.GetVersion(out string actualDirectory));
        Assert.True(Path.IsPathRooted(actualDirectory));
    }


    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void GetVersion_ReadNuGetPackageVersionSettings_SemVer(int semVer)
    {
        var json = $@"{{ ""version"" : ""1.0"", ""nugetPackageVersion"" : {{ ""semVer"" : {semVer}  }} }}";
        var path = Path.Combine(this.RepoPath, "version.json");
        File.WriteAllText(path, json);

        var versionOptions = this.Context.VersionFile.GetVersion();

        Assert.NotNull(versionOptions.NuGetPackageVersion);
        Assert.NotNull(versionOptions.NuGetPackageVersion.SemVer);
        Assert.Equal(semVer, versionOptions.NuGetPackageVersion.SemVer);
    }

    [Theory]
    [CombinatorialData]
    public void GetVersion_ReadNuGetPackageVersionSettings_Precision(VersionOptions.VersionPrecision precision)
    {
        var json = $@"{{ ""version"" : ""1.0"", ""nugetPackageVersion"" : {{ ""precision"" : ""{precision}""  }} }}";
        var path = Path.Combine(this.RepoPath, "version.json");
        File.WriteAllText(path, json);

        var versionOptions = this.Context.VersionFile.GetVersion();

        Assert.NotNull(versionOptions.NuGetPackageVersion);
        Assert.NotNull(versionOptions.NuGetPackageVersion.Precision);
        Assert.Equal(precision, versionOptions.NuGetPackageVersion.Precision);
    }

    private void AssertPathHasVersion(string committish, string absolutePath, VersionOptions expected)
    {
        var actual = this.GetVersionOptions(absolutePath, committish);
        Assert.Equal(expected, this.GetVersionOptions(absolutePath, committish));
    }
}
