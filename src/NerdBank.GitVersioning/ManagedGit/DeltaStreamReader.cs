// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Reads delta instructions from a <see cref="Stream"/>.
/// </summary>
/// <seealso href="https://git-scm.com/docs/pack-format#_deltified_representation"/>
public static class DeltaStreamReader
{
    /// <summary>
    /// Reads the next instruction from a <see cref="Stream"/>.
    /// </summary>
    /// <param name="stream">
    /// The stream from which to read the instruction.
    /// </param>
    /// <returns>
    /// The next instruction if found; otherwise, <see langword="null"/>.
    /// </returns>
    public static DeltaInstruction? Read(Stream stream)
    {
        int next = stream.ReadByte();

        if (next == -1)
        {
            return null;
        }

        byte instruction = (byte)next;

        DeltaInstruction value;
        value.Offset = 0;
        value.Size = 0;

        value.InstructionType = (DeltaInstructionType)((instruction & 0b1000_0000) >> 7);

        if (value.InstructionType == DeltaInstructionType.Insert)
        {
            value.Size = instruction & 0b0111_1111;
        }
        else if (value.InstructionType == DeltaInstructionType.Copy)
        {
            // offset1
            if ((instruction & 0b0000_0001) != 0)
            {
                value.Offset |= (byte)stream.ReadByte();
            }

            // offset2
            if ((instruction & 0b0000_0010) != 0)
            {
                value.Offset |= (byte)stream.ReadByte() << 8;
            }

            // offset3
            if ((instruction & 0b0000_0100) != 0)
            {
                value.Offset |= (byte)stream.ReadByte() << 16;
            }

            // offset4
            if ((instruction & 0b0000_1000) != 0)
            {
                value.Offset |= (byte)stream.ReadByte() << 24;
            }

            // size1
            if ((instruction & 0b0001_0000) != 0)
            {
                value.Size = (byte)stream.ReadByte();
            }

            // size2
            if ((instruction & 0b0010_0000) != 0)
            {
                value.Size |= (byte)stream.ReadByte() << 8;
            }

            // size3
            if ((instruction & 0b0100_0000) != 0)
            {
                value.Size |= (byte)stream.ReadByte() << 16;
            }

            // Size zero is automatically converted to 0x10000.
            if (value.Size == 0)
            {
                value.Size = 0x10000;
            }
        }

        return value;
    }

    /// <summary>
    /// Reads the next instruction from a <see cref="Stream"/>.
    /// </summary>
    /// <param name="stream">
    /// The stream from which to read the instruction.
    /// </param>
    /// <returns>
    /// The next instruction if found; otherwise, <see langword="null"/>.
    /// </returns>
    public static DeltaInstruction? Read(ref ReadOnlyMemory<byte> stream)
    {
        if (stream.Length == 0)
        {
            return null;
        }

        ReadOnlySpan<byte> span = stream.Span;
        int i = 0;
        int next = span[i++];

        byte instruction = (byte)next;

        DeltaInstruction value;
        value.Offset = 0;
        value.Size = 0;

        value.InstructionType = (DeltaInstructionType)((instruction & 0b1000_0000) >> 7);

        if (value.InstructionType == DeltaInstructionType.Insert)
        {
            value.Size = instruction & 0b0111_1111;
        }
        else if (value.InstructionType == DeltaInstructionType.Copy)
        {
            // offset1
            if ((instruction & 0b0000_0001) != 0)
            {
                value.Offset |= span[i++];
            }

            // offset2
            if ((instruction & 0b0000_0010) != 0)
            {
                value.Offset |= span[i++] << 8;
            }

            // offset3
            if ((instruction & 0b0000_0100) != 0)
            {
                value.Offset |= span[i++] << 16;
            }

            // offset4
            if ((instruction & 0b0000_1000) != 0)
            {
                value.Offset |= span[i++] << 24;
            }

            // size1
            if ((instruction & 0b0001_0000) != 0)
            {
                value.Size = span[i++];
            }

            // size2
            if ((instruction & 0b0010_0000) != 0)
            {
                value.Size |= span[i++] << 8;
            }

            // size3
            if ((instruction & 0b0100_0000) != 0)
            {
                value.Size |= span[i++] << 16;
            }
        }

        stream = stream.Slice(i);
        return value;
    }
}
