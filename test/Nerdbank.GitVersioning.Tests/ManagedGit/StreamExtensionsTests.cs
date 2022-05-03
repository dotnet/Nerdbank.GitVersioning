// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace ManagedGit;

public class StreamExtensionsTests
{
    [Fact]
    public void ReadTest()
    {
        byte[] data = new byte[] { 0b10010001, 0b00101110 };

        using (MemoryStream stream = new MemoryStream(data))
        {
            Assert.Equal(5905, stream.ReadMbsInt());
        }
    }
}
