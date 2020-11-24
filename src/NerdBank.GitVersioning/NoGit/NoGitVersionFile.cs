namespace Nerdbank.GitVersioning
{
    using Validation;

    internal class NoGitVersionFile : VersionFile
    {
        public NoGitVersionFile(GitContext context)
            : base(context)
        {
        }

        protected override VersionOptions GetVersionCore(out string actualDirectory) => throw Assumes.NotReachable();
    }
}
