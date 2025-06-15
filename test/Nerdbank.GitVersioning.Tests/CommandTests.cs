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

        command.SetBuildVariables(this.RepoPath, metadata: [], version: "1.2.3.4", ciSystem, allVars: false, commonVars: false, setCloudBuildNumber, additionalVariables: [], alwaysUseLibGit2: false, withOutput: false);

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

    [Theory, CombinatorialData]
    public void CloudCommand_WithOutputFlag(bool withOutput)
    {
        const string ciSystem = "VisualStudioTeamServices";
        const string outputVariableSyntax = "##vso[task.setvariable variable=TestVar;isOutput=true]";
        const string regularVariableSyntax = "##vso[task.setvariable variable=TestVar;]";

        var outWriter = new StringWriter();
        var errWriter = new StringWriter();

        var command = new CloudCommand(outWriter, errWriter);
        var additionalVariables = new Dictionary<string, string> { { "TestVar", "TestValue" } };

        command.SetBuildVariables(this.RepoPath, metadata: [], version: "1.2.3.4", ciSystem, allVars: false, commonVars: false, cloudBuildNumber: false, additionalVariables, alwaysUseLibGit2: false, withOutput);

        outWriter.Flush();
        errWriter.Flush();

        var output = outWriter.ToString();

        // Regular variable should always be set
        Assert.Contains(regularVariableSyntax, output);

        // Output variable should only be set when withOutput is true
        if (withOutput)
        {
            Assert.Contains(outputVariableSyntax, output);
        }
        else
        {
            Assert.DoesNotContain(outputVariableSyntax, output);
        }

        Assert.Empty(errWriter.ToString());
    }

    [Theory, CombinatorialData]
    public void CloudCommand_AllVarsWithOutput(bool withOutput)
    {
        const string ciSystem = "VisualStudioTeamServices";

        var outWriter = new StringWriter();
        var errWriter = new StringWriter();

        var command = new CloudCommand(outWriter, errWriter);

        command.SetBuildVariables(this.RepoPath, metadata: [], version: "1.2.3.4", ciSystem, allVars: true, commonVars: false, cloudBuildNumber: false, additionalVariables: [], alwaysUseLibGit2: false, withOutput);

        outWriter.Flush();
        errWriter.Flush();

        var output = outWriter.ToString();

        // Should contain some NBGV_ variables (from --all-vars)
        Assert.Contains("##vso[task.setvariable variable=NBGV_", output);

        // Check if output variables are present based on withOutput flag
        var outputVariableCount = CountOccurrences(output, ";isOutput=true;");
        var regularVariableCount = CountOccurrences(output, "##vso[task.setvariable variable=");

        if (withOutput)
        {
            // Should have output variables for each regular variable
            Assert.True(outputVariableCount > 0, "Should have output variables when withOutput=true");
            Assert.Equal(regularVariableCount, outputVariableCount * 2); // Each variable creates 2 lines: regular + output
        }
        else
        {
            // Should have no output variables
            Assert.Equal(0, outputVariableCount);
        }

        Assert.Empty(errWriter.ToString());
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, engine: GitContext.Engine.ReadWrite);
}
