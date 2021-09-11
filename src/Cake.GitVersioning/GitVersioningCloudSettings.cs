namespace Cake.GitVersioning
{
    using System.Collections.Generic;

    /// <summary>
    /// Defines settings for the <see cref="GitVersioningAliases.GitVersioningCloud"/> alias.
    /// </summary>
    public class GitVersioningCloudSettings
    {
        /// <summary>
        /// The string to use for the cloud build number. 
        /// If not value os specified, the computed version will be used.
        /// </summary>
        public string Version { get; set; } = null;

        /// <summary>
        /// Adds an identifier to the build metadata part of a semantic version.
        /// </summary>
        public IList<string> Metadata { get; set; } = new List<string>();

        /// <summary>
        /// Force activation for a particular CI system. If not specified,
        /// auto-detection will be used.
        /// </summary>
        public GitVersioningCloudProvider? CISystem { get; set; } = null;

        /// <summary>
        /// Defines ALL version variables as cloud build variables, with a "NBGV_" prefix.
        /// </summary>
        public bool AllVariables { get; set; } = false;

        /// <summary>
        /// Defines a few common version variables as cloud build variables, with a "Git" prefix.
        /// </summary>
        public bool CommonVariables { get; set; } = false;

        /// <summary>
        /// Additional cloud build variables to define.
        /// </summary>
        public IDictionary<string, string> AdditionalVariables { get; set; } = new Dictionary<string, string>();
    }
}
