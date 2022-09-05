// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nerdbank.GitVersioning;
using Xunit;

public class VersionExtensionsTests
{
    [Fact]
    public void EnsureNonNegativeComponents_NoValues()
    {
        Version version = new Version().EnsureNonNegativeComponents();
        Assert.Equal(0, version.Major);
        Assert.Equal(0, version.Minor);
        Assert.Equal(0, version.Build);
        Assert.Equal(0, version.Revision);
    }

    [Fact]
    public void EnsureNonNegativeComponents_2Values()
    {
        Version version = new Version(1, 2).EnsureNonNegativeComponents();
        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(0, version.Build);
        Assert.Equal(0, version.Revision);
    }

    [Fact]
    public void EnsureNonNegativeComponents_3Values()
    {
        Version version = new Version(1, 2, 3).EnsureNonNegativeComponents();
        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Build);
        Assert.Equal(0, version.Revision);
    }

    [Fact]
    public void EnsureNonNegativeComponents_4Values()
    {
        var original = new Version(1, 2, 3, 4);
        Version version = original.EnsureNonNegativeComponents();
        Assert.Same(original, version);
    }

    [Fact]
    public void ToStringSafe()
    {
        Assert.Equal("1.2.3.4", new Version(1, 2, 3, 4).ToStringSafe(4));
        Assert.Equal("1.2.3.0", new Version(1, 2, 3).ToStringSafe(4));
        Assert.Equal("1.2.0.0", new Version(1, 2).ToStringSafe(4));

        Assert.Equal("1.2.3", new Version(1, 2, 3, 4).ToStringSafe(3));
        Assert.Equal("1.2.3", new Version(1, 2, 3).ToStringSafe(3));
        Assert.Equal("1.2.0", new Version(1, 2).ToStringSafe(3));

        Assert.Equal("1.2", new Version(1, 2, 3, 4).ToStringSafe(2));
        Assert.Equal("1.2", new Version(1, 2, 3).ToStringSafe(2));
        Assert.Equal("1.2", new Version(1, 2).ToStringSafe(2));

        Assert.Equal("1", new Version(1, 2).ToStringSafe(1));

        Assert.Equal(string.Empty, new Version(1, 2).ToStringSafe(0));
    }
}
