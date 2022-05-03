// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Validation;

/// <summary>
/// Test utility methods.
/// </summary>
internal static class TestUtilities
{
    /// <summary>
    /// Recursively delete a directory, even after a git commit has been authored within it.
    /// </summary>
    /// <param name="path">The path to delete.</param>
    internal static void DeleteDirectory(string path)
    {
        Requires.NotNullOrEmpty(path, nameof(path));
        Requires.Argument(Path.IsPathRooted(path), nameof(path), "Must be rooted.");

        try
        {
            Directory.Delete(path, true);
        }
        catch (UnauthorizedAccessException)
        {
            // Unknown why this fails so often.
            // Somehow making commits with libgit2sharp locks files
            // such that we can't delete them (but Windows Explorer can).
            var psi = new ProcessStartInfo("cmd.exe", $"/c rd /s /q \"{path}\"");
            psi.WorkingDirectory = Path.GetTempPath();
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            var process = Process.Start(psi);
            process.WaitForExit();
        }
    }

    internal static Stream GetEmbeddedResource(string resourcePath)
    {
        Requires.NotNullOrEmpty(resourcePath, nameof(resourcePath));

        return Assembly.GetExecutingAssembly().GetManifestResourceStream($"{ThisAssembly.RootNamespace}.{resourcePath.Replace('\\', '.')}");
    }

    internal static void ExtractEmbeddedResource(string resourcePath, string extractedFilePath)
    {
        Requires.NotNullOrEmpty(resourcePath, nameof(resourcePath));
        Requires.NotNullOrEmpty(extractedFilePath, nameof(extractedFilePath));

        using (Stream stream = GetEmbeddedResource(resourcePath))
        {
            Requires.Argument(stream is not null, nameof(resourcePath), "Resource not found.");
            using (FileStream extractedFile = File.OpenWrite(extractedFilePath))
            {
                stream.CopyTo(extractedFile);
            }
        }
    }

    internal static ExpandedRepo ExtractRepoArchive(string repoArchiveName)
    {
        string archiveFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string expandedFolderPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        ExtractEmbeddedResource($"repos.{repoArchiveName}.zip", archiveFilePath);
        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(archiveFilePath, expandedFolderPath);
            return new ExpandedRepo(expandedFolderPath);
        }
        finally
        {
            File.Delete(archiveFilePath);
        }
    }

    internal static string ToHex(ushort number) => number.ToString("X");

    internal static ushort FromHex(string hex) => ushort.Parse(hex, System.Globalization.NumberStyles.HexNumber);

    internal class ExpandedRepo : IDisposable
    {
        internal ExpandedRepo(string repoPath)
        {
            Requires.NotNullOrEmpty(repoPath, nameof(repoPath));
            this.RepoPath = repoPath;
        }

        public string RepoPath { get; private set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Directory.Exists(this.RepoPath))
            {
                DeleteDirectory(this.RepoPath);
            }
        }
    }
}
