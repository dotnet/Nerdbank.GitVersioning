// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using Xunit;

public class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            this.Skip = "This test runs on Windows only";
        }
    }
}
