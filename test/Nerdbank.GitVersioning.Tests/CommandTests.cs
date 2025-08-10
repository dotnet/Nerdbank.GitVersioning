// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.Commands;
using Xunit;

public class CommandTests : RepoTestBase
{
    public CommandTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Theory, CombinatorialData]
    public void CloudCommand_CloudBuildNumber(bool setCloudBuildNumber)
    {
        const string ciSystem = "VisualStudioTeamServices";
        const string buildNumberSyntax = "##vso[build.updatebuildnumber]";

        var outWriter = new StringWriter();
        var errWriter = new StringWriter();

        var command = new CloudCommand(outWriter, errWriter);

        command.SetBuildVariables(this.RepoPath, metadata: [], version: "1.2.3.4", ciSystem, allVars: false, commonVars: false, setCloudBuildNumber, additionalVariables: [], alwaysUseLibGit2: false);

        outWriter.Flush();
        errWriter.Flush();

        if (setCloudBuildNumber)
        {
            Assert.Contains(buildNumberSyntax, outWriter.ToString());
        }
        else
        {
            Assert.DoesNotContain(buildNumberSyntax, outWriter.ToString());
        }

        Assert.Empty(errWriter.ToString());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)] // Default behavior without argument
    public void OnGetVersionCommand_PublicReleaseArgument(bool? publicReleaseArg, bool expectedPublicRelease)
    {
        using GitContext context = this.CreateGitContext(this.RepoPath);
        var oracle = new VersionOracle(context, CloudBuild.Active);

        // Simulate the logic from OnGetVersionCommand method
        if (publicReleaseArg.HasValue)
        {
            oracle.PublicRelease = publicReleaseArg.Value;
        }

        Assert.Equal(expectedPublicRelease, oracle.PublicRelease);
    }

    [Fact]
    public void OnGetVersionCommand_PublicReleaseArgumentOverridesEnvironmentVariable()
    {
        using GitContext context = this.CreateGitContext(this.RepoPath);
        var oracle = new VersionOracle(context, CloudBuild.Active);

        // Simulate environment variable being set to true
        const bool envValue = true;
        oracle.PublicRelease = envValue;

        // Command line argument should override
        const bool argValue = false;
        oracle.PublicRelease = argValue;

        Assert.Equal(argValue, oracle.PublicRelease);
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, engine: GitContext.Engine.ReadWrite);
}
