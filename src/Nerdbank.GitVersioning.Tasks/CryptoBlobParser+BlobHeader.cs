// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace Nerdbank.GitVersioning.Tasks
{
    /// <contents>
    /// The <see cref="BlobHeader"/> struct.
    /// </contents>
    internal partial class CryptoBlobParser
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct BlobHeader
        {
            /// <summary>
            /// Blob type.
            /// </summary>
            public byte Type;

            /// <summary>
            /// Blob format version.
            /// </summary>
            public byte Version;

            /// <summary>
            /// Must be 0.
            /// </summary>
            public ushort Reserved;

            /// <summary>
            /// Algorithm ID. Must be one of ALG_ID specified in wincrypto.h.
            /// </summary>
            public uint AlgId;
        }
    }
}
