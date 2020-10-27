#nullable enable

using Microsoft.Win32.SafeHandles;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NerdBank.GitVersioning.Managed
{
    internal static class FileHelpers
    {
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false, ExactSpelling = true)]
        public static unsafe extern SafeFileHandle CreateFile(
            string filename,
            FileAccess access,
            FileShare share,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            CreateFileFlags flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false, ExactSpelling = true)]
        public static unsafe extern SafeFileHandle CreateFile(
            char* filename,
            FileAccess access,
            FileShare share,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            CreateFileFlags flagsAndAttributes,
            IntPtr templateFile);

        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static bool TryOpen(string path, CreateFileFlags attributes, out FileStream? stream)
        {
            if (IsWindows)
            {
                var handle = CreateFile(path, FileAccess.Read, FileShare.Read, IntPtr.Zero, FileMode.Open, attributes, IntPtr.Zero);

                if (!handle.IsInvalid)
                {
                    stream = new FileStream(handle, FileAccess.Read);
                    return true;
                }
                else
                {
                    stream = null;
                    return false;
                }
            }
            else
            {
                if (!File.Exists(path))
                {
                    stream = null;
                    return false;
                }

                stream = File.OpenRead(path);
                return true;
            }
        }

        /// <summary>
        /// Opens the file with a given path, if it exists.
        /// </summary>
        /// <param name="path">The path to the file, as a null-terminated UTF-16 character array.</param>
        /// <param name="attributes">Attributes to open the file with, when running on Windows.</param>
        /// <param name="stream">The stream to open to, if the file exists.</param>
        /// <returns><see langword="true" /> if the file exists; otherwise <see langword="false" />.</returns>
        public static unsafe bool TryOpen(ReadOnlySpan<char> path, CreateFileFlags attributes, [NotNullWhen(true)] out FileStream? stream)
        {
            if (IsWindows)
            {
                SafeFileHandle handle;

                fixed (char* pathPtr = path)
                {
                    handle = CreateFile(pathPtr, FileAccess.Read, FileShare.Read, IntPtr.Zero, FileMode.Open, attributes, IntPtr.Zero);
                }

                if (!handle.IsInvalid)
                {
                    stream = new FileStream(handle, FileAccess.Read);
                    return true;
                }
                else
                {
                    stream = null;
                    return false;
                }
            }
            else
            {
                // Make sure to trim the trailing \0
                string fullPath = GetUtf16String(path.Slice(0, path.Length - 1));

                if (!File.Exists(fullPath))
                {
                    stream = null;
                    return false;
                }

                stream = File.OpenRead(fullPath);
                return true;
            }
        }

        private static unsafe string GetUtf16String(ReadOnlySpan<char> chars)
        {
            fixed (char* pChars = chars)
            {
                return new string(pChars, 0, chars.Length);
            }
        }
    }
}
