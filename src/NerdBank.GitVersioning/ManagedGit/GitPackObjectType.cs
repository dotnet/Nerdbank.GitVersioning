// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Nerdbank.GitVersioning.ManagedGit;

internal enum GitPackObjectType
{
    /// <summary>
    /// Invalid.
    /// </summary>
    Invalid = 0,

    /// <summary>
    /// A commit.
    /// </summary>
    OBJ_COMMIT = 1,

    /// <summary>
    /// A tree.
    /// </summary>
    OBJ_TREE = 2,

    /// <summary>
    /// A blob.
    /// </summary>
    OBJ_BLOB = 3,

    /// <summary>
    /// A tag.
    /// </summary>
    OBJ_TAG = 4,

    /// <summary>
    /// An OFS_DELTA.
    /// </summary>
    OBJ_OFS_DELTA = 6,

    /// <summary>
    /// A REF_DELTA.
    /// </summary>
    OBJ_REF_DELTA = 7,
}
