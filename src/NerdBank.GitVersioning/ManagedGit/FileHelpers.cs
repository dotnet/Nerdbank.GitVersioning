// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace Nerdbank.GitVersioning.ManagedGit;

internal static class FileHelpers
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Opens the file with a given path, if it exists.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <param name="stream">The stream to open to, if the file exists.</param>
    /// <returns><see langword="true" /> if the file exists; otherwise <see langword="false" />.</returns>
    internal static bool TryOpen(string path, out FileStream? stream)
    {
#if NET5_0_OR_GREATER
        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
#else
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
#endif
        {
            SafeFileHandle? handle = PInvoke.CreateFile(path, (uint)FILE_ACCESS_RIGHTS.FILE_GENERIC_READ, FILE_SHARE_MODE.FILE_SHARE_READ, lpSecurityAttributes: null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL, null);

            if (!handle.IsInvalid)
            {
                var fileHandle = new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: true);
                handle.SetHandleAsInvalid();
                stream = new FileStream(fileHandle, System.IO.FileAccess.Read);
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
    /// <param name="stream">The stream to open to, if the file exists.</param>
    /// <returns><see langword="true" /> if the file exists; otherwise <see langword="false" />.</returns>
    internal static unsafe bool TryOpen(ReadOnlySpan<char> path, [NotNullWhen(true)] out FileStream? stream)
    {
#if NET5_0_OR_GREATER
        if (OperatingSystem.IsWindowsVersionAtLeast(5, 1, 2600))
#else
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
#endif
        {
            HANDLE handle;
            fixed (char* pPath = &path[0])
            {
                handle = PInvoke.CreateFile(pPath, (uint)FILE_ACCESS_RIGHTS.FILE_GENERIC_READ, FILE_SHARE_MODE.FILE_SHARE_READ, null, FILE_CREATION_DISPOSITION.OPEN_EXISTING, FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL, default);
            }

            if (!handle.Equals(HANDLE.INVALID_HANDLE_VALUE))
            {
                var fileHandle = new SafeFileHandle(handle, ownsHandle: true);
                stream = new FileStream(fileHandle, System.IO.FileAccess.Read);
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
