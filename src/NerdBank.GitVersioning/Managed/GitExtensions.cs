using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using Nerdbank.GitVersioning;
using Validation;

namespace NerdBank.GitVersioning.Managed
{
    internal static class GitExtensions
    {
        /// <summary>
        /// The 0.0 version.
        /// </summary>
        private static readonly Version Version0 = new Version(0, 0);

        /// <summary>
        /// The 0.0 semver.
        /// </summary>
        private static readonly SemanticVersion SemVer0 = SemanticVersion.Parse("0.0");

        /// <summary>
        /// Maximum allowable value for the <see cref="Version.Build"/>
        /// and <see cref="Version.Revision"/> components.
        /// </summary>
        private const ushort MaximumBuildNumberOrRevisionComponent = 0xfffe;

        internal static string GetRepoRelativePath(this GitRepository repo, string absolutePath)
        {
            var repoRoot = repo.RootDirectory/* repo?.Info?.WorkingDirectory */?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && repoRoot != null && repoRoot.StartsWith("\\") && (repoRoot.Length == 1 || repoRoot[1] != '\\'))
            {
                // We're in a worktree, which libgit2sharp only gives us as a path relative to the root of the assumed drive.
                // Add the drive: to the front of the repoRoot.
                // repoRoot = repo.Info.Path.Substring(0, 2) + repoRoot;
            }

            if (repoRoot == null)
                return null;

            if (!absolutePath.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Path '{absolutePath}' is not within repository '{repoRoot}'", nameof(absolutePath));
            }

            return absolutePath.Substring(repoRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// Encodes a commit from history in a <see cref="Version"/>
        /// so that the original commit can be found later.
        /// </summary>
        /// <param name="commit">The commit whose ID and position in history is to be encoded.</param>
        /// <param name="versionOptions">The version options applicable at this point (either from commit or working copy).</param>
        /// <param name="versionHeight">The version height, previously calculated by a call to <see cref="GetVersionHeight(Commit, string, Version)"/>.</param>
        /// <returns>
        /// A version whose <see cref="Version.Build"/> and
        /// <see cref="Version.Revision"/> components are calculated based on the commit.
        /// </returns>
        /// <remarks>
        /// In the returned version, the <see cref="Version.Build"/> component is
        /// the height of the git commit while the <see cref="Version.Revision"/>
        /// component is the first four bytes of the git commit id (forced to be a positive integer).
        /// </remarks>
        internal static Version GetIdAsVersionHelper(this GitCommit? commit, VersionOptions versionOptions, int versionHeight)
        {
            var baseVersion = versionOptions?.Version?.Version ?? Version0;
            int buildNumber = baseVersion.Build;
            int revision = baseVersion.Revision;

            // Don't use the ?? coalescing operator here because the position property getters themselves can return null, which should NOT be overridden with our default.
            // The default value is only appropriate if versionOptions itself is null.
            var versionHeightPosition = versionOptions != null ? versionOptions.VersionHeightPosition : SemanticVersion.Position.Build;
            var commitIdPosition = versionOptions != null ? versionOptions.GitCommitIdPosition : SemanticVersion.Position.Revision;

            // The compiler (due to WinPE header requirements) only allows 16-bit version components,
            // and forbids 0xffff as a value.
            if (versionHeightPosition.HasValue)
            {
                int adjustedVersionHeight = versionHeight == 0 ? 0 : versionHeight + (versionOptions?.VersionHeightOffset ?? 0);
                Verify.Operation(adjustedVersionHeight <= MaximumBuildNumberOrRevisionComponent, "Git height is {0}, which is greater than the maximum allowed {0}.", adjustedVersionHeight, MaximumBuildNumberOrRevisionComponent);
                switch (versionHeightPosition.Value)
                {
                    case SemanticVersion.Position.Build:
                        buildNumber = adjustedVersionHeight;
                        break;
                    case SemanticVersion.Position.Revision:
                        revision = adjustedVersionHeight;
                        break;
                }
            }

            if (commitIdPosition.HasValue)
            {
                switch (commitIdPosition.Value)
                {
                    case SemanticVersion.Position.Revision:
                        revision = commit != null
                            ? Math.Min(MaximumBuildNumberOrRevisionComponent, commit.Value.GetTruncatedCommitIdAsUInt16())
                            : 0;
                        break;
                }
            }

            return VersionExtensions.Create(baseVersion.Major, baseVersion.Minor, buildNumber, revision);
        }

        /// <summary>
        /// Takes the first 2 bytes of a commit ID (i.e. first 4 characters of its hex-encoded SHA)
        /// and returns them as an 16-bit unsigned integer.
        /// </summary>
        /// <param name="commit">The commit to identify with an integer.</param>
        /// <returns>The unsigned integer which identifies a commit.</returns>
        public static ushort GetTruncatedCommitIdAsUInt16(this GitCommit commit)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(commit.Sha.AsSpan().Slice(0, 2));
        }
    }
}
