using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NerdBank.GitVersioning.Managed
{
    internal static class GitExtensions
    {
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

    }
}
