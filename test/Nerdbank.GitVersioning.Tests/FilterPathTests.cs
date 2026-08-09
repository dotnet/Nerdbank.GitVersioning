// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using Nerdbank.GitVersioning;
using Xunit;

public class FilterPathTests
{
    [Theory]
    [InlineData("./", "foo", "foo")]
    [InlineData("../relative-dir", "foo", "relative-dir")]
    [InlineData("relative-dir", "some/dir/../zany", "some/zany/relative-dir")]
    [InlineData("relative-dir", "some/dir/..", "some/relative-dir")]
    [InlineData("relative-dir", "some/../subdir", "subdir/relative-dir")]
    [InlineData("../../some/dir/here", "foo/multi/wow", "foo/some/dir/here")]
    [InlineData("relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData("./relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData(":^relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData(":!relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData(":!/absolutepath.txt", "foo", "absolutepath.txt")]
    [InlineData("../bar/relativepath.txt", "foo", "bar/relativepath.txt")]
    [InlineData("/", "foo", "")]
    [InlineData("/absolute/file.txt", "foo", "absolute/file.txt")]
    [InlineData(":/", "foo", "")]
    [InlineData(":/absolutepath.txt", "foo", "absolutepath.txt")]
    [InlineData(":/bar/absolutepath.txt", "foo", "bar/absolutepath.txt")]
    [InlineData(":/loc/*/MyProduct.*", "foo", "loc/*/MyProduct.*")]
    [InlineData("../**/generated?.cs", "foo/bar", "foo/**/generated?.cs")]
    public void CanBeParsedToRepoRelativePath(string pathSpec, string relativeTo, string expected)
    {
        Assert.Equal(expected, new FilterPath(pathSpec, relativeTo).RepoRelativePath);
    }

    [WindowsTheory]
    [InlineData("./dir\\hi/relativepath.txt", "foo", "foo/dir/hi/relativepath.txt")]
    [InlineData(".\\relativepath.txt", "foo", "foo/relativepath.txt")]
    [InlineData(":!\\absolutepath.txt", "foo", "absolutepath.txt")]
    [InlineData(":\\bar\\absolutepath.txt", "foo", "bar/absolutepath.txt")]
    public void CanBeParsedToRepoRelativePath_WindowsOnly(string pathSpec, string relativeTo, string expected)
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
        Assert.True(new FilterPath(pathSpec, relativeTo).Excludes(repoRelativePath, true));
        Assert.True(new FilterPath(pathSpec, relativeTo).Excludes(repoRelativePath, false));
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
        Assert.False(new FilterPath(pathSpec, relativeTo).Excludes(repoRelativePath, true));
        Assert.False(new FilterPath(pathSpec, relativeTo).Excludes(repoRelativePath, false));
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
        Assert.True(new FilterPath(pathSpec, relativeTo).Excludes(repoRelativePath, true));
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
        Assert.False(new FilterPath(pathSpec, relativeTo).Excludes(repoRelativePath, false));
    }

    [Theory]
    [InlineData(":/loc/*/MyProduct.*", "loc/en/MyProduct.resx")]
    [InlineData(":/loc/*/MyProduct.*", "loc/en/MyProduct.")]
    [InlineData(":/loc/?/MyProduct.*", "loc/e/MyProduct.resx")]
    [InlineData(":/loc/**/MyProduct.*", "loc/MyProduct.resx")]
    [InlineData(":/loc/**/MyProduct.*", "loc/en/subdir/MyProduct.resx")]
    [InlineData(":/**/MyProduct.*", "MyProduct.resx")]
    [InlineData("localization/*/messages.json", "src/localization/en/messages.json")]
    [InlineData(":/eng/*", "eng/product/src/file.cs")]
    public void PathsCanBeIncludedWithWildcards(string pathSpec, string repoRelativePath)
    {
        Assert.True(new FilterPath(pathSpec, "src").Includes(repoRelativePath, false));
    }

    [Theory]
    [InlineData(":/loc/*/MyProduct.*", "loc/en/subdir/MyProduct.resx")]
    [InlineData(":/loc/?/MyProduct.*", "loc/en/MyProduct.resx")]
    [InlineData(":/loc/*/MyProduct.*", "loc/en/OtherProduct.resx")]
    [InlineData(":/loc/*/MyProduct.*", "loc/EN/myproduct.resx")]
    public void PathsDoNotMatchWildcards(string pathSpec, string repoRelativePath)
    {
        Assert.False(new FilterPath(pathSpec, string.Empty).Includes(repoRelativePath, false));
    }

    [Fact]
    public void WildcardMatchingCanIgnoreCase()
    {
        var filter = new FilterPath(":/loc/*/MyProduct.*", string.Empty);

        Assert.True(filter.Includes("LOC/en/myproduct.resx", true));
        Assert.False(filter.Includes("LOC/en/myproduct.resx", false));
    }

    [Fact]
    public void PathsCanBeExcludedWithWildcards()
    {
        var filter = new FilterPath(":^/loc/**/generated?.cs", string.Empty);

        Assert.True(filter.Excludes("loc/generated1.cs", false));
        Assert.True(filter.Excludes("loc/en/subdir/generatedA.cs", false));
        Assert.False(filter.Excludes("loc/en/generated.cs", false));
    }

    [Theory]
    [InlineData("loc")]
    [InlineData("loc/en")]
    [InlineData("loc/en/MyProduct.resources")]
    public void WildcardFilterMayIncludeChildren(string repoRelativePath)
    {
        var filter = new FilterPath(":/loc/*/MyProduct.*", string.Empty);

        Assert.True(filter.IncludesChildren(repoRelativePath, false));
    }

    [Theory]
    [InlineData("localization")]
    [InlineData("docs")]
    public void WildcardFilterCannotIncludeChildren(string repoRelativePath)
    {
        var filter = new FilterPath(":/loc/*/MyProduct.*", string.Empty);

        Assert.False(filter.IncludesChildren(repoRelativePath, false));
    }

    [Fact]
    public void InvalidPathspecsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new FilterPath(null, string.Empty));
        Assert.Throws<ArgumentException>(() => new FilterPath(string.Empty, string.Empty));
        Assert.Throws<FormatException>(() => new FilterPath(":?", string.Empty));
        Assert.Throws<FormatException>(() => new FilterPath("../foo.txt", string.Empty));
        Assert.Throws<FormatException>(() => new FilterPath(".././a/../../foo.txt", "foo"));
    }

    [Theory]
    [InlineData(":/abc/def", "", "/abc/def")]
    [InlineData(":/abc/def", ".", "/abc/def")]
    [InlineData("abc", ".", "./abc")]
    [InlineData(".", ".", "./")]
    [InlineData("./", ".", "./")]
    [InlineData("./", "", "./")]
    [InlineData("abc/def", ".", "./abc/def")]
    [InlineData("abc/def", "./foo", "./abc/def")]
    [InlineData("../Directory.Build.props", "./foo", "../Directory.Build.props")]
    [InlineData(":!/Directory.Build.props", "./foo", ":!/Directory.Build.props")]
    [InlineData(":!relative.txt", "./foo", ":!relative.txt")]
    [InlineData(":/loc/*/MyProduct.*", "./foo", "/loc/*/MyProduct.*")]
    [InlineData("../**/generated?.cs", "./foo", "../**/generated?.cs")]
    public void ToPathSpec(string pathSpec, string relativeTo, string expectedPathSpec)
    {
        Assert.Equal(expectedPathSpec, new FilterPath(pathSpec, relativeTo).ToPathSpec(relativeTo));
    }

    [Theory]
    [InlineData("foo/bar", "foo", "./bar")]
    [InlineData("foo/bar", "FOO", "./bar")]
    public void ToPathSpecTest(string pathSpec, string relativeTo, string expectedPathSpec)
    {
        Assert.Equal(expectedPathSpec, new FilterPath(pathSpec, ".").ToPathSpec(relativeTo));
    }
}
