// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Globalization;
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
using Version = System.Version;

/// <content>
/// The TUnit tests declared directly on <see cref="BuildIntegrationTests"/>.
/// The rest of the class is test infrastructure, which is shared with the Nerdbank.GitVersioning.Combinatorial.Tests project.
/// </content>
public abstract partial class BuildIntegrationTests
{
    [Test]
    public async Task GetBuildVersion_Returns_BuildVersion_Property()
    {
        this.WriteVersionFile();
        this.InitializeSourceControl();
        BuildResults buildResult = await this.BuildAsync();
        Assert.Equal(
            buildResult.BuildVersion,
            buildResult.BuildResult.ResultsByTarget[Targets.GetBuildVersion].Items.Single().ItemSpec);
    }

    [Test]
    public async Task GetBuildVersion_Without_Git()
    {
        this.WriteVersionFile("3.4");
        BuildResults buildResult = await this.BuildAsync();
        Assert.Equal("3.4", buildResult.BuildVersion);
        Assert.Equal("3.4.0", buildResult.AssemblyInformationalVersion);
    }

    [Test]
    public async Task GetBuildVersion_Hooks_Clean()
    {
        this.WriteVersionFile("1.2");
        BuildResults buildResult = await this.BuildAsync("Clean");
        Assert.Equal("1.2", buildResult.BuildVersion);
    }

    [Test]
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

    [Test]
    public async Task WithExtraPrereleaseIdentifiers()
    {
        this.WriteVersionFile(new VersionOptions
        {
            Version = SemanticVersion.Parse("3.4"),
        });
        this.InitializeSourceControl();
        this.testProject.AddItem("PrereleaseIdentifier", "i1");
        this.testProject.AddItem("PrereleaseIdentifier", "i2");
        this.globalProperties["PublicRelease"] = "true";
        BuildResults buildResult = await this.BuildAsync();
        Assert.Matches(@"^3\.4\.[01]-i1-i2$", buildResult.NuGetPackageVersion);
    }

    // TODO: add key container test.
    [Test]
    [Arguments("keypair.snk", false)]
    [Arguments("public.snk", true)]
    [Arguments("protectedPair.pfx", true)]
    public async Task AssemblyInfo_HasKeyData(string keyFile, bool delaySigned)
    {
        TestUtilities.ExtractEmbeddedResource($@"Keys\{keyFile}", Path.Combine(this.projectDirectory, keyFile));
        this.testProject.AddProperty("SignAssembly", "true");
        this.testProject.AddProperty("AssemblyOriginatorKeyFile", keyFile);
        this.testProject.AddProperty("DelaySign", delaySigned.ToString());

        this.WriteVersionFile();
        BuildResults result = await this.BuildAsync(Targets.GenerateAssemblyNBGVVersionInfo, logVerbosity: LoggerVerbosity.Minimal);
        string versionCsContent = File.ReadAllText(
            Path.GetFullPath(
                Path.Combine(
                    this.projectDirectory,
                    result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile"))));
        this.Logger.WriteLine(versionCsContent);

        SyntaxTree sourceFile = CSharpSyntaxTree.ParseText(versionCsContent, cancellationToken: TestContext.Current!.Execution.CancellationToken);
        SyntaxNode syntaxTree = await sourceFile.GetRootAsync(TestContext.Current!.Execution.CancellationToken);
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

    /// <summary>
    /// Emulate a project with an unsupported language, and verify that
    /// no errors are emitted because the target is skipped.
    /// </summary>
    [Test]
    public async Task AssemblyInfo_Suppressed()
    {
        ProjectPropertyGroupElement propertyGroup = this.testProject.CreatePropertyGroupElement();
        this.testProject.AppendChild(propertyGroup);
        propertyGroup.AddProperty("Language", "NoCodeDOMProviderForThisLanguage");
        propertyGroup.AddProperty(Properties.GenerateAssemblyVersionInfo, "false");

        this.WriteVersionFile();
        BuildResults result = await this.BuildAsync(Targets.GenerateAssemblyNBGVVersionInfo, logVerbosity: LoggerVerbosity.Minimal);
        string versionCsFilePath = Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile"));
        Assert.False(File.Exists(versionCsFilePath));
        Assert.Empty(result.LoggedEvents.OfType<BuildErrorEventArgs>());
        Assert.Empty(result.LoggedEvents.OfType<BuildWarningEventArgs>());
    }

    /// <summary>
    /// Emulate a project with an unsupported language, and verify that
    /// no errors are emitted because the target is skipped.
    /// </summary>
    [Test]
    public async Task AssemblyInfo_SuppressedImplicitlyByTargetExt()
    {
        ProjectPropertyGroupElement propertyGroup = this.testProject.CreatePropertyGroupElement();
        this.testProject.InsertAfterChild(propertyGroup, this.testProject.Imports.First()); // insert just after the Common.Targets import.
        propertyGroup.AddProperty("Language", "NoCodeDOMProviderForThisLanguage");
        propertyGroup.AddProperty("TargetExt", ".notdll");

        this.WriteVersionFile();
        BuildResults result = await this.BuildAsync(Targets.GenerateAssemblyNBGVVersionInfo, logVerbosity: LoggerVerbosity.Minimal);
        string versionCsFilePath = Path.Combine(this.projectDirectory, result.BuildResult.ProjectStateAfterBuild.GetPropertyValue("VersionSourceFile"));
        Assert.False(File.Exists(versionCsFilePath));
        Assert.Empty(result.LoggedEvents.OfType<BuildErrorEventArgs>());
        Assert.Empty(result.LoggedEvents.OfType<BuildWarningEventArgs>());
    }
}
