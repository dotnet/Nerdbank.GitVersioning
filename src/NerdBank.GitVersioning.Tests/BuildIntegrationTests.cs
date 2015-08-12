using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using LibGit2Sharp;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.Tests;
using Validation;
using Xunit;
using Xunit.Abstractions;
using Version = System.Version;

public class BuildIntegrationTests : RepoTestBase
{
    private BuildManager buildManager;
    private ProjectCollection projectCollection;
    private string projectDirectory;
    private ProjectRootElement testProject;
    private Dictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private Random random;

    public BuildIntegrationTests(ITestOutputHelper logger)
        : base(logger)
    {
        int seed = (int)DateTime.Now.Ticks;
        this.random = new Random(seed);
        this.Logger.WriteLine("Random seed: {0}", seed);
        this.buildManager = new BuildManager();
        this.projectCollection = new ProjectCollection();
        this.projectDirectory = Path.Combine(this.RepoPath, "projdir");
        Directory.CreateDirectory(this.projectDirectory);
        this.testProject = this.CreateProjectRootElement();
        this.globalProperties.Add("NerdbankGitVersioningTasksPath", Environment.CurrentDirectory + "\\");
    }

    [Fact]
    public async Task GetBuildVersion_Returns_BuildVersion_Property()
    {
        this.WriteVersionFile();
        this.InitializeSourceControl();
        var buildResult = await this.BuildAsync();
        Assert.Equal(
            buildResult.BuildVersion,
            buildResult.BuildResult.ResultsByTarget[Targets.GetBuildVersion].Items.Single().ItemSpec);
    }

    [Fact]
    public async Task GetBuildVersion_StableVersion()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(prerelease, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_UnstableVersion()
    {
        const string majorMinorVersion = "5.8";
        const string prerelease = "-beta";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        this.AddCommits(this.random.Next(15));
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(prerelease, buildResult);
    }

    private void AssertStandardProperties(string prerelease, BuildResults buildResult)
    {
        int height = this.Repo.Head.GetHeight();
        string commitIdShort = this.Repo.Head.Commits.First().Id.Sha.Substring(0, 10);
        Version version = this.Repo.Head.Commits.First().GetIdAsVersion();
        Assert.Equal($"{version}", buildResult.AssemblyFileVersion);
        Assert.Equal($"{version.Major}.{version.Minor}.{height}{prerelease}+g{commitIdShort}", buildResult.AssemblyInformationalVersion);
        Assert.Equal($"{version.Major}.{version.Minor}", buildResult.AssemblyVersion);
        Assert.Equal(height.ToString(), buildResult.BuildNumber);
        Assert.Equal(height.ToString(), buildResult.BuildNumberFirstAndSecondComponentsIfApplicable);
        Assert.Equal(height.ToString(), buildResult.BuildNumberFirstComponent);
        Assert.Equal(string.Empty, buildResult.BuildNumberSecondComponent);
        Assert.Equal($"{version}", buildResult.BuildVersion);
        Assert.Equal($"{version.Major}.{version.Minor}.{height}", buildResult.BuildVersion3Components);
        Assert.Equal(height.ToString(), buildResult.BuildVersionNumberComponent);
        Assert.Equal($"{version.Major}.{version.Minor}.{height}", buildResult.BuildVersionSimple);
        Assert.Equal(this.Repo.Head.Commits.First().Id.Sha, buildResult.GitCommitId);
        Assert.Equal(commitIdShort, buildResult.GitCommitIdShort);
        Assert.Equal(height.ToString(), buildResult.GitHeight);
        Assert.Equal($"{version.Major}.{version.Minor}", buildResult.MajorMinorVersion);
        if (string.IsNullOrEmpty(prerelease))
        {
            Assert.Equal($"{version.Major}.{version.Minor}.{buildResult.GitHeight}", buildResult.NuGetPackageVersion);
        }
        else
        {
            Assert.Equal($"{version.Major}.{version.Minor}.{buildResult.GitHeight}{prerelease}-g{commitIdShort}", buildResult.NuGetPackageVersion);
        }

        Assert.Equal(prerelease, buildResult.PrereleaseVersion);
        Assert.Equal($"+g{commitIdShort}", buildResult.SemVerBuildSuffix);
    }

    private async Task<BuildResults> BuildAsync(string target = Targets.GetBuildVersion)
    {
        var buildResult = await this.buildManager.BuildAsync(
            this.Logger,
            this.projectCollection,
            this.testProject,
            target,
            this.globalProperties);
        var result = new BuildResults(buildResult);
        this.Logger.WriteLine(result.ToString());
        Assert.Equal(BuildResultCode.Success, buildResult.OverallResult);
        return result;
    }

    private ProjectRootElement CreateProjectRootElement()
    {
        var pre = ProjectRootElement.Create(this.projectCollection);
        pre.FullPath = Path.Combine(this.projectDirectory, "test.proj");

        const string ns = "NerdBank.GitVersioning.Tests";
        const string gitVersioningTargetsFileName = "NerdBank.GitVersioning.targets";
        ProjectRootElement gitVersioningTargets;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{ns}.{gitVersioningTargetsFileName}"))
        {
            gitVersioningTargets = ProjectRootElement.Create(XmlReader.Create(stream), this.projectCollection);
            gitVersioningTargets.FullPath = Path.Combine(this.RepoPath, gitVersioningTargetsFileName);
        }

        pre.AddImport(gitVersioningTargets.FullPath);

        return pre;
    }

    private static class Targets
    {
        internal const string GetBuildVersion = "GetBuildVersion";
        internal const string GetNuGetPackageVersion = "GetNuGetPackageVersion";
        internal const string GenerateAssemblyInfo = "GenerateAssemblyInfo";
    }

    private class BuildResults
    {
        internal BuildResults(BuildResult buildResult)
        {
            Requires.NotNull(buildResult, nameof(buildResult));
            this.BuildResult = buildResult;
        }

        public BuildResult BuildResult { get; private set; }

        public string BuildNumber => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildNumber");
        public string GitCommitId => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitCommitId");
        public string BuildVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildVersion");
        public string BuildVersionSimple => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildVersionSimple");
        public string PrereleaseVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("PrereleaseVersion");
        public string MajorMinorVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("MajorMinorVersion");
        public string BuildVersionNumberComponent => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildVersionNumberComponent");
        public string BuildNumberFirstComponent => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildNumberFirstComponent");
        public string BuildNumberSecondComponent => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildNumberSecondComponent");
        public string BuildNumberFirstAndSecondComponentsIfApplicable => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildNumberFirstAndSecondComponentsIfApplicable");
        public string GitCommitIdShort => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitCommitIdShort");
        public string GitHeight => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitHeight");
        public string SemVerBuildSuffix => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("SemVerBuildSuffix");
        public string BuildVersion3Components => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildVersion3Components");
        public string AssemblyInformationalVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyInformationalVersion");
        public string AssemblyFileVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyFileVersion");
        public string AssemblyVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyVersion");
        public string NuGetPackageVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("NuGetPackageVersion");

        public override string ToString()
        {
            var sb = new StringBuilder();

            foreach (var property in this.GetType().GetRuntimeProperties().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (property.DeclaringType == this.GetType() && property.Name != nameof(BuildResult))
                {
                    sb.AppendLine($"{property.Name} = {property.GetValue(this)}");
                }
            }

            return sb.ToString();
        }
    }
}
