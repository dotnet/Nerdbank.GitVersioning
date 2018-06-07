// Liberally copied from (with slight modifications) https://github.com/dotnet/roslyn/blob/6181abfdf59da26da27f0dbedae2978df2f83768/src/Compilers/Core/Portable/StrongName/CryptoBlobParser.cs
// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See https://github.com/dotnet/roslyn/blob/6181abfdf59da26da27f0dbedae2978df2f83768/License.txt for license information.

namespace Nerdbank.GitVersioning.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Security.Cryptography;
    using System.Text;
    using Validation;

    internal static partial class CryptoBlobParser
    {
        /// <summary>
        /// The size of a public key token, in bytes.
        /// </summary>
        private const int SN_SIZEOF_TOKEN = 8;

        /// <summary>
        /// The length of a SHA1 hash, in bytes.
        /// </summary>
        private const int SHA1_HASH_SIZE = 20;

        private const UInt32 RSA1 = 0x31415352;

        // From wincrypt.h
        private const byte PublicKeyBlobId = 0x06;
        private const byte PrivateKeyBlobId = 0x07;

        // In wincrypt.h both public and private key blobs start with a
        // PUBLICKEYSTRUC and RSAPUBKEY and then start the key data
        private unsafe static readonly int s_offsetToKeyData = sizeof(BlobHeader) + sizeof(RsaPubKey);

        /// <summary>
        /// Derives the public key token from a full public key.
        /// </summary>
        /// <param name="publicKey">The public key.</param>
        /// <returns>The public key token.</returns>
        /// <remarks>
        /// Heavily inspired by https://github.com/dotnet/coreclr/blob/2efbb9282c059eb9742ba5a59b8a1d52ac4dfa4c/src/strongname/api/strongname.cpp#L270
        /// </remarks>
        internal static unsafe byte[] GetStrongNameTokenFromPublicKey(byte[] publicKey)
        {
            Requires.NotNull(publicKey, nameof(publicKey));

            byte[] strongNameToken = new byte[SN_SIZEOF_TOKEN];
            fixed (byte* publicKeyPtr = publicKey)
            {
                //var publicKeyBlob = (SnPublicKeyBlob*)publicKeyPtr;
                using (var sha1 = SHA1.Create())
                {
                    byte[] hash = sha1.ComputeHash(publicKey);
                    int hashLenMinusTokenSize = SHA1_HASH_SIZE - SN_SIZEOF_TOKEN;

                    // Take the last few bytes of the hash value for our token. (These are the
                    // low order bytes from a network byte order point of view). Reverse the
                    // order of these bytes in the output buffer to get host byte order.
                    for (int i = 0; i < SN_SIZEOF_TOKEN; i++)
                    {
                        strongNameToken[SN_SIZEOF_TOKEN - (i + 1)] = hash[i + hashLenMinusTokenSize];
                    }
                }
            }

            return strongNameToken;
        }

        private static byte[] CreateSnPublicKeyBlob(BlobHeader header, RsaPubKey rsa, byte[] pubKeyData)
        {
            var snPubKey = new SnPublicKeyBlob()
            {
                SigAlgId = AlgorithmId.RsaSign,
                HashAlgId = AlgorithmId.Sha,
                PublicKeySize = (UInt32)(s_offsetToKeyData + pubKeyData.Length)
            };

            using (var ms = new MemoryStream(160))
            using (var binaryWriter = new BinaryWriter(ms))
            {
                binaryWriter.Write(snPubKey.SigAlgId);
                binaryWriter.Write(snPubKey.HashAlgId);
                binaryWriter.Write(snPubKey.PublicKeySize);

                binaryWriter.Write(header.Type);
                binaryWriter.Write(header.Version);
                binaryWriter.Write(header.Reserved);
                binaryWriter.Write(header.AlgId);

                binaryWriter.Write(rsa.Magic);
                binaryWriter.Write(rsa.BitLen);
                binaryWriter.Write(rsa.PubExp);

                binaryWriter.Write(pubKeyData);

                return ms.ToArray();
            }
        }

        internal unsafe static bool TryGetPublicKeyFromPrivateKeyBlob(byte[] blob, out byte[] publicKey)
        {
            fixed (byte* blobPtr = blob)
            {
                var header = (BlobHeader*)blobPtr;
                var rsa = (RsaPubKey*)(blobPtr + sizeof(BlobHeader));

                var version = header->Version;
                var modulusBitLength = rsa->BitLen;
                var exponent = rsa->PubExp;
                var modulus = new byte[modulusBitLength >> 3];

                if (blob.Length - s_offsetToKeyData < modulus.Length)
                {
                    publicKey = null;
                    return false;
                }

                Marshal.Copy((IntPtr)(blobPtr + s_offsetToKeyData), modulus, 0, modulus.Length);

                var newHeader = new BlobHeader()
                {
                    Type = PublicKeyBlobId,
                    Version = version,
                    Reserved = 0,
                    AlgId = AlgorithmId.RsaSign
                };

                var newRsaKey = new RsaPubKey()
                {
                    Magic = RSA1, // Public key
                    BitLen = modulusBitLength,
                    PubExp = exponent
                };

                publicKey = CreateSnPublicKeyBlob(newHeader, newRsaKey, modulus);
                return true;
            }
        }

        private struct AlgorithmId
        {
            // From wincrypt.h
            private const int AlgorithmClassOffset = 13;
            private const int AlgorithmClassMask = 0x7;
            private const int AlgorithmSubIdOffset = 0;
            private const int AlgorithmSubIdMask = 0x1ff;

            private readonly uint _flags;

            public const int RsaSign = 0x00002400;
            public const int Sha = 0x00008004;

            public bool IsSet
            {
                get { return _flags != 0; }
            }

            public AlgorithmId(uint flags)
            {
                _flags = flags;
            }
        }
    }
}
