namespace Nerdbank.GitVersioning.CloudBuildServices
{
    using System;
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
        public bool IsPullRequest => false; // VSTS doesn't define this.

        public string BuildingTag => null; // VSTS doesn't define this.

        public string BuildingBranch => Environment.GetEnvironmentVariable("BUILD_SOURCEBRANCH");

        public string BuildingRef => this.BuildingBranch;

        public string GitCommitId => null;

        public bool IsApplicable => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSTEM_TEAMPROJECTID"));

        public void SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
        {
            (stdout ?? Console.Out).WriteLine($"##vso[build.updatebuildnumber]{buildNumber}");
            SetEnvVariableForBuildVariable("Build.BuildNumber", buildNumber);
        }

        public void SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
        {
            (stdout ?? Console.Out).WriteLine($"##vso[task.setvariable variable={name};]{value}");
            SetEnvVariableForBuildVariable(name, value);
        }

        private static void SetEnvVariableForBuildVariable(string name, string value)
        {
            string envVarName = name.ToUpperInvariant().Replace('.', '_');
            Environment.SetEnvironmentVariable(envVarName, value);
        }
    }
}
