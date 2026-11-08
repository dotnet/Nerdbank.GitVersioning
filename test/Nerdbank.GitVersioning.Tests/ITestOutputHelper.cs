// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// <summary>
/// A minimal stand-in for xunit's <c>ITestOutputHelper</c> so these tests can keep logging after moving to TUnit.
/// </summary>
public interface ITestOutputHelper
{
    void WriteLine(string message);

    void WriteLine(string format, params object[] args);
}
