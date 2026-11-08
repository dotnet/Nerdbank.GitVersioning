// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.Build.Framework;
using Nerdbank.GitVersioning;
using Xunit;
using Version = System.Version;

/// <summary>
/// Build integration tests that use Xunit.Combinatorial's <see cref="PairwiseDataAttribute"/>.
/// </summary>
/// <remarks>
/// These tests stay on xunit while the rest of the tests use TUnit, because TUnit has no pairwise (all-pairs) data source.
/// The test infrastructure (<see cref="BuildIntegrationTests"/>, <see cref="RepoTestBase"/>, etc.)
/// is linked in from the Nerdbank.GitVersioning.Tests project.
/// </remarks>
public abstract class CombinatorialBuildIntegrationTests : BuildIntegrationTests
{
    protected CombinatorialBuildIntegrationTests(Xunit.ITestOutputHelper logger)
        : base(new XunitTestOutputHelper(logger))
    {
    }

    [Theory]
    [PairwiseData]
    public async Task BuildNumber_VariousOptions(bool isPublic, VersionOptions.CloudBuildNumberCommitWhere where, VersionOptions.CloudBuildNumberCommitWhen when, [CombinatorialValues(0, 1, 2)] int extraBuildMetadataCount, [CombinatorialValues(1, 2)] int semVer)
    {
        VersionOptions versionOptions = BuildNumberVersionOptionsBasis;
        versionOptions.CloudBuild.BuildNumber.IncludeCommitId.Where = where;
        versionOptions.CloudBuild.BuildNumber.IncludeCommitId.When = when;
        versionOptions.NuGetPackageVersion = new VersionOptions.NuGetPackageVersionOptions
        {
            SemVer = semVer,
        };
        this.WriteVersionFile(versionOptions);
        this.InitializeSourceControl();

        this.globalProperties["PublicRelease"] = isPublic.ToString();
        for (int i = 0; i < extraBuildMetadataCount; i++)
        {
            this.testProject.AddItem("BuildMetadata", $"A{i}");
        }

        BuildResults buildResult = await this.BuildAsync();
        this.AssertStandardProperties(versionOptions, buildResult);
    }

