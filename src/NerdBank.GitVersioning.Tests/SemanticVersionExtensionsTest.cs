using System.Collections.Generic;
using System.Reflection;
using Nerdbank.GitVersioning;
using Xunit;
using Xunit.Sdk;

using static Nerdbank.GitVersioning.VersionOptions;

/// <summary>
/// Tests for <see cref=""/>
/// </summary>
public class SemanticVersionExtensionsTest
{
    [Theory]
    [InlineData("1.0", ReleaseVersionIncrement.Minor, "1.1")]
    [InlineData("1.0", ReleaseVersionIncrement.Major, "2.0")]
    [InlineData("1.0-tag", ReleaseVersionIncrement.Minor, "1.1-tag")]
    [InlineData("1.0-tag", ReleaseVersionIncrement.Major, "2.0-tag")]
    [InlineData("1.0+metadata", ReleaseVersionIncrement.Minor, "1.1+metadata")]
    [InlineData("1.0+metadata", ReleaseVersionIncrement.Major, "2.0+metadata")]
    [InlineData("1.0-tag+metadata", ReleaseVersionIncrement.Minor, "1.1-tag+metadata")]
    [InlineData("1.0-tag+metadata", ReleaseVersionIncrement.Major, "2.0-tag+metadata")]
    [InlineData("1.2.3", ReleaseVersionIncrement.Minor, "1.3.3")]
    [InlineData("1.2.3", ReleaseVersionIncrement.Major, "2.2.3")]
    [InlineData("1.2.3.4", ReleaseVersionIncrement.Minor, "1.3.3.4")]
    [InlineData("1.2.3.4", ReleaseVersionIncrement.Major, "2.2.3.4")]
    public void IncrementVersion(string currentVersionString, ReleaseVersionIncrement increment, string expectedVersionString)
    {
        var currentVersion = SemanticVersion.Parse(currentVersionString);
        var expectedVersion = SemanticVersion.Parse(expectedVersionString);

        var actualVersion = currentVersion.Increment(increment);

        Assert.Equal(expectedVersion, actualVersion);
    }
}

