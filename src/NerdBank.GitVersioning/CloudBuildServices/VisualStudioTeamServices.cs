namespace Nerdbank.GitVersioning.CloudBuildServices
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// The VSTS-specific properties referenced here are documented here:
    /// https://msdn.microsoft.com/en-us/Library/vs/alm/Build/scripts/variables
    /// </remarks>
    internal class VisualStudioTeamServices : ICloudBuild
    {
        public bool IsPullRequest => BuildingRef?.StartsWith("refs/pull/") ?? false;

        public string BuildingTag => CloudBuild.IfStartsWith(BuildingRef, "refs/tags/");

        public string BuildingBranch => CloudBuild.IfStartsWith(BuildingRef, "refs/heads/");

        public string GitCommitId => null;

        public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSTEM_TEAMPROJECTID"));

        private static string BuildingRef => Environment.GetEnvironmentVariable("BUILD_SOURCEBRANCH");

        public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
        {
            (stdout ?? Console.Out).WriteLine($"##vso[build.updatebuildnumber]{buildNumber}");
            return GetDictionaryFor("Build.BuildNumber", buildNumber);
        }

        public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
        {
            Utilities.FileOperationWithRetry(() =>
                (stdout ?? Console.Out).WriteLine($"##vso[task.setvariable variable={name};]{value}"));
            return GetDictionaryFor(name, value);
        }

        private static IReadOnlyDictionary<string, string> GetDictionaryFor(string variableName, string value)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { GetEnvironmentVariableNameForVariable(variableName), value },
            };
        }

        private static string GetEnvironmentVariableNameForVariable(string name) => name.ToUpperInvariant().Replace('.', '_');
    }
}
