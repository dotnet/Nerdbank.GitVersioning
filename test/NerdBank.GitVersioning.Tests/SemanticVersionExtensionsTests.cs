// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Nerdbank.GitVersioning;
using Xunit;
using Xunit.Sdk;

using static Nerdbank.GitVersioning.VersionOptions;

/// <summary>
/// Tests for <see cref="SemanticVersionExtensions"/>.
/// </summary>
public class SemanticVersionExtensionsTests
{
    [Theory]
    [InlineData("1.0", ReleaseVersionIncrement.Minor, "1.1")]
    [InlineData("1.1", ReleaseVersionIncrement.Minor, "1.2")]
    [InlineData("1.0", ReleaseVersionIncrement.Major, "2.0")]
    [InlineData("1.1", ReleaseVersionIncrement.Major, "2.0")]
    [InlineData("1.0-tag", ReleaseVersionIncrement.Minor, "1.1-tag")]
    [InlineData("1.0-tag", ReleaseVersionIncrement.Major, "2.0-tag")]
    [InlineData("1.0+metadata", ReleaseVersionIncrement.Minor, "1.1+metadata")]
    [InlineData("1.0+metadata", ReleaseVersionIncrement.Major, "2.0+metadata")]
    [InlineData("1.0-tag+metadata", ReleaseVersionIncrement.Minor, "1.1-tag+metadata")]
    [InlineData("1.0-tag+metadata", ReleaseVersionIncrement.Major, "2.0-tag+metadata")]
    [InlineData("1.2.3", ReleaseVersionIncrement.Minor, "1.3.0")]
    [InlineData("1.2.3", ReleaseVersionIncrement.Major, "2.0.0")]
    [InlineData("1.2.3.4", ReleaseVersionIncrement.Minor, "1.3.0.0")]
    [InlineData("1.2.3.4", ReleaseVersionIncrement.Major, "2.0.0.0")]
    [InlineData("1.2.3", ReleaseVersionIncrement.Build, "1.2.4")]
    [InlineData("1.2.3.4", ReleaseVersionIncrement.Build, "1.2.4.0")]
    [InlineData("1.2.3-tag", ReleaseVersionIncrement.Build, "1.2.4-tag")]
    [InlineData("1.2.3-tag+metadata", ReleaseVersionIncrement.Build, "1.2.4-tag+metadata")]
    [InlineData("1.2.3.4-tag", ReleaseVersionIncrement.Build, "1.2.4.0-tag")]
    [InlineData("1.2.3.4-tag+metadata", ReleaseVersionIncrement.Build, "1.2.4.0-tag+metadata")]
    public void Increment(string currentVersionString, ReleaseVersionIncrement increment, string expectedVersionString)
    {
        var currentVersion = SemanticVersion.Parse(currentVersionString);
        var expectedVersion = SemanticVersion.Parse(expectedVersionString);

        SemanticVersion actualVersion = currentVersion.Increment(increment);

        Assert.Equal(expectedVersion, actualVersion);
    }

    [Theory]
    [InlineData("1.0", ReleaseVersionIncrement.Build)]
    public void Increment_InvalidIncrement(string currentVersionString, ReleaseVersionIncrement increment)
    {
        var currentVersion = SemanticVersion.Parse(currentVersionString);

        Assert.Throws<ArgumentException>(() => currentVersion.Increment(increment));
    }

    [Theory]
    // no prerelease tag in input version
    [InlineData("1.2", "pre", "1.2-pre")]
    [InlineData("1.2", "-pre", "1.2-pre")]
    [InlineData("1.2+build", "pre", "1.2-pre+build")]
    [InlineData("1.2.3", "pre", "1.2.3-pre")]
    [InlineData("1.2.3+build", "pre", "1.2.3-pre+build")]
    // single prerelease tag in input version
    [InlineData("1.2-alpha", "beta", "1.2-beta")]
    [InlineData("1.2-alpha", "-beta", "1.2-beta")]
    [InlineData("1.2.3-alpha", "beta", "1.2.3-beta")]
    [InlineData("1.2-alpha+metadata", "-beta", "1.2-beta+metadata")]
    // multiple prerelease tags
    [InlineData("1.2-alpha.preview", "beta", "1.2-beta.preview")]
    [InlineData("1.2-alpha.preview", "-beta", "1.2-beta.preview")]
    [InlineData("1.2-alpha.preview+metadata", "beta", "1.2-beta.preview+metadata")]
    [InlineData("1.2.3-alpha.preview", "beta", "1.2.3-beta.preview")]
    [InlineData("1.2-alpha.{height}", "beta", "1.2-beta.{height}")]
    // remove tag
    [InlineData("1.2-pre", "", "1.2")]
    public void SetFirstPrereleaseTag(string currentVersionString, string newTag, string expectedVersionString)
    {
        var currentVersion = SemanticVersion.Parse(currentVersionString);
        var expectedVersion = SemanticVersion.Parse(expectedVersionString);

        SemanticVersion actualVersion = currentVersion.SetFirstPrereleaseTag(newTag);

        Assert.Equal(expectedVersion, actualVersion);
    }
}
