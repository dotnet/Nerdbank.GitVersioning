#nullable enable

using System;
using System.Diagnostics;

namespace Nerdbank.GitVersioning
{
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + ",nq}")]
    internal class NoGitContext : GitContext
    {
        private const string NotAGitRepoMessage = "Not a git repo";

        public NoGitContext(string workingTreePath)
            : base(workingTreePath, null)
        {
            this.VersionFile = new NoGitVersionFile(this);
        }

        public override VersionFile VersionFile { get; }

        public override string? GitCommitId => null;

        public override bool IsHead => false;

        public override DateTimeOffset? GitCommitDate => null;

        public override string? HeadCanonicalName => null;

        private string DebuggerDisplay => $"\"{this.WorkingTreePath}\" (no-git)";

        public override void ApplyTag(string name) => throw new InvalidOperationException(NotAGitRepoMessage);
        public override void Stage(string path) => throw new InvalidOperationException(NotAGitRepoMessage);
        public override string GetShortUniqueCommitId(int minLength) => throw new InvalidOperationException(NotAGitRepoMessage);
        public override bool TrySelectCommit(string committish) => throw new InvalidOperationException(NotAGitRepoMessage);
        internal override int CalculateVersionHeight(VersionOptions? committedVersion, VersionOptions? workingVersion) => 0;
        internal override Version GetIdAsVersion(VersionOptions? committedVersion, VersionOptions? workingVersion, int versionHeight) => throw new NotImplementedException();
    }
}
