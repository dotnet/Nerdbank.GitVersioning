// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Cake.GitVersioning;
using Nerdbank.GitVersioning;
using Xunit;

/// <summary>
/// Tests to verify the <see cref="GitVersioningCloudProvider"/> enum (part of the Cake integration) is up-to-date.
/// </summary>
public class GitVersioningCloudProviderTests
{
    [Fact]
    public void HasExpectedValues()
    {
        IEnumerable<string> expectedValues = CloudBuild.SupportedCloudBuilds.Select(cb => cb.GetType().Name);
        string[] actualValues = Enum.GetNames(typeof(GitVersioningCloudProvider));

        IEnumerable<string> missingValues = expectedValues.Except(actualValues);
        Assert.True(
            !missingValues.Any(),
            $"Enumeration is missing the following values of supported cloud build providers: {string.Join(", ", missingValues)}");

        IEnumerable<string> redundantValues = actualValues.Except(expectedValues);
        Assert.True(
            !redundantValues.Any(),
            $"Enumeration contains values which were not found among supported cloud build providers: {string.Join(",", redundantValues)}");
    }
}
