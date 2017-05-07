namespace Nerdbank.GitVersioning.Tasks
{
    using System.Runtime.InteropServices;

    /// <contents>
    /// The <see cref="SnPublicKeyBlob"/> struct.
    /// </contents>
    partial class CryptoBlobParser
    {
        /// <summary>
        /// The strong name public key blob binary format.
        /// </summary>
        /// <see cref="https://github.com/dotnet/coreclr/blob/32f0f9721afb584b4a14d69135bea7ddc129f755/src/strongname/inc/strongname.h#L29"/>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private unsafe struct SnPublicKeyBlob
        {
            /// <summary>
            /// Signature algorithm ID
            /// </summary>
            public uint SigAlgId;

            /// <summary>
            /// Hash algorithm ID
            /// </summary>
            public uint HashAlgId;

            /// <summary>
            /// Size of public key data in bytes, not including the header
            /// </summary>
            public uint PublicKeySize;

            /// <summary>
            /// PublicKeySize bytes of public key data
            /// </summary>
            /// <remarks>
            /// Note: PublicKey is variable sized
            /// </remarks>
            public fixed byte PublicKey[1];
        }
    }
}
