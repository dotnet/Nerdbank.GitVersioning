// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, engine: GitContext.Engine.ReadWrite);
}
