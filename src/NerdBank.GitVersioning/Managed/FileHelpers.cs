#nullable enable

using Microsoft.Win32.SafeHandles;
using System;
using System.Buffers;
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
            byte* filename,
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

        public static unsafe bool TryOpen(ReadOnlySpan<byte> path, CreateFileFlags attributes, out FileStream? stream)
        {
            if (IsWindows)
            {
                SafeFileHandle handle;

                fixed (byte* pathPtr = path)
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
                var fullPath = GetUtf16String(path.Slice(0, path.Length - 2));

                if (!File.Exists(fullPath))
                {
                    stream = null;
                    return false;
                }

                stream = File.OpenRead(fullPath);
                return true;
            }
        }

        private static string GetUtf16String(ReadOnlySpan<byte> bytes)
        {
#if NETSTANDARD
            byte[]? buffer = null;

            try
            {
                buffer = ArrayPool<byte>.Shared.Rent(bytes.Length);
                bytes.CopyTo(buffer);

                return Encoding.Unicode.GetString(buffer, 0, bytes.Length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
#else
            return Encoding.Unicode.GetString(bytes);
#endif
        }
    }
}
