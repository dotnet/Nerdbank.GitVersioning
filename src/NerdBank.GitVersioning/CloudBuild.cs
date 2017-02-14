namespace Nerdbank.GitVersioning
{
    using System;
    using System.Linq;
    using CloudBuildServices;

    /// <summary>
    /// Provides access to cloud build providers.
    /// </summary>
    public static class CloudBuild
    {
        /// <summary>
        /// An array of cloud build systems we support.
        /// </summary>
        private static readonly ICloudBuild[] SupportedCloudBuilds = new ICloudBuild[] {
            new AppVeyor(),
            new VisualStudioTeamServices(),
            new TeamCity(),
            new AtlassianBamboo(), 
        };

        /// <summary>
        /// Gets the cloud build provider that applies to this build, if any.
        /// </summary>
        public static ICloudBuild Active => SupportedCloudBuilds.FirstOrDefault(cb => cb.IsApplicable);

        /// <summary>
        /// Gets the specified string, prefixing it with some value if it is non-empty and lacks the prefix.
        /// </summary>
        /// <param name="prefix">The prefix that should be included in the returned value.</param>
        /// <param name="value">The value to prefix.</param>
        /// <returns>The <paramref name="value" /> provided, with <paramref name="prefix" /> prepended
        /// if the value doesn't already start with that string and the value is non-empty.</returns>
        internal static string ShouldStartWith(string value, string prefix)
        {
            return
                string.IsNullOrEmpty(value) ? value :
                value.StartsWith(prefix, StringComparison.Ordinal) ? value :
                prefix + value;
        }
    }
}
