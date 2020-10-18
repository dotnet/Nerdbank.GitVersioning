using System;
using System.IO;

namespace NerdBank.GitVersioning.Managed
{
    internal class GitReferenceReader
    {
        private readonly static byte[] RefPrefix = GitRepository.Encoding.GetBytes("ref: ");

        public static string ReadReference(Stream stream)
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

            return GitRepository.Encoding.GetString(reference);
        }
    }
}
