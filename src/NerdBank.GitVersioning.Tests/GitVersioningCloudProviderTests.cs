using System;
using System.Linq;
using Cake.GitVersioning;
using Nerdbank.GitVersioning;
using Xunit;

/// <summary>
/// Tests to verify the <see cref="GitVersioningCloudProvider"/> enum (part of the Cake integration) is up-to-date
/// </summary>
public class GitVersioningCloudProviderTests
{
    [Fact]
    public void HasExpectedValues()
    {
        var expectedValues = CloudBuild.SupportedCloudBuilds.Select(cb => cb.GetType().Name);
        var actualValues = Enum.GetNames(typeof(GitVersioningCloudProvider));

        var missingValues = expectedValues.Except(actualValues);
        Assert.True(
            !missingValues.Any(),
            $"Enumeration is missing the following values of supported cloud build providers: {string.Join(", ", missingValues)}"
        );

        var redundantValues = actualValues.Except(expectedValues);
        Assert.True(
            !redundantValues.Any(),
            $"Enumeration contains values which were not found among supported cloud build providers: {string.Join(",", redundantValues)}"
        );
    }
}
