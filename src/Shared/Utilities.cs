// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Validation;

namespace Nerdbank.GitVersioning;

internal static class Utilities
{
    private const int SharingViolation = unchecked((int)0x80070020); // ERROR_SHARING_VIOLATION
    private const int AccessDenied = unchecked((int)0x80070005);   // ERROR_ACCESS_DENIED

    internal static void FileOperationWithRetry(Action operation)
    {
        Requires.NotNull(operation, nameof(operation));

        for (int retriesLeft = 6; retriesLeft > 0; retriesLeft--)
        {
            try
            {
                operation();
                break;
            }
            catch (Exception ex) when (IsTransientFileAccessError(ex) && retriesLeft > 1)
            {
                Thread.Sleep(100);
                continue;
            }
        }
    }

    private static bool IsTransientFileAccessError(Exception ex) => ex is
        IOException { HResult: SharingViolation } or
        UnauthorizedAccessException { HResult: AccessDenied };
}
