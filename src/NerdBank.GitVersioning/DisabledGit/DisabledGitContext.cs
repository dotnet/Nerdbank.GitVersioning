// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Diagnostics;

namespace Nerdbank.GitVersioning;

[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
internal class DisabledGitContext : GitContext
{
    public DisabledGitContext(string workingTreePath)
        : base(workingTreePath, null)
    {
        this.VersionFile = new DisabledGitVersionFile(this);
    }

    public override VersionFile VersionFile { get; }

    public override string? GitCommitId => null;

    public override bool IsHead => false;

    public override DateTimeOffset? GitCommitDate => null;

    public override string? HeadCanonicalName => null;

    public override IReadOnlyCollection<string>? HeadTags => null;

    private string DebuggerDisplay => $"\"{this.WorkingTreePath}\" (disabled-git)";

    public override void ApplyTag(string name) => throw new NotSupportedException();

    public override void Stage(string path) => throw new NotSupportedException();

    public override string GetShortUniqueCommitId(int minLength) => "nerdbankdisabled";

    public override bool TrySelectCommit(string committish) => true;

    internal override int CalculateVersionHeight(VersionOptions? committedVersion, VersionOptions? workingVersion) => 0;

    internal override Version GetIdAsVersion(VersionOptions? committedVersion, VersionOptions? workingVersion, int versionHeight) => Version0;
}
