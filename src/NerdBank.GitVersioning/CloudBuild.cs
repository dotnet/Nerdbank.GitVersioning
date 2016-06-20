namespace Nerdbank.GitVersioning
{
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
        };

        /// <summary>
        /// Gets the cloud build provider that applies to this build, if any.
        /// </summary>
        public static ICloudBuild Active => SupportedCloudBuilds.FirstOrDefault(cb => cb.IsApplicable);
    }
}
