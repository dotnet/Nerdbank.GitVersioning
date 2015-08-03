using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NerdBank.GitVersioning;
using Xunit;

public class SemanticVersionTests
{
    [Fact]
    public void Ctor()
    {
        var sv = new SemanticVersion(new Version(1, 2), "-pre");
        Assert.Equal(new Version(1, 2), sv.Version);
        Assert.Equal("-pre", sv.UnstableTag);
    }

    [Fact]
    public void Ctor_ValidatesInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new SemanticVersion(null));
    }

    [Fact]
    public void Ctor_NormalizesNullUnstableTag()
    {
        var sv = new SemanticVersion(new Version(1, 2), null);
        Assert.Equal(string.Empty, sv.UnstableTag);
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
}
