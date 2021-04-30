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
        public static readonly ICloudBuild[] SupportedCloudBuilds = new ICloudBuild[] {
            new AppVeyor(),
            new VisualStudioTeamServices(),
            new GitHubActions(),
            new TeamCity(),
            new AtlassianBamboo(), 
            new Jenkins(),
            new GitLab(),
            new Travis(),
            new SpaceAutomation(),
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

        /// <summary>
        /// Gets the specified string if it starts with a given prefix; otherwise null.
        /// </summary>
        /// <param name="value">The value to return.</param>
        /// <param name="prefix">The prefix to check for.</param>
        /// <returns><paramref name="value"/> if it starts with <paramref name="prefix"/>; otherwise <c>null</c>.</returns>
        internal static string IfStartsWith(string value, string prefix) => value is object && value.StartsWith(prefix, StringComparison.Ordinal) ? value : null;
    }
}
