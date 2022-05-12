// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// Represents an instruction in a deltified stream.
/// </summary>
/// <seealso href="https://git-scm.com/docs/pack-format#_deltified_representation"/>
public struct DeltaInstruction
{
    /// <summary>
    /// Gets or sets the type of the current instruction.
    /// </summary>
    public DeltaInstructionType InstructionType;

    /// <summary>
    /// If the <see cref="InstructionType"/> is <see cref="DeltaInstructionType.Copy"/>,
    /// the offset of the base stream to start copying from.
    /// </summary>
    public int Offset;

    /// <summary>
    /// The number of bytes to copy or insert.
    /// </summary>
    public int Size;
}
