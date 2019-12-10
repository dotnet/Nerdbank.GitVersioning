using System;
using System.IO;
using Nerdbank.GitVersioning;
using Xunit;

public class FilterPathTests
{
    [Theory]
    [InlineData("./", "foo", "foo")]
    [InlineData("../relative-dir", "foo", "relative-dir")]
    [InlineData("../../some/dir/here", "foo/multi/wow", "foo/some/dir/here")]
    [InlineData("relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData("./relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData("./dir\\hi/relativepath.txt", "foo", "foo/dir/hi/relativepath.txt")]
    [InlineData(".\\relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData(":^relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData(":!relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData(":!/absolutepath.txt", "foo", "absolutepath.txt")]
    [InlineData(":!\\absolutepath.txt", "foo", "absolutepath.txt")]
    [InlineData("../bar/relativepath.txt", "foo", "bar/relativepath.txt")]
    [InlineData(":/", "foo", "")]
    [InlineData(":/absolutepath.txt", "foo", "absolutepath.txt")]
    [InlineData(":/bar/absolutepath.txt", "foo", "bar/absolutepath.txt")]
    [InlineData(":\\bar\\absolutepath.txt", "foo", "bar/absolutepath.txt")]
    public void CanBeParsedToRepoRelativePath(string pathSpec, string relativeTo, string expected)
    {
        Assert.Equal(expected, new FilterPath(pathSpec, relativeTo).RepoRelativePath);
    }

    [Theory]
    [InlineData(":!.", "foo", "foo")]
    [InlineData(":!.", "foo", "foo/")]
    [InlineData(":!.", "foo", "foo/relativepath.txt")]
    [InlineData(":!relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData(":^relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData(":^./relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData(":^../bar", "foo", "bar")]
    [InlineData(":^../bar", "foo", "bar/")]
    [InlineData(":^../bar", "foo", "bar/somefile.txt")]
    [InlineData(":^/absolute.txt", "foo", "absolute.txt")]
    public void PathsCanBeExcluded(string pathSpec, string relativeTo, string repoRelativePath)
    {
        Assert.True(new FilterPath(pathSpec, relativeTo, true).Excludes(repoRelativePath));
        Assert.True(new FilterPath(pathSpec, relativeTo, false).Excludes(repoRelativePath));
    }

    [Theory]
    [InlineData(":!.", "foo", "foo.txt")]
    [InlineData(":^relativepath.txt", "foo", "foo2/relativepath.txt")]
    [InlineData(":^/absolute.txt", "foo", "absolute.txt.bak")]
    [InlineData(":^/absolute.txt", "foo", "absolute")]

    // Not exclude paths
    [InlineData(":/absolute.txt", "foo", "absolute.txt")]
    [InlineData("/absolute.txt", "foo", "absolute.txt")]
    [InlineData("../root.txt", "foo", "root.txt")]
    [InlineData("relativepath.txt", "foo", "foo/relativepath.txt")]
    public void NonMatchingPathsAreNotExcluded(string pathSpec, string relativeTo, string repoRelativePath)
    {
        Assert.False(new FilterPath(pathSpec, relativeTo, true).Excludes(repoRelativePath));
        Assert.False(new FilterPath(pathSpec, relativeTo, false).Excludes(repoRelativePath));
    }

    [Theory]
    [InlineData(":!.", "foo", "Foo")]
    [InlineData(":!.", "foo", "Foo/")]
    [InlineData(":!.", "foo", "Foo/relativepath.txt")]
    [InlineData(":!RelativePath.txt", "foo", "foo/relativepath.txt")]
    [InlineData(":^relativepath.txt", "foo", "Foo/RelativePath.txt")]
    [InlineData(":^./relativepath.txt", "Foo", "foo/RelativePath.txt")]
    [InlineData(":^../bar", "foo", "Bar")]
    [InlineData(":^../bar", "foo", "Bar/")]
    [InlineData(":^../bar", "foo", "Bar/SomeFile.txt")]
    [InlineData(":^/absOLUte.txt", "foo", "Absolute.TXT")]
    public void PathsCanBeExcludedCaseInsensitive(string pathSpec, string relativeTo, string repoRelativePath)
    {
        Assert.True(new FilterPath(pathSpec, relativeTo, true).Excludes(repoRelativePath));
    }

    [Theory]
    [InlineData(":!.", "foo", "Foo")]
    [InlineData(":!.", "foo", "Foo/")]
    [InlineData(":!.", "foo", "Foo/relativepath.txt")]
    [InlineData(":!RelativePath.txt", "foo", "foo/relativepath.txt")]
    [InlineData(":^relativepath.txt", "foo", "Foo/RelativePath.txt")]
    [InlineData(":^./relativepath.txt", "Foo", "foo/RelativePath.txt")]
    [InlineData(":^../bar", "foo", "Bar")]
    [InlineData(":^../bar", "foo", "Bar/")]
    [InlineData(":^../bar", "foo", "Bar/SomeFile.txt")]
    [InlineData(":^/absOLUte.txt", "foo", "Absolute.TXT")]
    public void NonMatchingPathsAreNotExcludedCaseSensitive(string pathSpec, string relativeTo, string repoRelativePath)
    {
        Assert.False(new FilterPath(pathSpec, relativeTo, false).Excludes(repoRelativePath));
    }

    [Fact]
    public void InvalidPathspecsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new FilterPath(null, ""));
        Assert.Throws<ArgumentException>(() => new FilterPath("", ""));
        Assert.Throws<FormatException>(() => new FilterPath(":?", ""));
        Assert.Throws<FormatException>(() => new FilterPath("../foo.txt", ""));
        Assert.Throws<FormatException>(() => new FilterPath(".././a/../../foo.txt", "foo"));
    }
}