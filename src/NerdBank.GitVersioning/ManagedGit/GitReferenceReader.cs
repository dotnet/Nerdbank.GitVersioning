// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Nerdbank.GitVersioning.ManagedGit;

internal class GitReferenceReader
{
    private static readonly byte[] RefPrefix = GitRepository.Encoding.GetBytes("ref: ");

    public static object ReadReference(Stream stream)
    {
        Span<byte> reference = stackalloc byte[(int)stream.Length];
        stream.ReadAll(reference);

        return ReadReference(reference);
    }

    public static object ReadReference(Span<byte> value)
    {
        if (!value.StartsWith(RefPrefix))
        {
            int length = value.Length;
            while (length > 0 && (value[length - 1] == (byte)'\n' || value[length - 1] == (byte)'\r'))
            {
                length--;
            }

            if (length == GitObjectId.Sha1HexLength || length == GitObjectId.Sha256HexLength)
            {
                return GitObjectId.ParseHex(value.Slice(0, length));
            }

            throw new GitException();
        }
        else
        {
            // Skip the terminating newline character(s)
            int length = value.Length;
            while (length > RefPrefix.Length && (value[length - 1] == (byte)'\n' || value[length - 1] == (byte)'\r'))
            {
                length--;
            }

            return GitRepository.GetString(value.Slice(RefPrefix.Length, length - RefPrefix.Length));
        }
    }
}
