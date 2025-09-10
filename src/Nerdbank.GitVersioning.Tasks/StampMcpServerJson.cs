// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Nerdbank.GitVersioning.Tasks
{
    /// <summary>
    /// MSBuild task that stamps version information into an MCP server.json file.
    /// </summary>
    public class StampMcpServerJson : Microsoft.Build.Utilities.Task
{
        /// <summary>
        /// Gets or sets the path to the source server.json file.
        /// </summary>
        [Required]
        public string SourceServerJson { get; set; }

        /// <summary>
        /// Gets or sets the path where the stamped server.json file should be written.
        /// </summary>
        [Required]
        public string OutputServerJson { get; set; }

        /// <summary>
        /// Gets or sets the version to stamp into the server.json file.
        /// </summary>
        [Required]
        public string Version { get; set; }

        /// <summary>
        /// Executes the task to stamp version information into the MCP server.json file.
        /// </summary>
        /// <returns><see langword="true"/> if the task succeeded; <see langword="false"/> otherwise.</returns>
        public override bool Execute()
        {
            try
            {
                if (string.IsNullOrEmpty(this.SourceServerJson) || string.IsNullOrEmpty(this.OutputServerJson) || string.IsNullOrEmpty(this.Version))
                {
                    this.Log.LogError("SourceServerJson, OutputServerJson, and Version are required parameters.");
                    return false;
                }

                if (!File.Exists(this.SourceServerJson))
                {
                    this.Log.LogError($"Source server.json file not found: {this.SourceServerJson}");
                    return false;
                }

                // Ensure output directory exists
                string outputDir = Path.GetDirectoryName(this.OutputServerJson);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Read and parse the server.json file
                string jsonContent = File.ReadAllText(this.SourceServerJson);
                JsonNode jsonNode = JsonNode.Parse(jsonContent);

                if (jsonNode is JsonObject jsonObject)
                {
                    // Stamp the version
                    jsonObject["version"] = this.Version;

                    // Write the updated JSON with indentation for readability
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    };

                    string updatedJson = JsonSerializer.Serialize(jsonObject, options);
                    File.WriteAllText(this.OutputServerJson, updatedJson);

                    this.Log.LogMessage(MessageImportance.Low, $"Stamped version '{this.Version}' into server.json: {this.OutputServerJson}");
                    return true;
                }
                else
                {
                    this.Log.LogError($"server.json does not contain a valid JSON object: {this.SourceServerJson}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                this.Log.LogErrorFromException(ex);
                return false;
            }
        }
    }
}
