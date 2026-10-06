// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using Nerdbank.GitVersioning;
using Xunit;

public class FilterPathTests
{
    [Test]
    [Arguments("./", "foo", "foo")]
    [Arguments("../relative-dir", "foo", "relative-dir")]
    [Arguments("relative-dir", "some/dir/../zany", "some/zany/relative-dir")]
    [Arguments("relative-dir", "some/dir/..", "some/relative-dir")]
    [Arguments("relative-dir", "some/../subdir", "subdir/relative-dir")]
    [Arguments("../../some/dir/here", "foo/multi/wow", "foo/some/dir/here")]
    [Arguments("relativepath.txt", "foo", "foo/relativepath.txt")]
    [Arguments("./relativepath.txt", "foo", "foo/relativepath.txt")]
    [Arguments(":^relativepath.txt", "foo", "foo/relativepath.txt")]
    [Arguments(":!relativepath.txt", "foo", "foo/relativepath.txt")]
    [Arguments(":!/absolutepath.txt", "foo", "absolutepath.txt")]
    [Arguments("../bar/relativepath.txt", "foo", "bar/relativepath.txt")]
    [Arguments("/", "foo", "")]
    [Arguments("/absolute/file.txt", "foo", "absolute/file.txt")]
    [Arguments(":/", "foo", "")]
    [Arguments(":/absolutepath.txt", "foo", "absolutepath.txt")]
    [Arguments(":/bar/absolutepath.txt", "foo", "bar/absolutepath.txt")]
    [Arguments(":/loc/*/MyProduct.*", "foo", "loc/*/MyProduct.*")]
    [Arguments("../**/generated?.cs", "foo/bar", "foo/**/generated?.cs")]
    public void CanBeParsedToRepoRelativePath(string pathSpec, string relativeTo, string expected)
    {
        Assert.Equal(expected, new FilterPath(pathSpec, relativeTo).RepoRelativePath);
    }

    [Test, RunOn(TUnit.Core.Enums.OS.Windows)]
    [Arguments("./dir\\hi/relativepath.txt", "foo", "foo/dir/hi/relativepath.txt")]
    [Arguments(".\\relativepath.txt", "foo", "foo/relativepath.txt")]
    [Arguments(":!\\absolutepath.txt", "foo", "absolutepath.txt")]
    [Arguments(":\\bar\\absolutepath.txt", "foo", "bar/absolutepath.txt")]
    public void CanBeParsedToRepoRelativePath_WindowsOnly(string pathSpec, string relativeTo, string expected)
    {
        Assert.Equal(expected, new FilterPath(pathSpec, relativeTo).RepoRelativePath);
    }

    [Test]
    [Arguments(":!.", "foo", "foo")]
    [Arguments(":!.", "foo", "foo/")]
    [Arguments(":!.", "foo", "foo/relativepath.txt")]
    [Arguments(":!relativepath.txt", "foo", "foo/relativepath.txt")]
    [Arguments(":^relativepath.txt", "foo", "foo/relativepath.txt")]
    [Arguments(":^./relativepath.txt", "foo", "foo/relativepath.txt")]
    [Arguments(":^../bar", "foo", "bar")]
    [Arguments(":^../bar", "foo", "bar/")]
    [Arguments(":^../bar", "foo", "bar/somefile.txt")]
    [Arguments(":^/absolute.txt", "foo", "absolute.txt")]
    public void PathsCanBeExcluded(string pathSpec, string relativeTo, string repoRelativePath)
    {
        Assert.True(new FilterPath(pathSpec, relativeTo).Excludes(repoRelativePath, true));
        Assert.True(new FilterPath(pathSpec, relativeTo).Excludes(repoRelativePath, false));
    }

    [Test]
    [Arguments(":!.", "foo", "foo.txt")]
    [Arguments(":^relativepath.txt", "foo", "foo2/relativepath.txt")]
    [Arguments(":^/absolute.txt", "foo", "absolute.txt.bak")]
    [Arguments(":^/absolute.txt", "foo", "absolute")]

