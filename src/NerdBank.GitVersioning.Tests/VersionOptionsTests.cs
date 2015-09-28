using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nerdbank.GitVersioning;
using Xunit;

public class VersionOptionsTests
{
    [Fact]
    public void FromVersion()
    {
        var vo = VersionOptions.FromVersion(new Version(1, 2), "-pre");
        Assert.Equal(new Version(1, 2), vo.Version.Version);
        Assert.Equal("-pre", vo.Version.Prerelease);
        Assert.Null(vo.AssemblyVersion);
        Assert.Equal(0, vo.BuildNumberOffset);
    }

    [Fact]
    public void Equality()
    {
        var vo1a = new VersionOptions
        {
            Version = new SemanticVersion("1.2"),
            AssemblyVersion = new Version("1.3"),
            BuildNumberOffset = 2,
        };
        var vo1b = new VersionOptions
        {
            Version = new SemanticVersion("1.2"),
            AssemblyVersion = new Version("1.3"),
            BuildNumberOffset = 2,
        };

        var vo2VaryAV = new VersionOptions
        {
            Version = new SemanticVersion("1.2"),
            AssemblyVersion = new Version("1.4"),
        };
        var vo2VaryV = new VersionOptions
        {
            Version = new SemanticVersion("1.4"),
            AssemblyVersion = new Version("1.3"),
        };
        var vo2VaryO = new VersionOptions
        {
            Version = new SemanticVersion("1.2"),
            AssemblyVersion = new Version("1.3"),
            BuildNumberOffset = 3,
        };

        Assert.Equal(vo1a, vo1b);
        Assert.NotEqual(vo2VaryAV, vo1a);
        Assert.NotEqual(vo2VaryV, vo1a);
        Assert.NotEqual(vo2VaryO, vo1a);
    }
}
