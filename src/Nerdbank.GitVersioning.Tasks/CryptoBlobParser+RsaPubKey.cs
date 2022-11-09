// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace Nerdbank.GitVersioning.Tasks
{
    internal partial class CryptoBlobParser
    {
        /// <summary>
        /// RSAPUBKEY struct from wincrypt.h.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct RsaPubKey
        {
            /// <summary>
            /// Indicates RSA1 or RSA2.
            /// </summary>
            public uint Magic;

            /// <summary>
            /// Number of bits in the modulus. Must be multiple of 8.
            /// </summary>
            public uint BitLen;

            /// <summary>
            /// The public exponent.
            /// </summary>
            public uint PubExp;
        }
    }
}
