#nullable enable

using System;
using System.IO;

namespace NerdBank.GitVersioning.Managed
{
    internal class GitReferenceReader
    {
        private readonly static byte[] RefPrefix = GitRepository.Encoding.GetBytes("ref: ");

        public static object ReadReference(Stream stream)
        {
            if (stream.Length == 41)
            {
                Span<byte> objectId = stackalloc byte[40];
                stream.Read(objectId);

                return GitObjectId.ParseHex(objectId);
            }
            else
            {
                Span<byte> prefix = stackalloc byte[RefPrefix.Length];
                stream.Read(prefix);

                if (!prefix.SequenceEqual(RefPrefix))
                {
                    throw new GitException();
                }

                // Skip the terminating \n character
                Span<byte> reference = stackalloc byte[(int)stream.Length - RefPrefix.Length - 1];
                stream.Read(reference);

                return GitRepository.GetString(reference);
            }
        }
    }
}
