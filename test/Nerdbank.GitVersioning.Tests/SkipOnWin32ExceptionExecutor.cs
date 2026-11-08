// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.ComponentModel;

/// <summary>
/// Skips a test that throws <see cref="Win32Exception"/>.
/// </summary>
public class SkipOnWin32ExceptionExecutor : SkipOnExceptionExecutor<Win32Exception>
{
}
