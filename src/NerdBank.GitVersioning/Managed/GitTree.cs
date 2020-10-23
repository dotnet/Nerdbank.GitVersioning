using System.Collections.Generic;

namespace NerdBank.GitVersioning.Managed
{
    public class GitTree
    {
        public static GitTree Empty { get; } = new GitTree();

        public GitObjectId Sha { get; set; }

        public Dictionary<string, GitTreeEntry> Children { get; } = new Dictionary<string, GitTreeEntry>();

        public override string ToString()
        {
            return $"Git tree: {this.Sha}";
        }
    }
}