    // Not exclude paths
    [Arguments(":/absolute.txt", "foo", "absolute.txt")]
    [Arguments("/absolute.txt", "foo", "absolute.txt")]
    [Arguments("../root.txt", "foo", "root.txt")]
    [Arguments("relativepath.txt", "foo", "foo/relativepath.txt")]
    public void NonMatchingPathsAreNotExcluded(string pathSpec, string relativeTo, string repoRelativePath)
    {
        Assert.False(new FilterPath(pathSpec, relativeTo).Excludes(repoRelativePath, true));
        Assert.False(new FilterPath(pathSpec, relativeTo).Excludes(repoRelativePath, false));
    }

    [Test]
    [Arguments(":!.", "foo", "Foo")]
    [Arguments(":!.", "foo", "Foo/")]
    [Arguments(":!.", "foo", "Foo/relativepath.txt")]
    [Arguments(":!RelativePath.txt", "foo", "foo/relativepath.txt")]
    [Arguments(":^relativepath.txt", "foo", "Foo/RelativePath.txt")]
    [Arguments(":^./relativepath.txt", "Foo", "foo/RelativePath.txt")]
    [Arguments(":^../bar", "foo", "Bar")]
    [Arguments(":^../bar", "foo", "Bar/")]
    [Arguments(":^../bar", "foo", "Bar/SomeFile.txt")]
    [Arguments(":^/absOLUte.txt", "foo", "Absolute.TXT")]
    public void PathsCanBeExcludedCaseInsensitive(string pathSpec, string relativeTo, string repoRelativePath)
    {
        Assert.True(new FilterPath(pathSpec, relativeTo).Excludes(repoRelativePath, true));
    }

    [Test]
    [Arguments(":!.", "foo", "Foo")]
    [Arguments(":!.", "foo", "Foo/")]
    [Arguments(":!.", "foo", "Foo/relativepath.txt")]
    [Arguments(":!RelativePath.txt", "foo", "foo/relativepath.txt")]
    [Arguments(":^relativepath.txt", "foo", "Foo/RelativePath.txt")]
    [Arguments(":^./relativepath.txt", "Foo", "foo/RelativePath.txt")]
    [Arguments(":^../bar", "foo", "Bar")]
    [Arguments(":^../bar", "foo", "Bar/")]
    [Arguments(":^../bar", "foo", "Bar/SomeFile.txt")]
    [Arguments(":^/absOLUte.txt", "foo", "Absolute.TXT")]
    public void NonMatchingPathsAreNotExcludedCaseSensitive(string pathSpec, string relativeTo, string repoRelativePath)
    {
        Assert.False(new FilterPath(pathSpec, relativeTo).Excludes(repoRelativePath, false));
    }

    [Test]
    [Arguments(":/loc/*/MyProduct.*", "loc/en/MyProduct.resx")]
    [Arguments(":/loc/*/MyProduct.*", "loc/en/MyProduct.")]
    [Arguments(":/loc/?/MyProduct.*", "loc/e/MyProduct.resx")]
    [Arguments(":/loc/**/MyProduct.*", "loc/MyProduct.resx")]
    [Arguments(":/loc/**/MyProduct.*", "loc/en/subdir/MyProduct.resx")]
    [Arguments(":/**/MyProduct.*", "MyProduct.resx")]
    [Arguments("localization/*/messages.json", "src/localization/en/messages.json")]
    [Arguments(":/eng/*", "eng/product/src/file.cs")]
    public void PathsCanBeIncludedWithWildcards(string pathSpec, string repoRelativePath)
    {
        Assert.True(new FilterPath(pathSpec, "src").Includes(repoRelativePath, false));
    }

