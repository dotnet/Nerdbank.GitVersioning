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

    // TODO: This is tightly coupled to the cloud build service. Can I use a FakeCloudBuildService instead?
    [Theory, CombinatorialData]
    public void CloudCommand_CloudBuildNumber([CombinatorialValues("VisualStudioTeamServices", "TeamCity", "Jenkins")] string ciSystem, bool setCloudBuildNumber)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();

        var command = new CloudCommand(outWriter, errWriter);

        command.SetBuildVariables(this.RepoPath, metadata: [], version: "1.2.3.4", ciSystem, allVars: false, commonVars: false, setCloudBuildNumber, additionalVariables: [], alwaysUseLibGit2: false);

        outWriter.Flush();
        errWriter.Flush();

        if (setCloudBuildNumber)
        {
            Assert.NotEmpty(outWriter.ToString());
        }
        else
        {
            Assert.Empty(outWriter.ToString());
        }

        Assert.Empty(errWriter.ToString());
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, engine: GitContext.Engine.ReadWrite);
}
