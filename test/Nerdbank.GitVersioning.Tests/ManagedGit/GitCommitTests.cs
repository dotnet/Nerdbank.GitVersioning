// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace ManagedGit;

public class GitCommitTests
{
    private readonly byte[] shaAsByteArray = new byte[] { 0x4e, 0x91, 0x27, 0x36, 0xc2, 0x7e, 0x40, 0xb3, 0x89, 0x90, 0x4d, 0x04, 0x6d, 0xc6, 0x3d, 0xc9, 0xf5, 0x78, 0x11, 0x7f };

    [Fact]
    public void EqualsObjectTest()
    {
        var commit = new GitCommit()
        {
            Sha = GitObjectId.Parse(this.shaAsByteArray),
        };

        var commit2 = new GitCommit()
        {
            Sha = GitObjectId.Parse(this.shaAsByteArray),
        };

        var emptyCommit = new GitCommit()
        {
            Sha = GitObjectId.Empty,
        };

        // Must be equal to itself
        Assert.True(commit.Equals((object)commit));
        Assert.True(commit.Equals((object)commit2));

        // Not equal to null
        Assert.False(commit.Equals(null));

        // Not equal to other representations of the commit
        Assert.False(commit.Equals(this.shaAsByteArray));
        Assert.False(commit.Equals(commit.Sha));

        // Not equal to other object ids
        Assert.False(commit.Equals((object)emptyCommit));
    }

    [Fact]
    public void EqualsCommitTest()
    {
        var commit = new GitCommit()
        {
            Sha = GitObjectId.Parse(this.shaAsByteArray),
        };

        var commit2 = new GitCommit()
        {
            Sha = GitObjectId.Parse(this.shaAsByteArray),
        };

        var emptyCommit = new GitCommit()
        {
            Sha = GitObjectId.Empty,
        };

        // Must be equal to itself
        Assert.True(commit.Equals(commit2));
        Assert.True(commit.Equals(commit2));

        // Not equal to other object ids
        Assert.False(commit.Equals(emptyCommit));
    }

    [Fact]
    public void GetHashCodeTest()
    {
        var commit = new GitCommit()
        {
            Sha = GitObjectId.Parse(this.shaAsByteArray),
        };

        var emptyCommit = new GitCommit()
        {
            Sha = GitObjectId.Empty,
        };

        // The hash code is the int32 representation of the first 4 bytes of the SHA hash
        Assert.Equal(0x3627914e, commit.GetHashCode());
        Assert.Equal(0, emptyCommit.GetHashCode());
    }

    [Fact]
    public void ToStringTest()
    {
        var commit = new GitCommit()
        {
            Sha = GitObjectId.Parse(this.shaAsByteArray),
        };

        Assert.Equal("Git Commit: 4e912736c27e40b389904d046dc63dc9f578117f", commit.ToString());
    }
}
