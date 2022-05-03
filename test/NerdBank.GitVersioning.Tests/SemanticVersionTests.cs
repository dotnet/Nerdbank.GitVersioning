// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nerdbank.GitVersioning;
using Xunit;

public class SemanticVersionTests
{
    [Fact]
    public void Ctor_Version()
    {
        var sv = new SemanticVersion(new Version(1, 2), "-pre", "+mybuild");
        Assert.Equal(new Version(1, 2), sv.Version);
        Assert.Equal("-pre", sv.Prerelease);
        Assert.Equal("+mybuild", sv.BuildMetadata);
    }

    [Fact]
    public void Ctor_String()
    {
        var sv = new SemanticVersion("1.2", "-pre", "+mybuild");
        Assert.Equal(new Version(1, 2), sv.Version);
        Assert.Equal("-pre", sv.Prerelease);
        Assert.Equal("+mybuild", sv.BuildMetadata);
    }

    [Fact]
    public void Ctor_ValidatesInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new SemanticVersion((Version)null));
        Assert.Throws<ArgumentNullException>(() => new SemanticVersion((string)null));
    }

    [Fact]
    public void Ctor_NormalizesNullPreleaseAndBuildMetadata()
    {
        var sv = new SemanticVersion(new Version(1, 2));
        Assert.Equal(string.Empty, sv.Prerelease);
        Assert.Equal(string.Empty, sv.BuildMetadata);
    }

    [Fact]
    public void TryParse()
    {
        SemanticVersion result;
        Assert.True(SemanticVersion.TryParse("1.2-pre.5.8-foo+build-metadata.id1.2", out result));
        Assert.Equal(1, result.Version.Major);
        Assert.Equal(2, result.Version.Minor);
        Assert.Equal("-pre.5.8-foo", result.Prerelease);
        Assert.Equal("+build-metadata.id1.2", result.BuildMetadata);
    }

    [Fact]
    public void PrereleaseIdentifiers_InvalidCases()
    {
        SemanticVersion result;
        Assert.False(SemanticVersion.TryParse("1.2-$", out result));
        Assert.False(SemanticVersion.TryParse("1.2-", out result));
        Assert.False(SemanticVersion.TryParse("1.2-a.", out result));
        Assert.Null(result);
    }

    [Fact]
    public void BuildMetadata_InvalidCases()
    {
        SemanticVersion result;
        Assert.False(SemanticVersion.TryParse("1.2+$", out result));
        Assert.False(SemanticVersion.TryParse("1.2+", out result));
        Assert.False(SemanticVersion.TryParse("1.2+a.", out result));
        Assert.Null(result);
    }

    [Fact]
    public void TryParse_WithPatch()
    {
        SemanticVersion result;
        Assert.True(SemanticVersion.TryParse("1.2.3-pre+build", out result));
        Assert.Equal(1, result.Version.Major);
        Assert.Equal(2, result.Version.Minor);
        Assert.Equal(3, result.Version.Build);
        Assert.Equal("-pre", result.Prerelease);
        Assert.Equal("+build", result.BuildMetadata);
    }

    [Fact]
    public void TryParse_WithRevision()
    {
        SemanticVersion result;
        Assert.True(SemanticVersion.TryParse("1.2.3.4-pre+build", out result));
        Assert.Equal(1, result.Version.Major);
        Assert.Equal(2, result.Version.Minor);
        Assert.Equal(3, result.Version.Build);
        Assert.Equal(4, result.Version.Revision);
        Assert.Equal("-pre", result.Prerelease);
        Assert.Equal("+build", result.BuildMetadata);
    }

    [Fact]
    public void Parse()
    {
        SemanticVersion result = SemanticVersion.Parse("1.2-pre+build");
        Assert.Equal(1, result.Version.Major);
        Assert.Equal(2, result.Version.Minor);
        Assert.Equal("-pre", result.Prerelease);
        Assert.Equal("+build", result.BuildMetadata);

        Assert.Throws<ArgumentException>(() => SemanticVersion.Parse("1.2-$"));
    }

    [Fact]
    public void Equality()
    {
        var sv12a = new SemanticVersion(new Version(1, 2), null);
        var sv12b = new SemanticVersion(new Version(1, 2), null);
        Assert.Equal(sv12a, sv12b);

        var sv13 = new SemanticVersion(new Version(1, 3), null);
        Assert.NotEqual(sv12a, sv13);

        var sv12Pre = new SemanticVersion(new Version(1, 2), "-pre");
        var sv12Beta = new SemanticVersion(new Version(1, 2), "-beta");
        Assert.NotEqual(sv12a, sv12Pre);
        Assert.NotEqual(sv12Pre, sv12Beta);
        Assert.Equal(sv12Pre, sv12Pre);

        var sv12BuildInfo = new SemanticVersion(new Version(1, 2), buildMetadata: "+buildInfo");
        var sv12OtherBuildInfo = new SemanticVersion(new Version(1, 2), buildMetadata: "+otherBuildInfo");
        Assert.NotEqual(sv12BuildInfo, sv12OtherBuildInfo);
        Assert.Equal(sv12BuildInfo, sv12BuildInfo);

        Assert.False(sv12a.Equals(null));
    }

    [Fact]
    public void HashCodes()
    {
        var sv12a = new SemanticVersion(new Version(1, 2), null);
        var sv12b = new SemanticVersion(new Version(1, 2), null);
        Assert.Equal(sv12a.GetHashCode(), sv12b.GetHashCode());

        var sv13 = new SemanticVersion(new Version(1, 3), null);
        Assert.NotEqual(sv12a.GetHashCode(), sv13.GetHashCode());
    }

    [Fact]
    public void ToStringTests()
    {
        var v = new SemanticVersion(new Version(1, 2), null);
        Assert.Equal("1.2", v.ToString());
        var vp = new SemanticVersion(new Version(1, 2), "-pre");
        Assert.Equal("1.2-pre", vp.ToString());
        var vb = new SemanticVersion(new Version(1, 2), buildMetadata: "+buildInfo");
        Assert.Equal("1.2+buildInfo", vb.ToString());
        var vpb = new SemanticVersion(new Version(1, 2), "-pre", "+buildInfo");
        Assert.Equal("1.2-pre+buildInfo", vpb.ToString());
    }
}
