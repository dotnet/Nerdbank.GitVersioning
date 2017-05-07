namespace Nerdbank.GitVersioning.Tasks
{
    using System.Runtime.InteropServices;

    /// <contents>
    /// The <see cref="BlobHeader"/> struct.
    /// </contents>
    partial class CryptoBlobParser
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct BlobHeader
        {
            /// <summary>
            /// Blob type.
            /// </summary>
            public byte Type;

            /// <summary>
            /// Blob format version
            /// </summary>
            public byte Version;

            /// <summary>
            /// Must be 0
            /// </summary>
            public ushort Reserved;

            /// <summary>
            /// Algorithm ID. Must be one of ALG_ID specified in wincrypto.h
            /// </summary>
            public uint AlgId;
        }
    }
}
