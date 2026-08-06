// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Nerdbank.GitVersioning.Tasks
{
    public class SetCloudBuildVariables : Microsoft.Build.Utilities.Task
    {
        public ITaskItem[] CloudBuildVersionVars { get; set; }

        [Output]
        public ITaskItem[] MSBuildPropertyUpdates { get; set; }

        public string CloudBuildNumber { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether cloud build output should be redirected through the MSBuild log.
        /// </summary>
        public bool RedirectOutput { get; set; }

        /// <inheritdoc/>
        public override bool Execute()
        {
            ICloudBuild cloudBuild = CloudBuild.Active;
            if (cloudBuild is not null)
            {
                var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Take care in a unit test environment because it would actually
                // adversely impact the build variables of the cloud build underway that
                // is running the tests.
                var testStdOut = new StringBuilder();
                var testStdErr = new StringBuilder();
                TextWriter stdout = this.RedirectOutput ? new StringWriter(testStdOut) : Console.Out;
                TextWriter stderr = this.RedirectOutput ? new StringWriter(testStdErr) : Console.Error;

                if (!string.IsNullOrWhiteSpace(this.CloudBuildNumber))
                {
                    IReadOnlyDictionary<string, string> newVars = cloudBuild.SetCloudBuildNumber(this.CloudBuildNumber, stdout, stderr);
                    foreach (KeyValuePair<string, string> item in newVars)
                    {
                        envVars[item.Key] = item.Value;
                    }
                }

                if (this.CloudBuildVersionVars is not null)
                {
                    foreach (ITaskItem variable in this.CloudBuildVersionVars)
                    {
                        IReadOnlyDictionary<string, string> newVars = cloudBuild.SetCloudBuildVariable(variable.ItemSpec, variable.GetMetadata("Value"), stdout, stderr);
                        foreach (KeyValuePair<string, string> item in newVars)
                        {
                            envVars[item.Key] = item.Value;
                        }
                    }
                }

                this.MSBuildPropertyUpdates = (from envVar in envVars
                                               let metadata = new Dictionary<string, string> { { "Value", envVar.Value } }
                                               select new TaskItem(envVar.Key, metadata)).ToArray();

                foreach (KeyValuePair<string, string> item in envVars)
                {
                    Environment.SetEnvironmentVariable(item.Key, item.Value);
                }

                if (this.RedirectOutput)
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
