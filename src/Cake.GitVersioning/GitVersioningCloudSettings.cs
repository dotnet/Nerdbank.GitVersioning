// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Cake.GitVersioning
{
    /// <summary>
    /// Defines settings for the <see cref="GitVersioningAliases.GitVersioningCloud"/> alias.
    /// </summary>
    public class GitVersioningCloudSettings
    {
        /// <summary>
        /// Gets or sets the string to use for the cloud build number.
        /// If no value is specified, the computed version will be used.
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Gets a list of identifiers to include with the build metadata part of a semantic version.
        /// </summary>
        public List<string> Metadata { get; } = new();

        /// <summary>
        /// Gets or sets a a particular CI system to force usage of.
        /// If not specified, auto-detection will be used.
        /// </summary>
        public GitVersioningCloudProvider? CISystem { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to define ALL version variables as cloud build variables, with a "NBGV_" prefix.
        /// </summary>
        public bool AllVariables { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to define a few common version variables as cloud build variables, with a "Git" prefix.
        /// </summary>
        public bool CommonVariables { get; set; }

        /// <summary>
        /// Gets additional cloud build variables to define.
        /// </summary>
        public Dictionary<string, string> AdditionalVariables { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
