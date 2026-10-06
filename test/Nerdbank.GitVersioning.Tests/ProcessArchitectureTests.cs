// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

/// <summary>
/// Guards the x86 CI test leg, which only has value if the tests really run in a 32-bit process.
/// </summary>
public class ProcessArchitectureTests
{
    [Test]
    public void TestProcessBitnessMatchesBuild()
    {
        string description = $"Is64BitProcess={Environment.Is64BitProcess}, ProcessArchitecture={RuntimeInformation.ProcessArchitecture}, IntPtr.Size={IntPtr.Size}, Framework={RuntimeInformation.FrameworkDescription}";
        Console.WriteLine(description);
#if TEST_X86
        Assert.False(Environment.Is64BitProcess, $"This test assembly was built for x86, but is running in a 64-bit process. {description}");
        Assert.Equal(4, IntPtr.Size);
#else
        Assert.Equal(Environment.Is64BitProcess, IntPtr.Size == 8);
#endif
    }
}
