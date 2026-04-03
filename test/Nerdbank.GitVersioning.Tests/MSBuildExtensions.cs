// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Locator;
using Microsoft.Build.Logging;
using Validation;
using Xunit;

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
                    MSBuildLocator.RegisterMSBuildPath(GetX86DotNetSdkPath());
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

#if NET
    private static string GetX86DotNetSdkPath()
    {
        string sdkVersion = GetSdkVersion();
        string[] dotnetRoots =
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT_X86"),
            Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet"),
        };

        foreach (string dotnetRoot in dotnetRoots)
        {
            if (string.IsNullOrWhiteSpace(dotnetRoot))
            {
                continue;
            }

            string candidate = Path.Combine(dotnetRoot, "sdk", sdkVersion);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"Could not find x86 .NET SDK '{sdkVersion}'.");
    }

    private static string GetSdkVersion()
    {
        string globalJsonPath = FindFileInAncestors(AppContext.BaseDirectory, "global.json");
        if (string.IsNullOrEmpty(globalJsonPath))
        {
            throw new InvalidOperationException($"Could not find global.json by searching parent directories of '{AppContext.BaseDirectory}'.");
        }

        using JsonDocument globalJson = JsonDocument.Parse(File.ReadAllText(globalJsonPath));
        if (!globalJson.RootElement.TryGetProperty("sdk", out JsonElement sdkElement)
            || !sdkElement.TryGetProperty("version", out JsonElement versionElement))
        {
            throw new InvalidOperationException($"The SDK version could not be determined from '{globalJsonPath}'.");
        }

        string sdkVersion = versionElement.GetString();
        if (string.IsNullOrEmpty(sdkVersion))
        {
            throw new InvalidOperationException($"The SDK version could not be determined from '{globalJsonPath}'.");
        }

        return sdkVersion;
    }

    private static string FindFileInAncestors(string startingDirectory, string fileName)
    {
        DirectoryInfo directory = new(startingDirectory);
        while (true)
        {
            string candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (directory.Parent is null)
            {
                return string.Empty;
            }

            directory = directory.Parent;
        }
    }
#endif
}
