// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Validation;

namespace Nerdbank.GitVersioning;

internal class NoGitVersionFile : VersionFile
{
    public NoGitVersionFile(GitContext context)
        : base(context)
    {
    }

    /// <inheritdoc/>
    protected override VersionOptions GetVersionCore(out string actualDirectory) => throw Assumes.NotReachable();
}
