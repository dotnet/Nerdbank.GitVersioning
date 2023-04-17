// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using Nerdbank.GitVersioning.ManagedGit;
using Xunit;

namespace ManagedGit;

// Test case borrowed from https://stefan.saasen.me/articles/git-clone-in-haskell-from-the-bottom-up/#format-of-the-delta-representation
public class DeltaStreamReaderTests
{
    [Fact]
    public void ReadCopyInstruction()
    {
        using (Stream stream = new MemoryStream(
            new byte[]
            {
                0b_10110000,
                0b_11010001,
                0b_00000001,
            }))
        {
            DeltaInstruction instruction = DeltaStreamReader.Read(stream).Value;

            Assert.Equal(DeltaInstructionType.Copy, instruction.InstructionType);
            Assert.Equal(0, instruction.Offset);
            Assert.Equal(465, instruction.Size);
        }
    }

    [Fact]
    public void ReadCopyInstruction_Memory()
    {
        byte[] stream = new byte[]
        {
            0b_10110000,
            0b_11010001,
            0b_00000001,
        };
        var memory = new ReadOnlyMemory<byte>(stream);

        DeltaInstruction instruction = DeltaStreamReader.Read(ref memory).Value;

        Assert.Equal(0, memory.Length);
        Assert.Equal(DeltaInstructionType.Copy, instruction.InstructionType);
        Assert.Equal(0, instruction.Offset);
        Assert.Equal(465, instruction.Size);
    }

    [Fact]
    public void ReadInsertInstruction()
    {
        using (Stream stream = new MemoryStream(new byte[] { 0b_00010111 }))
        {
            DeltaInstruction instruction = DeltaStreamReader.Read(stream).Value;

            Assert.Equal(DeltaInstructionType.Insert, instruction.InstructionType);
            Assert.Equal(0, instruction.Offset);
            Assert.Equal(23, instruction.Size);
        }
    }

    [Fact]
    public void ReadInsertInstruction_Memory()
    {
        byte[] stream = new byte[] { 0b_00010111 };
        var memory = new ReadOnlyMemory<byte>(stream);

        DeltaInstruction instruction = DeltaStreamReader.Read(ref memory).Value;

        Assert.Equal(0, memory.Length);
        Assert.Equal(DeltaInstructionType.Insert, instruction.InstructionType);
        Assert.Equal(0, instruction.Offset);
        Assert.Equal(23, instruction.Size);
    }

    [Fact]
    public void ReadStreamTest()
    {
        using (Stream stream = new MemoryStream(
            new byte[]
            {
                0b_10110011, 0b_11001110, 0b_00000001, 0b_00100111, 0b_00000001,
                0b_10110011, 0b_01011111, 0b_00000011, 0b_01101100, 0b_00010000, 0b_10010011,
                0b_11110101, 0b_00000010, 0b_01101011, 0b_10110011, 0b_11001011, 0b_00010011,
                0b_01000110, 0b_00000011,
            }))
        {
            Collection<DeltaInstruction> instructions = new Collection<DeltaInstruction>();

            DeltaInstruction? current;

            while ((current = DeltaStreamReader.Read(stream)) is not null)
            {
                instructions.Add(current.Value);
            }

            Assert.Collection(
                instructions,
                instruction =>
                {
                    Assert.Equal(DeltaInstructionType.Copy, instruction.InstructionType);
                    Assert.Equal(462, instruction.Offset);
                    Assert.Equal(295, instruction.Size);
                },
                instruction =>
                {
                    Assert.Equal(DeltaInstructionType.Copy, instruction.InstructionType);
                    Assert.Equal(863, instruction.Offset);
                    Assert.Equal(4204, instruction.Size);
                },
                instruction =>
                {
                    Assert.Equal(DeltaInstructionType.Copy, instruction.InstructionType);
                    Assert.Equal(757, instruction.Offset);
                    Assert.Equal(107, instruction.Size);
                },
                instruction =>
                {
                    Assert.Equal(DeltaInstructionType.Copy, instruction.InstructionType);
                    Assert.Equal(5067, instruction.Offset);
                    Assert.Equal(838, instruction.Size);
                });
        }
    }

    [Fact]
    public void ReadStreamTest_Memory()
    {
        byte[] stream =
            new byte[]
            {
                0b_10110011, 0b_11001110, 0b_00000001, 0b_00100111, 0b_00000001,
                0b_10110011, 0b_01011111, 0b_00000011, 0b_01101100, 0b_00010000, 0b_10010011,
                0b_11110101, 0b_00000010, 0b_01101011, 0b_10110011, 0b_11001011, 0b_00010011,
                0b_01000110, 0b_00000011,
            };
        var memory = new ReadOnlyMemory<byte>(stream);

        Collection<DeltaInstruction> instructions = new Collection<DeltaInstruction>();

        DeltaInstruction? current;

        while ((current = DeltaStreamReader.Read(ref memory)) is not null)
        {
            instructions.Add(current.Value);
        }

        Assert.Equal(0, memory.Length);
        Assert.Collection(
            instructions,
            instruction =>
            {
                Assert.Equal(DeltaInstructionType.Copy, instruction.InstructionType);
                Assert.Equal(462, instruction.Offset);
                Assert.Equal(295, instruction.Size);
            },
            instruction =>
            {
                Assert.Equal(DeltaInstructionType.Copy, instruction.InstructionType);
                Assert.Equal(863, instruction.Offset);
                Assert.Equal(4204, instruction.Size);
            },
            instruction =>
            {
                Assert.Equal(DeltaInstructionType.Copy, instruction.InstructionType);
                Assert.Equal(757, instruction.Offset);
                Assert.Equal(107, instruction.Size);
            },
            instruction =>
            {
                Assert.Equal(DeltaInstructionType.Copy, instruction.InstructionType);
                Assert.Equal(5067, instruction.Offset);
                Assert.Equal(838, instruction.Size);
            });
    }
}
