using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Validation;
using Xunit.Abstractions;

internal static class MSBuildExtensions
{
    internal static async Task<BuildResult> BuildAsync(this BuildManager buildManager, ITestOutputHelper logger, ProjectCollection projectCollection, ProjectRootElement project, string target, IDictionary<string, string> globalProperties = null)
    {
        Requires.NotNull(buildManager, nameof(buildManager));
        Requires.NotNull(projectCollection, nameof(projectCollection));
        Requires.NotNull(project, nameof(project));

        globalProperties = globalProperties ?? new Dictionary<string, string>();
        var projectInstance = new ProjectInstance(project, globalProperties, null, projectCollection);
        var brd = new BuildRequestData(projectInstance, new[] { target }, null, BuildRequestDataFlags.ProvideProjectStateAfterBuild);

        var parameters = new BuildParameters(projectCollection);
        parameters.Loggers = new ILogger[]
        {
            new ConsoleLogger(LoggerVerbosity.Detailed, s => logger.WriteLine(s.TrimEnd('\r', '\n')), null, null),
        };
        buildManager.BeginBuild(parameters);

        var result = await buildManager.BuildAsync(brd);

        buildManager.EndBuild();

        return result;
    }

    internal static Task<BuildResult> BuildAsync(this BuildManager buildManager, BuildRequestData buildRequestData)
    {
        Requires.NotNull(buildManager, nameof(buildManager));
        Requires.NotNull(buildRequestData, nameof(buildRequestData));

        var tcs = new TaskCompletionSource<BuildResult>();
        var submission = buildManager.PendBuildRequest(buildRequestData);
        submission.ExecuteAsync(s => tcs.SetResult(s.BuildResult), null);
        return tcs.Task;
    }
}