    // This test builds projects using 'classic' MSBuild projects, which target net45.
    // This is not supported on Linux.
    [WindowsTheory]
    [PairwiseData]
    public async Task AssemblyInfo(bool isVB, bool includeNonVersionAttributes, bool gitRepo, bool isPrerelease, bool isPublicRelease)
    {
        this.WriteVersionFile(prerelease: isPrerelease ? "-beta" : string.Empty);
        if (gitRepo)
        {
            this.InitializeSourceControl();
        }

        if (isVB)
        {
            this.MakeItAVBProject();
        }

        if (includeNonVersionAttributes)
        {
            this.testProject.AddProperty("NBGV_EmitNonVersionCustomAttributes", "true");
        }

        this.globalProperties["PublicRelease"] = isPublicRelease ? "true" : "false";

        BuildResults result = await this.BuildAsync("Build", logVerbosity: LoggerVerbosity.Minimal);
        string assemblyPath = result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("TargetPath");
        string versionFileContent = File.ReadAllText(Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile")));
        this.Logger.WriteLine(versionFileContent);

        var assembly = Assembly.LoadFile(assemblyPath);

        AssemblyFileVersionAttribute assemblyFileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
        AssemblyInformationalVersionAttribute assemblyInformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        AssemblyTitleAttribute assemblyTitle = assembly.GetCustomAttribute<AssemblyTitleAttribute>();
        AssemblyProductAttribute assemblyProduct = assembly.GetCustomAttribute<AssemblyProductAttribute>();
        AssemblyCompanyAttribute assemblyCompany = assembly.GetCustomAttribute<AssemblyCompanyAttribute>();
        AssemblyCopyrightAttribute assemblyCopyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
        Type thisAssemblyClass = assembly.GetType("ThisAssembly") ?? assembly.GetType("TestNamespace.ThisAssembly");
        Assert.NotNull(thisAssemblyClass);

        Assert.Equal(new Version(result.AssemblyVersion), assembly.GetName().Version);
        Assert.Equal(result.AssemblyFileVersion, assemblyFileVersion.Version);
        Assert.Equal(result.AssemblyInformationalVersion, assemblyInformationalVersion.InformationalVersion);
        if (includeNonVersionAttributes)
        {
            Assert.Equal(result.AssemblyTitle, assemblyTitle.Title);
            Assert.Equal(result.AssemblyProduct, assemblyProduct.Product);
            Assert.Equal(result.AssemblyCompany, assemblyCompany.Company);
            Assert.Equal(result.AssemblyCopyright, assemblyCopyright.Copyright);
        }
        else
        {
            Assert.Null(assemblyTitle);
            Assert.Null(assemblyProduct);
            Assert.Null(assemblyCompany);
            Assert.Null(assemblyCopyright);
        }

        const BindingFlags fieldFlags = BindingFlags.Static | BindingFlags.NonPublic;
        Assert.Equal(result.AssemblyVersion, thisAssemblyClass.GetField("AssemblyVersion", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyFileVersion, thisAssemblyClass.GetField("AssemblyFileVersion", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyInformationalVersion, thisAssemblyClass.GetField("AssemblyInformationalVersion", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyName, thisAssemblyClass.GetField("AssemblyName", fieldFlags).GetValue(null));
        Assert.Equal(result.RootNamespace, thisAssemblyClass.GetField("RootNamespace", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyConfiguration, thisAssemblyClass.GetField("AssemblyConfiguration", fieldFlags).GetValue(null));
        Assert.Equal(result.AssemblyTitle, thisAssemblyClass.GetField("AssemblyTitle", fieldFlags)?.GetValue(null));
        Assert.Equal(result.AssemblyProduct, thisAssemblyClass.GetField("AssemblyProduct", fieldFlags)?.GetValue(null));
        Assert.Equal(result.AssemblyCompany, thisAssemblyClass.GetField("AssemblyCompany", fieldFlags)?.GetValue(null));
        Assert.Equal(result.AssemblyCopyright, thisAssemblyClass.GetField("AssemblyCopyright", fieldFlags)?.GetValue(null));
        Assert.Equal(result.GitCommitId, thisAssemblyClass.GetField("GitCommitId", fieldFlags)?.GetValue(null) ?? string.Empty);
        Assert.Equal(result.PublicRelease, thisAssemblyClass.GetField("IsPublicRelease", fieldFlags)?.GetValue(null));
        Assert.Equal(!string.IsNullOrEmpty(result.PrereleaseVersion), thisAssemblyClass.GetField("IsPrerelease", fieldFlags)?.GetValue(null));

        if (gitRepo)
        {
            Assert.True(long.TryParse(result.GitCommitDateTicks, out _), $"Invalid value for GitCommitDateTicks: '{result.GitCommitDateTicks}'");
            var gitCommitDate = new DateTime(long.Parse(result.GitCommitDateTicks), DateTimeKind.Utc);
            Assert.Equal(gitCommitDate, thisAssemblyClass.GetProperty("GitCommitDate", fieldFlags)?.GetValue(null) ?? thisAssemblyClass.GetField("GitCommitDate", fieldFlags)?.GetValue(null) ?? string.Empty);
        }
        else
        {
            Assert.Empty(result.GitCommitDateTicks);
            Assert.Null(thisAssemblyClass.GetProperty("GitCommitDate", fieldFlags));
        }

        // Verify that it doesn't have key fields
        Assert.Null(thisAssemblyClass.GetField("PublicKey", fieldFlags));
        Assert.Null(thisAssemblyClass.GetField("PublicKeyToken", fieldFlags));
    }

    /// <summary>
    /// Adapts xunit's <see cref="Xunit.ITestOutputHelper"/> to the <see cref="ITestOutputHelper"/> that the shared test infrastructure uses.
    /// </summary>
    private class XunitTestOutputHelper : ITestOutputHelper
    {
        private readonly Xunit.ITestOutputHelper inner;

        internal XunitTestOutputHelper(Xunit.ITestOutputHelper inner) => this.inner = inner;

        public void WriteLine(string message) => this.inner.WriteLine(message);

        public void WriteLine(string format, params object[] args) => this.inner.WriteLine(format, args);
    }
}
