using System;
using System.Diagnostics;
using System.IO;

namespace NerdBank.GitVersioning.Managed
{
    internal static class StreamExtensions
    {
        public static void ReadAll(this Stream stream, Span<byte> buffer)
        {
            int read = stream.Read(buffer);
            Debug.Assert(read == buffer.Length);
        }
    }
}
