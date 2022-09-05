// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;
using ZLibStream = Nerdbank.GitVersioning.ManagedGit.ZLibStream;

namespace ManagedGit;

public class GitPackTests : IDisposable
{
    private readonly string indexFile = Path.GetTempFileName();
    private readonly string packFile = Path.GetTempFileName();

    public GitPackTests()
    {
        using (Stream resourceStream = TestUtilities.GetEmbeddedResource(@"ManagedGit\pack-7d6b2c56ffb97eedb92f4e28583c093f7ee4b3d9.idx"))
        using (FileStream stream = File.Open(this.indexFile, FileMode.Open))
        {
            resourceStream.CopyTo(stream);
        }

        using (Stream resourceStream = TestUtilities.GetEmbeddedResource(@"ManagedGit\pack-7d6b2c56ffb97eedb92f4e28583c093f7ee4b3d9.pack"))
        using (FileStream stream = File.Open(this.packFile, FileMode.Open))
        {
            resourceStream.CopyTo(stream);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            File.Delete(this.indexFile);
        }
        catch (UnauthorizedAccessException)
        {
            // TBD: Figure out what's keeping a lock on the file. Seems to be unique to Windows.
        }

        try
        {
            File.Delete(this.packFile);
        }
        catch (UnauthorizedAccessException)
        {
            // TBD: Figure out what's keeping a lock on the file. Seems to be unique to Windows.
        }
    }

    [Fact]
    public void GetPackedObject()
    {
        using (var gitPack = new GitPack(
            (sha, objectType) => null,
            new Lazy<FileStream>(() => File.OpenRead(this.indexFile)),
            () => File.OpenRead(this.packFile),
            GitPackNullCache.Instance))
        using (Stream commitStream = gitPack.GetObject(12, "commit"))
        using (SHA1 sha = SHA1.Create())
        {
            // This commit is not deltafied. It is stored as a .gz-compressed stream in the pack file.
            ZLibStream zlibStream = Assert.IsType<ZLibStream>(commitStream);
            DeflateStream deflateStream = Assert.IsType<DeflateStream>(zlibStream.BaseStream);

            if (IntPtr.Size > 4)
            {
                MemoryMappedStream pooledStream = Assert.IsType<MemoryMappedStream>(deflateStream.BaseStream);
            }
            else
            {
                FileStream pooledStream = Assert.IsType<FileStream>(deflateStream.BaseStream);
            }

            Assert.Equal(222, commitStream.Length);
            Assert.Equal("/zgldANj+jvgOwlecnOKylZDVQg=", Convert.ToBase64String(sha.ComputeHash(commitStream)));
        }
    }

    [Fact]
    public void GetDeltafiedObject()
    {
        using (var gitPack = new GitPack(
            (sha, objectType) => null,
            new Lazy<FileStream>(() => File.OpenRead(this.indexFile)),
            () => File.OpenRead(this.packFile),
            GitPackNullCache.Instance))
        using (Stream commitStream = gitPack.GetObject(317, "commit"))
        using (SHA1 sha = SHA1.Create())
        {
            // This commit is not deltafied. It is stored as a .gz-compressed stream in the pack file.
            GitPackDeltafiedStream deltaStream = Assert.IsType<GitPackDeltafiedStream>(commitStream);
            ZLibStream zlibStream = Assert.IsType<ZLibStream>(deltaStream.BaseStream);
            DeflateStream deflateStream = Assert.IsType<DeflateStream>(zlibStream.BaseStream);

            if (IntPtr.Size > 4)
            {
                MemoryMappedStream pooledStream = Assert.IsType<MemoryMappedStream>(deflateStream.BaseStream);
            }
            else
            {
                FileStream directAccessStream = Assert.IsType<FileStream>(deflateStream.BaseStream);
            }

            Assert.Equal(137, commitStream.Length);
            Assert.Equal("lZu/7nGb0n1UuO9SlPluFnSvj4o=", Convert.ToBase64String(sha.ComputeHash(commitStream)));
        }
    }

    [Fact]
    public void GetInvalidObject()
    {
        using (var gitPack = new GitPack(
            (sha, objectType) => null,
            new Lazy<FileStream>(() => File.OpenRead(this.indexFile)),
            () => File.OpenRead(this.packFile),
            GitPackNullCache.Instance))
        {
            Assert.Throws<GitException>(() => gitPack.GetObject(12, "invalid"));
            Assert.Throws<IOException>(() => gitPack.GetObject(-1, "commit"));
            Assert.Throws<GitException>(() => gitPack.GetObject(1, "commit"));
            Assert.Throws<GitException>(() => gitPack.GetObject(2, "commit"));
            Assert.Throws<GitException>(() => gitPack.GetObject(int.MaxValue, "commit"));
        }
    }

    [Fact]
    public void TryGetObjectTest()
    {
        using (var gitPack = new GitPack(
            (sha, objectType) => null,
            new Lazy<FileStream>(() => File.OpenRead(this.indexFile)),
            () => File.OpenRead(this.packFile),
            GitPackNullCache.Instance))
        using (SHA1 sha = SHA1.Create())
        {
            Assert.True(gitPack.TryGetObject(GitObjectId.Parse("f5b401f40ad83f13030e946c9ea22cb54cb853cd"), "commit", out Stream commitStream));
            using (commitStream)
            {
                // This commit is not deltafied. It is stored as a .gz-compressed stream in the pack file.
                ZLibStream zlibStream = Assert.IsType<ZLibStream>(commitStream);
                DeflateStream deflateStream = Assert.IsType<DeflateStream>(zlibStream.BaseStream);

                if (IntPtr.Size > 4)
                {
                    MemoryMappedStream pooledStream = Assert.IsType<MemoryMappedStream>(deflateStream.BaseStream);
                }
                else
                {
                    FileStream directAccessStream = Assert.IsType<FileStream>(deflateStream.BaseStream);
                }

                Assert.Equal(222, commitStream.Length);
                Assert.Equal("/zgldANj+jvgOwlecnOKylZDVQg=", Convert.ToBase64String(sha.ComputeHash(commitStream)));
            }
        }
    }

    [Fact]
    public void TryGetMissingObjectTest()
    {
        using (var gitPack = new GitPack(
            (sha, objectType) => null,
            new Lazy<FileStream>(() => File.OpenRead(this.indexFile)),
            () => File.OpenRead(this.packFile),
            GitPackNullCache.Instance))
        {
            Assert.False(gitPack.TryGetObject(GitObjectId.Empty, "commit", out Stream commitStream));
        }
    }
}
