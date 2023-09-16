// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System.Diagnostics;

namespace Nerdbank.GitVersioning;

[DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
internal class NoGitContext : GitContext
{
    private const string NotAGitRepoMessage = "Not a git repo";

    public NoGitContext(string workingTreePath)
        : base(workingTreePath, null)
    {
        this.VersionFile = new NoGitVersionFile(this);
    }

    /// <inheritdoc/>
    public override VersionFile VersionFile { get; }

    /// <inheritdoc/>
    public override string? GitCommitId => null;

    /// <inheritdoc/>
    public override bool IsHead => false;

    /// <inheritdoc/>
    public override DateTimeOffset? GitCommitDate => null;

    /// <inheritdoc/>
    public override string? HeadCanonicalName => null;

    /// <inheritdoc/>
    public override IReadOnlyCollection<string>? HeadTags => null;

    private string DebuggerDisplay => $"\"{this.WorkingTreePath}\" (no-git)";

    /// <inheritdoc/>
    public override void ApplyTag(string name) => throw new InvalidOperationException(NotAGitRepoMessage);

    /// <inheritdoc/>
    public override void Stage(string path) => throw new InvalidOperationException(NotAGitRepoMessage);

    /// <inheritdoc/>
    public override string GetShortUniqueCommitId(int minLength) => throw new InvalidOperationException(NotAGitRepoMessage);

    /// <inheritdoc/>
    public override bool TrySelectCommit(string committish) => throw new InvalidOperationException(NotAGitRepoMessage);

    /// <inheritdoc/>
    internal override int CalculateVersionHeight(VersionOptions? committedVersion, VersionOptions? workingVersion) => 0;

    /// <inheritdoc/>
    internal override Version GetIdAsVersion(VersionOptions? committedVersion, VersionOptions? workingVersion, int versionHeight) => throw new NotImplementedException();
}
