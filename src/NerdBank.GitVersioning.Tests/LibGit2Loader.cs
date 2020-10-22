#if NETCOREAPP
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using RuntimeEnvironment = Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment;

public static class LibGit2Loader
{
    public static string RuntimePath = "./runtimes";

    static LibGit2Loader()
    {
        NativeLibrary.SetDllImportResolver(typeof(Repository).Assembly, ImportResolver);
    }

    public static void EnsureRegistered()
    {
        // No-op, only here to ensure the static constructor is triggered
    }

    private static IntPtr ImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        IntPtr handle = IntPtr.Zero;

        if (libraryName.StartsWith("git2-", StringComparison.Ordinal) ||
            libraryName.StartsWith("libgit2-", StringComparison.Ordinal))
        {
            var directory = GetNativeLibraryDirectory();
            var extension = GetNativeLibraryExtension();

            if (!libraryName.EndsWith(extension, StringComparison.Ordinal))
            {
                libraryName += extension;
            }

            var nativeLibraryPath = Path.Combine(directory, libraryName);
            if (!File.Exists(nativeLibraryPath))
            {
                nativeLibraryPath = Path.Combine(directory, "lib" + libraryName);
            }

            if (!NativeLibrary.TryLoad(nativeLibraryPath, assembly, DllImportSearchPath.System32, out handle))
            {
                throw new NotImplementedException($"No support for loading {libraryName} at {nativeLibraryPath}");
            }
        }

        return handle;
    }

    private static string GetNativeLibraryDirectory()
    {
        var dir = Path.GetDirectoryName(typeof(Repository).Assembly.Location);
        return Path.Combine(dir, RuntimePath, RuntimeIdMap.GetNativeLibraryDirectoryName(RuntimeEnvironment.GetRuntimeIdentifier()), "native");
    }

    private static string GetNativeLibraryExtension()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ".dll";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return ".dylib";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return ".so";
        }

        throw new PlatformNotSupportedException();
    }
}
#endif