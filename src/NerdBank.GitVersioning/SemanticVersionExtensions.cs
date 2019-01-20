namespace Nerdbank.GitVersioning
{
    using System;

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
            // depending on whether the current versions has 2, 3 or 4 segments
            Version newVersion;
            if (currentVersion.Version.Build >= 0 && currentVersion.Version.Revision > 0)
            {
                // 4 segment version
                newVersion = new Version(
                    currentVersion.Version.Major + majorIncrement,
                    currentVersion.Version.Minor + minorIncrement,
                    currentVersion.Version.Build,
                    currentVersion.Version.Revision);
            }
            else if (currentVersion.Version.Build >= 0)
            {
                // 3 segment version
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
        
        /// <summary>
        /// Sets the first prerelease tag of the specified semantic version to the specified value.
        /// </summary>
        /// <param name="version">The version which's prerelease tag to modify.</param>
        /// <param name="newFirstTag">The new prerelease tag.</param>
        /// <returns>Returns a new instance of <see cref="SemanticVersion"/> with the updated prerelease tag</returns>
        public static SemanticVersion SetFirstPrereleaseTag(this SemanticVersion version, string newFirstTag)
        {
            newFirstTag = newFirstTag ?? "";

            string preRelease;
            if(string.IsNullOrEmpty(version.Prerelease))
            {
                preRelease = newFirstTag; 
            }
            else if(version.Prerelease.Contains("."))
            {
                preRelease = newFirstTag + version.Prerelease.Substring(version.Prerelease.IndexOf("."));
            }
            else
            {
                preRelease = newFirstTag;
            }        

            if (!string.IsNullOrEmpty(preRelease) && !preRelease.StartsWith("-"))
                preRelease = "-" + preRelease;

            return new SemanticVersion(version.Version, preRelease, version.BuildMetadata);            
        }

        /// <summary>
        /// Removes all prerelease tags from the semantic version.
        /// </summary>
        /// <param name="version">The version to remove the prerelease tags from.</param>
        /// <returns>Returns a new instance <see cref="SemanticVersion"/> which does not contain any prerelease tags.</returns>
        public static SemanticVersion WithoutPrepreleaseTags(this SemanticVersion version)
        {
            return new SemanticVersion(version.Version, null, version.BuildMetadata);
        }
    }
}
