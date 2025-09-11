// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

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
                return !this.Log.HasLoggedErrors;
            }

            if (!File.Exists(this.SourceServerJson))
            {
                this.Log.LogError($"Source server.json file not found: {this.SourceServerJson}");
                return !this.Log.HasLoggedErrors;
            }

            // Ensure output directory exists
            string outputDir = Path.GetDirectoryName(this.OutputServerJson);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Read and parse the server.json file
            string jsonContent = File.ReadAllText(this.SourceServerJson);
            JsonNode jsonNode = JsonNode.Parse(jsonContent);

            if (jsonNode is JsonObject jsonObject)
            {
                // Replace all __VERSION__ placeholders in the JSON tree
                this.ReplaceVersionPlaceholders(jsonNode, this.Version);

                // Write the updated JSON with indentation for readability
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                };

                string updatedJson = JsonSerializer.Serialize(jsonObject, options);
                File.WriteAllText(this.OutputServerJson, updatedJson);

                this.Log.LogMessage(MessageImportance.Low, $"Stamped version '{this.Version}' into server.json: {this.OutputServerJson}");
            }
            else
            {
                this.Log.LogError($"server.json does not contain a valid JSON object: {this.SourceServerJson}");
            }
        }
        catch (Exception ex)
        {
            this.Log.LogErrorFromException(ex);
        }

        return !this.Log.HasLoggedErrors;
    }

    /// <summary>
    /// Recursively walks the JSON tree and replaces any string values containing "__VERSION__" with the actual version.
    /// </summary>
    /// <param name="node">The JSON node to process.</param>
    /// <param name="version">The version string to replace "__VERSION__" with.</param>
    private void ReplaceVersionPlaceholders(JsonNode node, string version)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToArray())
                {
                    if (property.Value != null)
                    {
                        this.ReplaceVersionPlaceholders(property.Value, version);
                    }
                }
                break;

            case JsonArray jsonArray:
                for (int i = 0; i < jsonArray.Count; i++)
                {
                    if (jsonArray[i] != null)
                    {
                        this.ReplaceVersionPlaceholders(jsonArray[i], version);
                    }
                }
                break;

            case JsonValue jsonValue:
                if (jsonValue.TryGetValue<string>(out string stringValue) && stringValue.Contains("__VERSION__"))
                {
                    string replacedValue = stringValue.Replace("__VERSION__", version);
                    JsonNode parent = jsonValue.Parent;
                    if (parent is JsonObject parentObject)
                    {
                        // Find the property key for this value
                        foreach (var kvp in parentObject)
                        {
                            if (ReferenceEquals(kvp.Value, jsonValue))
                            {
                                parentObject[kvp.Key] = replacedValue;
                                break;
                            }
                        }
                    }
                    else if (parent is JsonArray parentArray)
                    {
                        // Find the index for this value
                        for (int i = 0; i < parentArray.Count; i++)
                        {
                            if (ReferenceEquals(parentArray[i], jsonValue))
                            {
                                parentArray[i] = replacedValue;
                                break;
                            }
                        }
                    }
                }
                break;
        }
    }
}
