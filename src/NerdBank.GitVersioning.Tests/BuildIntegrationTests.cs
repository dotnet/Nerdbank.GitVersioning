using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    private async Task<BuildResults> BuildAsync(string target = Targets.GetBuildVersion)
    {
        var buildResult = await this.buildManager.BuildAsync(
            this.logger,
            this.projectCollection,
            this.testProject,
            target,
            this.globalProperties);
        Assert.Equal(BuildResultCode.Success, buildResult.OverallResult);
        return new BuildResults(buildResult);
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
            this.repo.Commit($"Add {VersionTextFile.FileName}");
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

        this.repo.Commit("initial commit");
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
    }
}
