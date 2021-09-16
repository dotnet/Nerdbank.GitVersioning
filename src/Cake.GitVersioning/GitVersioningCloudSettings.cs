#nullable enable

namespace Cake.GitVersioning
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Defines settings for the <see cref="GitVersioningAliases.GitVersioningCloud"/> alias.
    /// </summary>
    public class GitVersioningCloudSettings
    {
        /// <summary>
        /// The string to use for the cloud build number.
        /// If no value is specified, the computed version will be used.
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Adds an identifier to the build metadata part of a semantic version.
        /// </summary>
        public List<string> Metadata { get; } = new();

        /// <summary>
        /// Force activation for a particular CI system. If not specified,
        /// auto-detection will be used.
        /// </summary>
        public GitVersioningCloudProvider? CISystem { get; set; }

        /// <summary>
        /// Defines ALL version variables as cloud build variables, with a "NBGV_" prefix.
        /// </summary>
        public bool AllVariables { get; set; }

        /// <summary>
        /// Defines a few common version variables as cloud build variables, with a "Git" prefix.
        /// </summary>
        public bool CommonVariables { get; set; }

        /// <summary>
        /// Additional cloud build variables to define.
        /// </summary>
        public Dictionary<string, string> AdditionalVariables { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
