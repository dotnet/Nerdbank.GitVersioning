// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using Validation;

namespace Nerdbank.GitVersioning;

internal static class Utilities
{
    private const int SharingViolation = unchecked((int)0x80070020); // ERROR_SHARING_VIOLATION
    private const int AccessDenied = unchecked((int)0x80070005);   // ERROR_ACCESS_DENIED

    /// <summary>
    /// How long <see cref="FileOperationWithRetry(Action)" /> keeps retrying a transient failure before giving up.
    /// </summary>
    private const int RetryTimeout = 5000;

    /// <summary>
    /// The longest a single backoff may last, in milliseconds.
    /// </summary>
    private const int MaxBackoff = 50;

    private static readonly bool IsWindows =
#if NETFRAMEWORK
        true;
#else
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
#endif

    [ThreadStatic]
    private static Random random;

    /// <summary>
    /// Gets a pseudo-random number generator that belongs to the calling thread, since <see cref="Random" /> is not thread-safe.
    /// </summary>
    private static Random Random => random ??= new Random(Environment.CurrentManagedThreadId ^ Environment.TickCount);

    /// <summary>
    /// Invokes a file operation, retrying while it fails for a reason that another process or thread is expected to clear.
    /// </summary>
    /// <param name="operation">The operation to invoke. It must be safe to invoke more than once.</param>
    /// <remarks>
    /// Many instances of this library may run concurrently within one build, and several of them may contend over one file.
    /// Backoffs are randomized so that a large set of contenders does not keep colliding in lock step.
    /// </remarks>
    internal static void FileOperationWithRetry(Action operation)
    {
        Requires.NotNull(operation, nameof(operation));

        Stopwatch timer = Stopwatch.StartNew();
        int attempt = 0;
        while (true)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception ex) when (IsTransientFileAccessError(ex) && timer.ElapsedMilliseconds < RetryTimeout)
            {
                int ceiling = Math.Min(MaxBackoff, 1 << Math.Min(attempt, 30));
                Thread.Sleep(Random.Next(1, ceiling + 1));
                attempt++;
            }
        }
    }

    private static bool IsTransientFileAccessError(Exception ex) => ex switch
    {
        // Windows reports contention with well-known HRESULTs.
        IOException { HResult: SharingViolation } => true,
        UnauthorizedAccessException { HResult: AccessDenied } => true,

        // No amount of waiting resolves these, on any platform.
        FileNotFoundException or DirectoryNotFoundException or PathTooLongException => false,

        // Non-Windows platforms report contention over the advisory lock that .NET takes on behalf of a
        // restrictive FileShare as an IOException carrying a raw errno in HResult, and those values are not
        // portable (EAGAIN is 11 on Linux but 35 on macOS, for example), so classify by type instead.
        // Retrying an error that turns out not to be transient merely delays rethrowing it.
        // UnauthorizedAccessException is deliberately excluded here because on these platforms it means
        // EACCES or EPERM, which contention never causes.
        IOException => !IsWindows,

        _ => false,
    };
}
