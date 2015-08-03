using Validation;
namespace NerdBank.GitVersioning.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

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
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                var process = Process.Start(psi);
                process.WaitForExit();
            }
        }
    }
}
