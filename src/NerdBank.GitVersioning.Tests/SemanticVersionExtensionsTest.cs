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
    /// <summary>
    /// Provides testcases (original version, incremented version) in all supported version formats for
    /// testing <see cref="SemanticVersionExtensions.Increment(SemanticVersion, ReleaseVersionIncrement)"/>
    /// </summary>
    public class TestDataAttribute : DataAttribute
    {            
        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            // prefix: none or "v"
            foreach (var prefix in new[] { "", "v" })
            // version has either 2, 3 or 4 components
            foreach (var precision in new[] { VersionPrecision.Minor, VersionPrecision.Build, VersionPrecision.Revision })
            // version can have a prerelease tag
            foreach (var prereleaseTag in new[] { "", "-pre", "-pre.{height}", "-{height}", "-{height}.build" }) 
            // version can have build metadata
            foreach (var buildMetadata in new[] { "", "+build", "+build.{height}", "+{height}", "+{height}.build" }) 
            // either major or minor version can be incremented
            foreach (var increment in new[] { ReleaseVersionIncrement.Major, ReleaseVersionIncrement.Minor }) 
            {
                var major = 1;
                var minor = 2;
                var build = 3;
                var revision = 4;

                var majorIncrement = increment == ReleaseVersionIncrement.Major ? 1 : 0;
                var minorIncrement = increment == ReleaseVersionIncrement.Minor ? 1 : 0;

                var current = $"{prefix}{major}.{minor}";
                var next = $"{prefix}{major + majorIncrement}.{minor + minorIncrement}";

                // append third and/or fourth version component
                switch (precision)
                {
                    case VersionPrecision.Build:
                        current += $".{build}";
                        next += $".{build}";
                        break;
                    case VersionPrecision.Revision:
                        current += $".{build}.{revision}";
                        next += $".{build}.{revision}";
                        break;
                }

                // append prerelease tag
                current += prereleaseTag;
                next += prereleaseTag;
                
                // append build metadata
                current += buildMetadata;
                next += buildMetadata;

                yield return new object[]
                {
                    current,
                    increment,
                    next
                };
            }
        }
    }

    [Theory]
    [TestData]
    public void IncrementVersion(string currentVersionString, ReleaseVersionIncrement increment, string expectedVersionString)
    {
        var currentVersion = SemanticVersion.Parse(currentVersionString);
        var expectedVersion = SemanticVersion.Parse(expectedVersionString);

        var actualVersion = currentVersion.Increment(increment);

        Assert.Equal(expectedVersion, actualVersion);
    }
}

