namespace Nerdbank.GitVersioning.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;

    public class SetCloudBuildVariables : Task
    {
        public ITaskItem[] CloudBuildVersionVars { get; set; }

        [Output]
        public ITaskItem[] MSBuildPropertyUpdates { get; set; }

        public string CloudBuildNumber { get; set; }

        public override bool Execute()
        {
            var cloudBuild = CloudBuild.Active;
            if (cloudBuild is not null)
            {
                var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Take care in a unit test environment because it would actually
                // adversely impact the build variables of the cloud build underway that
                // is running the tests.
                bool isUnitTest = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("_NBGV_UnitTest"));
                var testStdOut = new StringBuilder();
                var testStdErr = new StringBuilder();
                TextWriter stdout = isUnitTest ? new StringWriter(testStdOut) : Console.Out;
                TextWriter stderr = isUnitTest ? new StringWriter(testStdErr) : Console.Error;

                if (!string.IsNullOrWhiteSpace(this.CloudBuildNumber))
                {
                    var newVars = cloudBuild.SetCloudBuildNumber(this.CloudBuildNumber, stdout, stderr);
                    foreach (var item in newVars)
                    {
                        envVars[item.Key] = item.Value;
                    }
                }

                if (this.CloudBuildVersionVars is not null)
                {
                    foreach (var variable in this.CloudBuildVersionVars)
                    {
                        var newVars = cloudBuild.SetCloudBuildVariable(variable.ItemSpec, variable.GetMetadata("Value"), stdout, stderr);
                        foreach (var item in newVars)
                        {
                            envVars[item.Key] = item.Value;
                        }
                    }
                }

                this.MSBuildPropertyUpdates = (from envVar in envVars
                                               let metadata = new Dictionary<string, string> { { "Value", envVar.Value } }
                                               select new TaskItem(envVar.Key, metadata)).ToArray();

                foreach (var item in envVars)
                {
                    Environment.SetEnvironmentVariable(item.Key, item.Value);
                }

                if (isUnitTest)
                {
                    this.PipeOutputToMSBuildLog(testStdOut.ToString(), warning: false);
                    this.PipeOutputToMSBuildLog(testStdErr.ToString(), warning: true);
                }
            }
            else
            {
                this.Log.LogMessage(MessageImportance.Low, "No supported cloud build detected, so no variables or build number set.");
            }

            return !this.Log.HasLoggedErrors;
        }

        private void PipeOutputToMSBuildLog(string output, bool warning)
        {
            using (var logReader = new StringReader(output))
            {
                string line;
                while ((line = logReader.ReadLine()) is not null)
                {
                    // The prefix is presumed to nullify the effect in a real cloud build,
                    // yet make it detectable by a unit test.
                    string message = $"UnitTest: {line}";
                    if (warning)
                    {
                        this.Log.LogWarning(message);
                    }
                    else
                    {
                        this.Log.LogMessage(message);
                    }
                }
            }
        }
    }
}
