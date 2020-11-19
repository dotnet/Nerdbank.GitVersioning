#nullable enable

using System;

namespace Nerdbank.GitVersioning
{
    internal class NoGitContext : GitContext
    {
        private const string NotAGitRepoMessage = "Not a git repo";

        public NoGitContext(string workingTreePath)
            : base(workingTreePath)
        {
        }

        public override VersionFile VersionFile => throw new NotImplementedException();

        public override bool IsRepository => false;

        public override string? GitCommitId => null;

        public override DateTimeOffset? GitCommitDate => null;

        public override string? HeadCanonicalName => null;

        public override void ApplyTag(string name) => throw new InvalidOperationException(NotAGitRepoMessage);
        public override void Stage(string path) => throw new InvalidOperationException(NotAGitRepoMessage);
        public override bool TrySelectCommit(string committish) => throw new InvalidOperationException(NotAGitRepoMessage);
        internal override int CalculateVersionHeight(VersionOptions? committedVersion, VersionOptions? workingVersion) => throw new NotImplementedException();
        internal override Version GetIdAsVersion(VersionOptions? committedVersion, VersionOptions? workingVersion, int versionHeight) => throw new NotImplementedException();
        internal override string GetShortUniqueCommitId(int minLength) => throw new InvalidOperationException(NotAGitRepoMessage);
    }
}