    [Test]
    [Arguments(":/loc/*/MyProduct.*", "loc/en/subdir/MyProduct.resx")]
    [Arguments(":/loc/?/MyProduct.*", "loc/en/MyProduct.resx")]
    [Arguments(":/loc/*/MyProduct.*", "loc/en/OtherProduct.resx")]
    [Arguments(":/loc/*/MyProduct.*", "loc/EN/myproduct.resx")]
    public void PathsDoNotMatchWildcards(string pathSpec, string repoRelativePath)
    {
        Assert.False(new FilterPath(pathSpec, string.Empty).Includes(repoRelativePath, false));
    }

    [Test]
    public void WildcardMatchingCanIgnoreCase()
    {
        var filter = new FilterPath(":/loc/*/MyProduct.*", string.Empty);

        Assert.True(filter.Includes("LOC/en/myproduct.resx", true));
        Assert.False(filter.Includes("LOC/en/myproduct.resx", false));
    }

    [Test]
    public void PathsCanBeExcludedWithWildcards()
    {
        var filter = new FilterPath(":^/loc/**/generated?.cs", string.Empty);

        Assert.True(filter.Excludes("loc/generated1.cs", false));
        Assert.True(filter.Excludes("loc/en/subdir/generatedA.cs", false));
        Assert.False(filter.Excludes("loc/en/generated.cs", false));
    }

    [Test]
    [Arguments("loc")]
    [Arguments("loc/en")]
    [Arguments("loc/en/MyProduct.resources")]
    public void WildcardFilterMayIncludeChildren(string repoRelativePath)
    {
        var filter = new FilterPath(":/loc/*/MyProduct.*", string.Empty);

        Assert.True(filter.IncludesChildren(repoRelativePath, false));
    }

    [Test]
    [Arguments("localization")]
    [Arguments("docs")]
    public void WildcardFilterCannotIncludeChildren(string repoRelativePath)
    {
        var filter = new FilterPath(":/loc/*/MyProduct.*", string.Empty);

        Assert.False(filter.IncludesChildren(repoRelativePath, false));
    }

    [Test]
    public void InvalidPathspecsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new FilterPath(null, string.Empty));
        Assert.Throws<ArgumentException>(() => new FilterPath(string.Empty, string.Empty));
        Assert.Throws<FormatException>(() => new FilterPath(":?", string.Empty));
        Assert.Throws<FormatException>(() => new FilterPath("../foo.txt", string.Empty));
        Assert.Throws<FormatException>(() => new FilterPath(".././a/../../foo.txt", "foo"));
    }

    [Test]
    [Arguments(":/abc/def", "", "/abc/def")]
    [Arguments(":/abc/def", ".", "/abc/def")]
    [Arguments("abc", ".", "./abc")]
    [Arguments(".", ".", "./")]
    [Arguments("./", ".", "./")]
    [Arguments("./", "", "./")]
    [Arguments("abc/def", ".", "./abc/def")]
    [Arguments("abc/def", "./foo", "./abc/def")]
    [Arguments("../Directory.Build.props", "./foo", "../Directory.Build.props")]
    [Arguments(":!/Directory.Build.props", "./foo", ":!/Directory.Build.props")]
    [Arguments(":!relative.txt", "./foo", ":!relative.txt")]
    [Arguments(":/loc/*/MyProduct.*", "./foo", "/loc/*/MyProduct.*")]
    [Arguments("../**/generated?.cs", "./foo", "../**/generated?.cs")]
    public void ToPathSpec(string pathSpec, string relativeTo, string expectedPathSpec)
    {
        Assert.Equal(expectedPathSpec, new FilterPath(pathSpec, relativeTo).ToPathSpec(relativeTo));
    }

    [Test]
    [Arguments("foo/bar", "foo", "./bar")]
    [Arguments("foo/bar", "FOO", "./bar")]
    public void ToPathSpecTest(string pathSpec, string relativeTo, string expectedPathSpec)
    {
        Assert.Equal(expectedPathSpec, new FilterPath(pathSpec, ".").ToPathSpec(relativeTo));
    }
}
