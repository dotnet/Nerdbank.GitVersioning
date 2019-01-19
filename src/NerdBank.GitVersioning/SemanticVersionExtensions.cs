using System;

namespace Nerdbank.GitVersioning
{
    /// <summary>
    /// Extension methods for <see cref="SemanticVersion"/>
    /// </summary>
    public static class SemanticVersionExtensions
    {
        /// <summary>
        /// Gets a new semantic with the specified version component (major/minor) incremented.
        /// </summary>
        /// <param name="currentVersion">The version to increment.</param>
        /// <param name="increment">Specifies whether to increment the major or minor version.</param>
        /// <returns>Returns a new <see cref="SemanticVersion"/> object with either the major or minor version incremented by 1.</returns>
        public static SemanticVersion Increment(this SemanticVersion currentVersion, VersionOptions.ReleaseVersionIncrement increment)
        {
            if (increment != VersionOptions.ReleaseVersionIncrement.Major && increment != VersionOptions.ReleaseVersionIncrement.Minor)
                throw new ArgumentException($"Unexpected increment value '{increment}'", nameof(increment));

            var majorIncrement = increment == VersionOptions.ReleaseVersionIncrement.Major ? 1 : 0;
            var minorIncrement = increment == VersionOptions.ReleaseVersionIncrement.Minor ? 1 : 0;
            
            // use the appropriate constructor for the new version object
            // dependening on whether the current versions has 2, 3 or 4 segements
            Version newVersion;
            if (currentVersion.Version.Build >= 0 && currentVersion.Version.Revision > 0)
            {
                // 4 segement version
                newVersion = new Version(
                    currentVersion.Version.Major + majorIncrement,
                    currentVersion.Version.Minor + minorIncrement,
                    currentVersion.Version.Build,
                    currentVersion.Version.Revision);
            }
            else if (currentVersion.Version.Build >= 0)
            {
                // 3 segement version
                newVersion = new Version(
                    currentVersion.Version.Major + majorIncrement,
                    currentVersion.Version.Minor + minorIncrement,
                    currentVersion.Version.Build);
            }
            else
            {
                // 2 segment version
                newVersion = new Version(
                    currentVersion.Version.Major + majorIncrement,
                    currentVersion.Version.Minor + minorIncrement);
            }
            
            return new SemanticVersion(newVersion, currentVersion.Prerelease, currentVersion.BuildMetadata);
        }
    }
}
