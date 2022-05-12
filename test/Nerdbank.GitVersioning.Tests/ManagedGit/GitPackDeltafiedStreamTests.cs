// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace ManagedGit;

public class GitPackDeltafiedStreamTests
{
    // Reconstructs an object by reading the base stream and the delta stream.
    // You can create delta representations of an object by running the
    // test tool which is located in the t/helper/ folder of the Git source repository.
    // Use with the delta -d [base file,in] [updated file,in] [delta file,out] arguments.
    [Theory]
    [InlineData(@"ManagedGit\commit-4497b0eaaa89abf0e6d70961ad5f04fd3a49cbc6", @"ManagedGit\commit.delta", @"ManagedGit\commit-d56dc3ed179053abef2097d1120b4507769bcf1a")]
    [InlineData(@"ManagedGit\tree-bb36cf0ca445ccc8e5ce9cc88f7cf74128e96dc9", @"ManagedGit\tree.delta", @"ManagedGit\tree-f914b48023c7c804a4f3be780d451f31aef74ac1")]
    public void TestDeltaStream(string basePath, string deltaPath, string expectedPath)
    {
        byte[] expected = null;

        using (Stream expectedStream = TestUtilities.GetEmbeddedResource(expectedPath))
        {
            expected = new byte[expectedStream.Length];
            expectedStream.Read(expected);
        }

        byte[] actual = new byte[expected.Length];

        using (Stream baseStream = TestUtilities.GetEmbeddedResource(basePath))
        using (Stream deltaStream = TestUtilities.GetEmbeddedResource(deltaPath))
        using (GitPackDeltafiedStream deltafiedStream = new GitPackDeltafiedStream(baseStream, deltaStream))
        {
            ////Assert.Equal(expected.Length, deltafiedStream.Length);

            deltafiedStream.Read(actual);

            Assert.Equal(expected, actual);
        }
    }
}
