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
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, -1, new[] { "refs/heads/master" }, @"{""version"":""2.3"",""assemblyVersion"":""2.2"",""versionHeightOffset"":-1,""publicReleaseRefSpec"":[""refs/heads/master""]}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, -1, new[] { "refs/heads/master" }, @"{""version"":""2.3"",""assemblyVersion"":""2.2"",""buildNumberOffset"":-1,""publicReleaseRefSpec"":[""refs/heads/master""]}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Minor, 0, null, @"{""version"":""2.3"",""assemblyVersion"":{""version"":""2.2""}}")]
    [InlineData("2.3", "2.2", VersionOptions.VersionPrecision.Revision, 0, null, @"{""version"":""2.3"",""assemblyVersion"":{""version"":""2.2"", ""precision"":""revision""}}")]
    public void GetVersion_JsonCompatibility(string version, string assemblyVersion, object precision, int versionHeightOffset, string[] publicReleaseRefSpec, string json)
    {
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.JsonFileName), json);

        var options = VersionFile.GetVersion(this.RepoPath);
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
        string pathWritten = VersionFile.SetVersion(this.RepoPath, new Version(expectedVersion), expectedPrerelease);
        Assert.Equal(Path.Combine(this.RepoPath, VersionFile.JsonFileName), pathWritten);

        string actualFileContent = File.ReadAllText(pathWritten);
        this.Logger.WriteLine(actualFileContent);

        VersionOptions actualVersion = VersionFile.GetVersion(this.RepoPath);

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
            AssemblyVersion = assemblyVersion != null || precision != null ? new VersionOptions.AssemblyVersionOptions(assemblyVersion != null ? new Version(assemblyVersion) : null, precision) : null,
            VersionHeightOffset = versionHeightOffset,
            Inherit = inherit,
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
    [InlineData(@"{""cloudBuild"":{""setAllVariables"":false}}", @"{}")]
    [InlineData(@"{""release"":{""increment"":""minor""}}", @"{}")]
    [InlineData(@"{""release"":{""branchName"":""v{version}""}}", @"{}")]
    [InlineData(@"{""release"":{""firstUnstableTag"":""alpha""}}", @"{}")]
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

        VersionFile.SetVersion(this.RepoPath, rootVersionSpec);
        string subDirA = Path.Combine(this.RepoPath, "a");
        string subDirAB = Path.Combine(subDirA, "b");
        string subDirABC = Path.Combine(subDirAB, "c");
        Directory.CreateDirectory(subDirABC);
        VersionFile.SetVersion(subDirAB, subdirVersionSpec);
        this.InitializeSourceControl();
        var commit = this.Repo.Head.Commits.First();

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

        var versionOptions = VersionFile.GetVersion(this.RepoPath);

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

        var versionOptions = VersionFile.GetVersion(this.RepoPath);

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

        var versionOptions = VersionFile.GetVersion(this.RepoPath);

        Assert.NotNull(versionOptions.Release);
        Assert.NotNull(versionOptions.Release.BranchName);
        Assert.Equal("someValue{version}", versionOptions.Release.BranchName);
    }

    [Fact]
    public void GetVersion_ReadPathFilters()
    {
        var json = @"{ ""version"" : ""1.2"", ""pathFilters"" : [ "":/root.txt"", ""./hello"" ] }";
        var path = Path.Combine(this.RepoPath, "version.json");
        File.WriteAllText(path, json);

        var versionOptions = VersionFile.GetVersion(this.RepoPath);

        Assert.NotNull(versionOptions.PathFilters);
        Assert.Equal(new[] {":/root.txt", "./hello"}, versionOptions.PathFilters);
    }

    [Fact]
    public void GetVersion_String_MissingFile()
    {
        Assert.Null(VersionFile.GetVersion(this.RepoPath));
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
        Assert.Throws<InvalidOperationException>(() => VersionFile.GetVersion(this.Repo));
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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void VersionJson_Inheritance(bool commitInSourceControl, bool bareRepo)
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
            @"foo\bar");
        this.WriteVersionFile(
            level2NoInherit = new VersionOptions
            {
                Version = SemanticVersion.Parse("10.1"),
            },
            @"noInherit");
        this.WriteVersionFile(
            level2InheritButResetVersion = new VersionOptions
            {
                Inherit = true,
                Version = SemanticVersion.Parse("8.2"),
            },
            @"inheritWithVersion");

        Repository operatingRepo = this.Repo;
        if (bareRepo)
        {
            operatingRepo = new Repository(
                Repository.Clone(this.RepoPath, this.CreateDirectoryForNewRepo(), new CloneOptions { IsBare = true }));
        }

        using (operatingRepo)
        {
            VersionOptions GetOption(string path) => commitInSourceControl ? VersionFile.GetVersion(operatingRepo, path) : VersionFile.GetVersion(Path.Combine(this.RepoPath, path));

            var level1Options = GetOption(string.Empty);
            Assert.False(level1Options.Inherit);

            var level2Options = GetOption("foo");
            Assert.Equal(level1.Version.Version.Major, level2Options.Version.Version.Major);
            Assert.Equal(level1.Version.Version.Minor, level2Options.Version.Version.Minor);
            Assert.Equal(level2.AssemblyVersion.Precision, level2Options.AssemblyVersion.Precision);
            Assert.True(level2Options.Inherit);

            var level3Options = GetOption(@"foo\bar");
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
                int totalCommits = operatingRepo.Head.Commits.Count();

                // The version height should be the same for all those that inherit the version from the base,
                // even though the inheriting files were introduced in successive commits.
                Assert.Equal(totalCommits, operatingRepo.GetVersionHeight());
                Assert.Equal(totalCommits, operatingRepo.GetVersionHeight("foo"));
                Assert.Equal(totalCommits, operatingRepo.GetVersionHeight(@"foo\bar"));

                // These either don't inherit, or inherit but reset versions, so the commits were reset.
                Assert.Equal(2, operatingRepo.GetVersionHeight("noInherit"));
                Assert.Equal(1, operatingRepo.GetVersionHeight("inheritWithVersion"));
            }
        }
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
