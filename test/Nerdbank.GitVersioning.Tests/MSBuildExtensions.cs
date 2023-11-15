// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Locator;
using Microsoft.Build.Logging;
using Validation;
using Xunit.Abstractions;

internal static class MSBuildExtensions
{
    private static readonly object LoadLock = new object();
    private static bool loaded;

    internal static void LoadMSBuild()
    {
        lock (LoadLock)
        {
            if (!loaded)
            {
#if NET
                if (IntPtr.Size == 4)
                {
                    // 32-bit .NET runtime requires special code to find the x86 SDK (where MSBuild is).
                    MSBuildLocator.RegisterMSBuildPath(@"C:\Program Files (x86)\dotnet\sdk\8.0.100");
                }
                else
                {
                    MSBuildLocator.RegisterDefaults();
                }
#else
                MSBuildLocator.RegisterDefaults();
#endif

                loaded = true;
            }
        }
    }

    internal static async Task<BuildResult> BuildAsync(this BuildManager buildManager, ITestOutputHelper logger, ProjectCollection projectCollection, ProjectRootElement project, string target, IDictionary<string, string> globalProperties = null, LoggerVerbosity logVerbosity = LoggerVerbosity.Detailed, ILogger[] additionalLoggers = null)
    {
        Requires.NotNull(buildManager, nameof(buildManager));
        Requires.NotNull(projectCollection, nameof(projectCollection));
        Requires.NotNull(project, nameof(project));

        globalProperties = globalProperties ?? new Dictionary<string, string>();
        var projectInstance = new ProjectInstance(project, globalProperties, null, projectCollection);
        var brd = new BuildRequestData(projectInstance, new[] { target }, null, BuildRequestDataFlags.ProvideProjectStateAfterBuild);

        var parameters = new BuildParameters(projectCollection);

        var loggers = new List<ILogger>();
        loggers.Add(new ConsoleLogger(logVerbosity, s => logger.WriteLine(s.TrimEnd('\r', '\n')), null, null));
        loggers.AddRange(additionalLoggers);
        parameters.Loggers = loggers.ToArray();

        buildManager.BeginBuild(parameters);

        BuildResult result = await buildManager.BuildAsync(brd);

        buildManager.EndBuild();

        return result;
    }

    internal static Task<BuildResult> BuildAsync(this BuildManager buildManager, BuildRequestData buildRequestData)
    {
        Requires.NotNull(buildManager, nameof(buildManager));
        Requires.NotNull(buildRequestData, nameof(buildRequestData));

        var tcs = new TaskCompletionSource<BuildResult>();
        BuildSubmission submission = buildManager.PendBuildRequest(buildRequestData);
        submission.ExecuteAsync(s => tcs.SetResult(s.BuildResult), null);
        return tcs.Task;
    }
}
