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
using NerdBank.GitVersioning;
using NerdBank.GitVersioning.Tests;
using Validation;
using Xunit;
using Xunit.Abstractions;

public class BuildIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper logger;
    private BuildManager buildManager;
    private ProjectCollection projectCollection;
    private string testDirectoryRoot;
    private string projectDirectory;
    private ProjectRootElement testProject;
    private Repository repo;
    private Signature signer = new Signature("a", "a@a.com", new DateTimeOffset(2015, 8, 2, 0, 0, 0, TimeSpan.Zero));
    private Dictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public BuildIntegrationTests(ITestOutputHelper logger)
    {
        this.logger = logger;

        this.buildManager = new BuildManager();
        this.projectCollection = new ProjectCollection();
        this.testDirectoryRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        this.projectDirectory = Path.Combine(this.testDirectoryRoot, "projdir");
        Directory.CreateDirectory(this.projectDirectory);
        this.testProject = this.CreateProjectRootElement();
        this.globalProperties.Add("NerdbankGitVersioningTasksPath", Environment.CurrentDirectory + "\\");
    }

    public void Dispose()
    {
        this.repo?.Dispose();
        TestUtilities.DeleteDirectory(this.testDirectoryRoot);
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
        const int height = 13;
        const string majorMinorVersion = "5.8";
        const string prerelease = "";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        this.AddCommits(height - 1);
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(height, majorMinorVersion, prerelease, buildResult);
    }

    [Fact]
    public async Task GetBuildVersion_UnstableVersion()
    {
        const int height = 13;
        const string majorMinorVersion = "5.8";
        const string prerelease = "-beta";

        this.WriteVersionFile(majorMinorVersion, prerelease);
        this.InitializeSourceControl();
        this.AddCommits(height - 1);
        var buildResult = await this.BuildAsync();
        this.AssertStandardProperties(height, majorMinorVersion, prerelease, buildResult);
    }

    private void AssertStandardProperties(int height, string majorMinorVersion, string prerelease, BuildResults buildResult)
    {
        string commitIdShort = this.repo.Head.Commits.First().Id.Sha.Substring(0, 10);
        Assert.Equal($"{majorMinorVersion}.{height}{prerelease}+g{commitIdShort}", buildResult.AssemblyInformationalVersion);
        Assert.Equal(height.ToString(), buildResult.BuildNumber);
        Assert.Equal(height.ToString(), buildResult.BuildNumberFirstAndSecondComponentsIfApplicable);
        Assert.Equal(height.ToString(), buildResult.BuildNumberFirstComponent);
        Assert.Equal(string.Empty, buildResult.BuildNumberSecondComponent);
        Assert.Equal($"{majorMinorVersion}.{height}", buildResult.BuildVersion);
        Assert.Equal($"{majorMinorVersion}.{height}", buildResult.BuildVersion3Components);
        Assert.Equal(height.ToString(), buildResult.BuildVersionNumberComponent);
        Assert.Equal($"{majorMinorVersion}.{height}", buildResult.BuildVersionSimple);
        Assert.Equal(this.repo.Head.Commits.First().Id.Sha, buildResult.GitCommitId);
        Assert.Equal(commitIdShort, buildResult.GitCommitIdShort);
        Assert.Equal(height.ToString(), buildResult.GitHeight);
        Assert.Equal(majorMinorVersion, buildResult.MajorMinorVersion);
        Assert.Equal($"{majorMinorVersion}.0{prerelease}-g{commitIdShort}", buildResult.NuGetPackageVersion);
        Assert.Equal(prerelease, buildResult.PrereleaseVersion);
        Assert.Equal($"+g{commitIdShort}", buildResult.SemVerBuildSuffix);
    }

    private async Task<BuildResults> BuildAsync(string target = Targets.GetBuildVersion)
    {
        var buildResult = await this.buildManager.BuildAsync(
            this.logger,
            this.projectCollection,
            this.testProject,
            target,
            this.globalProperties);
        var result = new BuildResults(buildResult);
        this.logger.WriteLine(result.ToString());
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
            gitVersioningTargets.FullPath = Path.Combine(this.testDirectoryRoot, gitVersioningTargetsFileName);
        }

        pre.AddImport(gitVersioningTargets.FullPath);

        return pre;
    }

    private void WriteVersionFile(string version = "1.2", string prerelease = "")
    {
        File.WriteAllLines(
            Path.Combine(this.testDirectoryRoot, VersionTextFile.FileName),
            new[] { version, prerelease });

        if (this.repo != null)
        {
            this.repo.Stage(VersionTextFile.FileName);
            this.repo.Commit($"Add {VersionTextFile.FileName}", this.signer);
        }
    }

    private void InitializeSourceControl()
    {
        Repository.Init(this.testDirectoryRoot);
        this.repo = new Repository(this.testDirectoryRoot);
        foreach (var file in this.repo.RetrieveStatus().Untracked)
        {
            this.repo.Stage(file.FilePath);
        }

        this.repo.Commit("initial commit", this.signer);
    }

    private void AddCommits(int count = 1)
    {
        Verify.Operation(this.repo != null, "Repo has not been created yet.");
        for (int i = 1; i <= count; i++)
        {
            this.repo.Commit($"filler commit {i}", signer, new CommitOptions { AllowEmptyCommit = true });
        }
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
