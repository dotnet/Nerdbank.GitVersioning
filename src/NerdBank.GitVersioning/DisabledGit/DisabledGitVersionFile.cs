// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

namespace Nerdbank.GitVersioning;

internal class DisabledGitVersionFile : VersionFile
{
    public DisabledGitVersionFile(GitContext context)
        : base(context)
    {
    }

    protected new DisabledGitContext Context => (DisabledGitContext)base.Context;

    protected override VersionOptions? GetVersionCore(out string? actualDirectory)
    {
        actualDirectory = null;
        return null;
    }
}
