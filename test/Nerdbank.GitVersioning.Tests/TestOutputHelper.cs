// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <summary>
/// Writes to the output of whichever TUnit test is currently running.
/// </summary>
internal sealed class TestOutputHelper : ITestOutputHelper
{
    internal static readonly TestOutputHelper Instance = new();

    private TestOutputHelper()
    {
    }

    public void WriteLine(string message) => TestContext.Current?.Output.WriteLine(message);

    public void WriteLine(string format, params object[] args) => this.WriteLine(string.Format(System.Globalization.CultureInfo.CurrentCulture, format, args));
}
