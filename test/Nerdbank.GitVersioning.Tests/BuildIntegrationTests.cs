// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Text;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nerdbank.GitVersioning;
using Validation;
using Xunit;
using Xunit.Abstractions;

public abstract class BuildIntegrationTests : RepoTestBase, IClassFixture<MSBuildFixture>
{
    protected const string GitVersioningTargetsFileName = "NerdBank.GitVersioning.targets";
    protected const string UnitTestCloudBuildPrefix = "UnitTest: ";

    protected BuildManager buildManager;
    protected ProjectCollection projectCollection;
    protected string projectDirectory;
    protected ProjectRootElement testProject;
    protected Dictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Set global properties to neutralize environment variables
        // that might actually be defined by a CI that is building and running these tests.
        { "PublicRelease", string.Empty },
    };

    protected Random random;

    private static readonly string[] ToxicEnvironmentVariablePrefixes = new string[]
    {
        "APPVEYOR",
        "SYSTEM_",
        "BUILD_",
        "NBGV_GitEngine",
    };

    public BuildIntegrationTests(ITestOutputHelper logger)
        : base(logger)
    {
        // MSBuildExtensions.LoadMSBuild will be called as part of the base constructor, because this class
        // implements the IClassFixture<MSBuildFixture> interface. LoadMSBuild will load the MSBuild assemblies.
        // This must happen _before_ any method that directly references types in the Microsoft.Build namespace has been called.
        // Net, don't init MSBuild-related fields in the constructor, but in a method that is called by the constructor.
        this.Init();
    }

    protected string CommitIdShort => this.Context.GitCommitId?.Substring(0, VersionOptions.DefaultGitCommitIdShortFixedLength);

    [Fact]
    public async Task GetBuildVersion_Without_Git()
    {
        this.WriteVersionFile("3.4");
        BuildResults buildResult = await this.BuildAsync();
        Assert.Equal("3.4", buildResult.BuildVersion);
        Assert.Equal("3.4.0", buildResult.AssemblyInformationalVersion);
    }

    [Fact]
    public async Task GetBuildVersion_Without_Git_HighPrecisionAssemblyVersion()
    {
        this.WriteVersionFile(new VersionOptions
        {
            Version = SemanticVersion.Parse("3.4"),
            AssemblyVersion = new VersionOptions.AssemblyVersionOptions
            {
                Precision = VersionOptions.VersionPrecision.Revision,
            },
        });
        BuildResults buildResult = await this.BuildAsync();
        Assert.Equal("3.4", buildResult.BuildVersion);
        Assert.Equal("3.4.0", buildResult.AssemblyInformationalVersion);
    }

    [Fact]
    public async Task GetBuildVersion_Returns_BuildVersion_Property()
    {
        this.WriteVersionFile();
        this.InitializeSourceControl();
        BuildResults buildResult = await this.BuildAsync();
        Assert.Equal(
            buildResult.BuildVersion,
            buildResult.BuildResult.ResultsByTarget[Targets.GetBuildVersion].Items.Single().ItemSpec);
    }

    /// <summary>
    /// Emulate a project with an unsupported language, and verify that
    /// no errors are emitted because the target is skipped.
    /// </summary>
    [Fact]
    public async Task AssemblyInfo_Suppressed()
    {
        ProjectPropertyGroupElement propertyGroup = this.testProject.CreatePropertyGroupElement();
        this.testProject.AppendChild(propertyGroup);
        propertyGroup.AddProperty("Language", "NoCodeDOMProviderForThisLanguage");
        propertyGroup.AddProperty(Targets.GenerateAssemblyVersionInfo, "false");

        this.WriteVersionFile();
        BuildResults result = await this.BuildAsync(Targets.GenerateAssemblyVersionInfo, logVerbosity: LoggerVerbosity.Minimal);
        string versionCsFilePath = Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile"));
        Assert.False(File.Exists(versionCsFilePath));
        Assert.Empty(result.LoggedEvents.OfType<BuildErrorEventArgs>());
        Assert.Empty(result.LoggedEvents.OfType<BuildWarningEventArgs>());
    }

    /// <summary>
    /// Emulate a project with an unsupported language, and verify that
    /// one warning is emitted because the assembly info file couldn't be generated.
    /// </summary>
    [Fact]
    public async Task AssemblyInfo_NotProducedWithoutCodeDomProvider()
    {
        ProjectPropertyGroupElement propertyGroup = this.testProject.CreatePropertyGroupElement();
        this.testProject.AppendChild(propertyGroup);
        propertyGroup.AddProperty("Language", "NoCodeDOMProviderForThisLanguage");

        this.WriteVersionFile();
        BuildResults result = await this.BuildAsync(Targets.GenerateAssemblyVersionInfo, logVerbosity: LoggerVerbosity.Minimal, assertSuccessfulBuild: false);
        Assert.Equal(BuildResultCode.Failure, result.BuildResult.OverallResult);
        string versionCsFilePath = Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile"));
        Assert.False(File.Exists(versionCsFilePath));
        Assert.Single(result.LoggedEvents.OfType<BuildErrorEventArgs>());
    }

    /// <summary>
    /// Emulate a project with an unsupported language, and verify that
    /// no errors are emitted because the target is skipped.
    /// </summary>
    [Fact]
    public async Task AssemblyInfo_SuppressedImplicitlyByTargetExt()
    {
        ProjectPropertyGroupElement propertyGroup = this.testProject.CreatePropertyGroupElement();
        this.testProject.InsertAfterChild(propertyGroup, this.testProject.Imports.First()); // insert just after the Common.Targets import.
        propertyGroup.AddProperty("Language", "NoCodeDOMProviderForThisLanguage");
        propertyGroup.AddProperty("TargetExt", ".notdll");

        this.WriteVersionFile();
        BuildResults result = await this.BuildAsync(Targets.GenerateAssemblyVersionInfo, logVerbosity: LoggerVerbosity.Minimal);
        string versionCsFilePath = Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile"));
        Assert.False(File.Exists(versionCsFilePath));
        Assert.Empty(result.LoggedEvents.OfType<BuildErrorEventArgs>());
        Assert.Empty(result.LoggedEvents.OfType<BuildWarningEventArgs>());
    }

    // TODO: add key container test.
    [Theory]
    [InlineData("keypair.snk", false)]
    [InlineData("public.snk", true)]
    [InlineData("protectedPair.pfx", true)]
    public async Task AssemblyInfo_HasKeyData(string keyFile, bool delaySigned)
    {
        TestUtilities.ExtractEmbeddedResource($@"Keys\{keyFile}", Path.Combine(this.projectDirectory, keyFile));
        this.testProject.AddProperty("SignAssembly", "true");
        this.testProject.AddProperty("AssemblyOriginatorKeyFile", keyFile);
        this.testProject.AddProperty("DelaySign", delaySigned.ToString());

        this.WriteVersionFile();
        BuildResults result = await this.BuildAsync(Targets.GenerateAssemblyVersionInfo, logVerbosity: LoggerVerbosity.Minimal);
        string versionCsContent = File.ReadAllText(
            Path.GetFullPath(
                Path.Combine(
                    this.projectDirectory,
                    result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile"))));
        this.Logger.WriteLine(versionCsContent);

        SyntaxTree sourceFile = CSharpSyntaxTree.ParseText(versionCsContent);
        SyntaxNode syntaxTree = await sourceFile.GetRootAsync();
        IEnumerable<VariableDeclaratorSyntax> fields = syntaxTree.DescendantNodes().OfType<VariableDeclaratorSyntax>();

        var publicKeyField = (LiteralExpressionSyntax)fields.SingleOrDefault(f => f.Identifier.ValueText == "PublicKey")?.Initializer.Value;
        var publicKeyTokenField = (LiteralExpressionSyntax)fields.SingleOrDefault(f => f.Identifier.ValueText == "PublicKeyToken")?.Initializer.Value;
        if (Path.GetExtension(keyFile) == ".pfx")
        {
            // No support for PFX (yet anyway), since they're encrypted.
            // Note for future: I think by this point, the user has typically already decrypted
            // the PFX and stored the key pair in a key container. If we knew how to find which one,
            // we could perhaps divert to that.
            Assert.Null(publicKeyField);
            Assert.Null(publicKeyTokenField);
        }
        else
        {
            Assert.Equal(
                "002400000480000094000000060200000024000052534131000400000100010067cea773679e0ecc114b7e1d442466a90bf77c755811a0d3962a546ed716525b6508abf9f78df132ffd3fb75fe604b3961e39c52d5dfc0e6c1fb233cb4fb56b1a9e3141513b23bea2cd156cb2ef7744e59ba6b663d1f5b2f9449550352248068e85b61c68681a6103cad91b3bf7a4b50d2fabf97e1d97ac34db65b25b58cd0dc",
                publicKeyField?.Token.ValueText);
            Assert.Equal("ca2d1515679318f5", publicKeyTokenField?.Token.ValueText);
        }
    }

    protected abstract void ApplyGlobalProperties(IDictionary<string, string> globalProperties);

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable("_NBGV_UnitTest", string.Empty);
        base.Dispose(disposing);
    }

    protected ProjectRootElement CreateProjectRootElement(string projectDirectory, string projectName)
    {
        using (var reader = XmlReader.Create(Assembly.GetExecutingAssembly().GetManifestResourceStream($"{ThisAssembly.RootNamespace}.test.prj")))
        {
            var pre = ProjectRootElement.Create(reader, this.projectCollection);
            pre.FullPath = Path.Combine(projectDirectory, projectName);
            pre.AddImport(Path.Combine(this.RepoPath, GitVersioningTargetsFileName));
            return pre;
        }
    }

    protected async Task<BuildResults> BuildAsync(string target = Targets.GetBuildVersion, LoggerVerbosity logVerbosity = LoggerVerbosity.Detailed, bool assertSuccessfulBuild = true)
    {
        var eventLogger = new MSBuildLogger { Verbosity = LoggerVerbosity.Minimal };
        var loggers = new ILogger[] { eventLogger };
        this.testProject.Save(); // persist generated project on disk for analysis
        this.ApplyGlobalProperties(this.globalProperties);
        BuildResult buildResult = await this.buildManager.BuildAsync(
            this.Logger,
            this.projectCollection,
            this.testProject,
            target,
            this.globalProperties,
            logVerbosity,
            loggers);
        var result = new BuildResults(buildResult, eventLogger.LoggedEvents);
        this.Logger.WriteLine(result.ToString());
        if (assertSuccessfulBuild)
        {
            Assert.Equal(BuildResultCode.Success, buildResult.OverallResult);
        }

        return result;
    }

    private void Init()
    {
        int seed = (int)DateTime.Now.Ticks;
        this.random = new Random(seed);
        this.Logger.WriteLine("Random seed: {0}", seed);
        this.buildManager = new BuildManager();
        this.projectCollection = new ProjectCollection();
        this.projectDirectory = Path.Combine(this.RepoPath, "projdir");
        Directory.CreateDirectory(this.projectDirectory);
        this.LoadTargetsIntoProjectCollection();
        this.testProject = this.CreateProjectRootElement(this.projectDirectory, "test.prj");
        this.globalProperties.Add("NerdbankGitVersioningTasksPath", Environment.CurrentDirectory + "\\");
        Environment.SetEnvironmentVariable("_NBGV_UnitTest", "true");

        // Sterilize the test of any environment variables.
        foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            string name = (string)variable.Key;
            if (ToxicEnvironmentVariablePrefixes.Any(toxic => name.StartsWith(toxic, StringComparison.OrdinalIgnoreCase)))
            {
                this.globalProperties[name] = string.Empty;
            }
        }
    }

    private void LoadTargetsIntoProjectCollection()
    {
        string prefix = $"{ThisAssembly.RootNamespace}.Targets.";

        IEnumerable<string> streamNames = from name in Assembly.GetExecutingAssembly().GetManifestResourceNames()
                          where name.StartsWith(prefix, StringComparison.Ordinal)
                          select name;
        foreach (string name in streamNames)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                var targetsFile = ProjectRootElement.Create(XmlReader.Create(stream), this.projectCollection);
                targetsFile.FullPath = Path.Combine(this.RepoPath, name.Substring(prefix.Length));
                targetsFile.Save(); // persist files on disk
            }
        }
    }

    protected static class Targets
    {
        internal const string Build = "Build";
        internal const string GetBuildVersion = "GetBuildVersion";
        internal const string GetNuGetPackageVersion = "GetNuGetPackageVersion";
        internal const string GenerateAssemblyVersionInfo = "GenerateAssemblyNBGVVersionInfo";
        internal const string GenerateNativeVersionInfo = "GenerateNativeVersionInfo";
    }

    protected class BuildResults
    {
        internal BuildResults(BuildResult buildResult, IReadOnlyList<BuildEventArgs> loggedEvents)
        {
            Requires.NotNull(buildResult, nameof(buildResult));
            this.BuildResult = buildResult;
            this.LoggedEvents = loggedEvents;
        }

        public BuildResult BuildResult { get; private set; }

        public IReadOnlyList<BuildEventArgs> LoggedEvents { get; private set; }

        public bool PublicRelease => string.Equals("true", this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("PublicRelease"), StringComparison.OrdinalIgnoreCase);

        public string BuildNumber => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildNumber");

        public string GitCommitId => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitCommitId");

        public string BuildVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildVersion");

        public string BuildVersionSimple => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildVersionSimple");

        public string PrereleaseVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("PrereleaseVersion");

        public string MajorMinorVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("MajorMinorVersion");

        public string BuildVersionNumberComponent => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildVersionNumberComponent");

        public string GitCommitIdShort => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitCommitIdShort");

        public string GitCommitDateTicks => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitCommitDateTicks");

        public string GitVersionHeight => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitVersionHeight");

        public string SemVerBuildSuffix => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("SemVerBuildSuffix");

        public string BuildVersion3Components => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("BuildVersion3Components");

        public string AssemblyInformationalVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyInformationalVersion");

        public string AssemblyFileVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyFileVersion");

        public string AssemblyVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyVersion");

        public string NuGetPackageVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("NuGetPackageVersion");

        public string ChocolateyPackageVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("ChocolateyPackageVersion");

        public string CloudBuildNumber => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("CloudBuildNumber");

        public string AssemblyName => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyName");

        public string AssemblyTitle => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyTitle");

        public string AssemblyProduct => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyProduct");

        public string AssemblyCompany => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyCompany");

        public string AssemblyCopyright => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("AssemblyCopyright");

        public string AssemblyConfiguration => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("Configuration");

        public string RootNamespace => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("RootNamespace");

        public string GitBuildVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitBuildVersion");

        public string GitBuildVersionSimple => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitBuildVersionSimple");

        public string GitAssemblyInformationalVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("GitAssemblyInformationalVersion");

        // Just a sampling of other properties optionally set in cloud build.
        public string NBGV_GitCommitIdShort => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("NBGV_GitCommitIdShort");

        public string NBGV_NuGetPackageVersion => this.BuildResult.ProjectStateAfterBuild.GetPropertyValue("NBGV_NuGetPackageVersion");

        public override string ToString()
        {
            var sb = new StringBuilder();

            foreach (PropertyInfo property in this.GetType().GetRuntimeProperties().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (property.DeclaringType == this.GetType() && property.Name != nameof(this.BuildResult))
                {
                    sb.AppendLine($"{property.Name} = {property.GetValue(this)}");
                }
            }

            return sb.ToString();
        }
    }

    protected class MSBuildLogger : ILogger
    {
        public string Parameters { get; set; }

        public LoggerVerbosity Verbosity { get; set; }

        public List<BuildEventArgs> LoggedEvents { get; } = new List<BuildEventArgs>();

        public void Initialize(IEventSource eventSource)
        {
            eventSource.AnyEventRaised += this.EventSource_AnyEventRaised;
        }

        public void Shutdown()
        {
        }

        private void EventSource_AnyEventRaised(object sender, BuildEventArgs e)
        {
            this.LoggedEvents.Add(e);
        }
    }
}
