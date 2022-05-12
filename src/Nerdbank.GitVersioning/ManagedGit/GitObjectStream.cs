// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Nerdbank.GitVersioning.ManagedGit;

/// <summary>
/// A <see cref="Stream"/> which reads data stored in the Git object store. The data is stored
/// as a gz-compressed stream, and is prefixed with the object type and data length.
/// </summary>
public class GitObjectStream : ZLibStream
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitObjectStream"/>  class.
    /// </summary>
    /// <param name="stream">
    /// The <see cref="Stream"/> from which to read data.
    /// </param>
    /// <param name="objectType">
    /// The expected object type of the git object.
    /// </param>
#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable. ObjectType is assigned in ReadObjectTypeAndLength.
    public GitObjectStream(Stream stream, string objectType)
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        : base(stream, -1)
    {
        this.ReadObjectTypeAndLength(objectType);
    }

    /// <summary>
    /// Gets the object type of this Git object.
    /// </summary>
    public string ObjectType { get; private set; }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => true;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    private void ReadObjectTypeAndLength(string objectType)
    {
        Span<byte> buffer = stackalloc byte[128];
        this.Read(buffer.Slice(0, objectType.Length + 1));

        string? actualObjectType = GitRepository.GetString(buffer.Slice(0, objectType.Length));
        this.ObjectType = actualObjectType;

        int headerLength = 0;
        long length = 0;

        while (headerLength < buffer.Length)
        {
            this.Read(buffer.Slice(headerLength, 1));

            if (buffer[headerLength] == 0)
            {
                break;
            }

            // Direct conversion from ASCII to int
            length = (10 * length) + (buffer[headerLength] - (byte)'0');

            headerLength += 1;
        }

        this.Initialize(length);
    }
}
