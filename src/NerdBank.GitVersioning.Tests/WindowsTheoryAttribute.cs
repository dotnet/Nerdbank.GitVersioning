using System;
using System.Runtime.InteropServices;
using Xunit;

public class WindowsTheoryAttribute : TheoryAttribute
{
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
