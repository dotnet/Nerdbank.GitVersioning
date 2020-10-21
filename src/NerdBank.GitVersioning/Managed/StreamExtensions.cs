using System;
using System.Diagnostics;
using System.IO;

namespace NerdBank.GitVersioning.Managed
{
    /// <summary>
    /// Provides extension methods for the <see cref="Stream"/> class.
    /// </summary>
    public static class StreamExtensions
    {
        /// <summary>
        /// Reads data from a <see cref="Stream"/>, and asserts the amount of data read equals the size of the buffer.
        /// </summary>
        /// <param name="stream">
        /// The <see cref="Stream"/> from which to read data.
        /// </param>
        /// <param name="buffer">
        /// A buffer into which to store the data read.
        /// </param>
        public static void ReadAll(this Stream stream, Span<byte> buffer)
        {
            int read = stream.Read(buffer);
            Debug.Assert(read == buffer.Length);
        }

        /// <summary>
        /// Reads an variable-length integer off a <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">
        /// The stream off which to read the variable-length integer.
        /// </param>
        /// <returns>
        /// The requested value.
        /// </returns>
        public static int ReadMbsInt(this Stream stream)
        {
            int value = 0;
            int currentBit = 0;
            int read;

            while (true)
            {
                read = stream.ReadByte();
                value |= (read & 0b_0111_1111) << currentBit;
                currentBit += 7;

                if (read < 128)
                {
                    break;
                }
            }

            return value;
        }
    }
}
