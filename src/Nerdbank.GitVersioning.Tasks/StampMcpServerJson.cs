// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Framework;

namespace Nerdbank.GitVersioning.Tasks;

/// <summary>
/// MSBuild task that stamps version information into an MCP server.json file.
/// </summary>
public class StampMcpServerJson : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Gets or sets the path to the source server.json file.
    /// </summary>
    [Required]
    public ITaskItem SourceServerJson { get; set; }

    /// <summary>
    /// Gets or sets the path where the stamped server.json file should be written.
    /// </summary>
    [Required]
    public ITaskItem OutputServerJson { get; set; }

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
            if (this.SourceServerJson is null || this.OutputServerJson is null || string.IsNullOrEmpty(this.Version))
            {
                this.Log.LogError("SourceServerJson, OutputServerJson, and Version are required parameters.");
                return !this.Log.HasLoggedErrors;
            }

            string sourceServerJson = this.SourceServerJson.GetMetadata("FullPath");
            string outputServerJson = this.OutputServerJson.GetMetadata("FullPath");
            if (!File.Exists(sourceServerJson))
            {
                this.Log.LogError($"Source server.json file not found: {sourceServerJson}");
                return !this.Log.HasLoggedErrors;
            }

            // Ensure output directory exists
            string outputDir = Path.GetDirectoryName(outputServerJson);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Read the server.json file and replace version placeholders
            string jsonContent = File.ReadAllText(sourceServerJson);
            jsonContent = jsonContent.Replace("\"0.0.0-placeholder\"", $"\"{this.Version}\"");

            File.WriteAllText(outputServerJson, jsonContent);
            this.Log.LogMessage(MessageImportance.Low, $"Stamped version '{this.Version}' into server.json: {outputServerJson}");
        }
        catch (Exception ex)
        {
            this.Log.LogErrorFromException(ex);
        }

        return !this.Log.HasLoggedErrors;
    }
}
