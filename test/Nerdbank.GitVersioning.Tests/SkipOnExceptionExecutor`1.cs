// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using TUnit.Core.Interfaces;

/// <summary>
/// Reports a test as skipped instead of failed when it throws <typeparamref name="TException"/>,
/// like xunit's <c>SkipExceptions</c>.
/// </summary>
/// <typeparam name="TException">The type of exception that indicates the test cannot run here.</typeparam>
public abstract class SkipOnExceptionExecutor<TException> : ITestExecutor
    where TException : Exception
{
    /// <inheritdoc/>
    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            Skip.Test($"{typeof(TException).Name}: {ex.Message}");
        }
    }
}
