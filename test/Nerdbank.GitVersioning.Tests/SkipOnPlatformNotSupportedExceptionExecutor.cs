// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <summary>
/// Skips a test that throws <see cref="PlatformNotSupportedException"/>.
/// </summary>
public class SkipOnPlatformNotSupportedExceptionExecutor : SkipOnExceptionExecutor<PlatformNotSupportedException>
{
}
