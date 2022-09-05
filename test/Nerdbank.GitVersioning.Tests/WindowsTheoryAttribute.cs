// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using Xunit;

public class WindowsTheoryAttribute : TheoryAttribute
{
    /// <inheritdoc/>
    public override string Skip
    {
        get
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "This test runs on Windows only";
            }

            return null;
        }

        set
        {
            throw new NotSupportedException();
        }
    }
}
