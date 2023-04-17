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
        if (value.Length == 41 && !value.StartsWith(RefPrefix))
        {
            // Skip the trailing \n
            return GitObjectId.ParseHex(value.Slice(0, 40));
        }
        else
        {
            if (!value.StartsWith(RefPrefix))
            {
                throw new GitException();
            }

            // Skip the terminating \n character
            return GitRepository.GetString(value.Slice(RefPrefix.Length, value.Length - RefPrefix.Length - 1));
        }
    }
}
