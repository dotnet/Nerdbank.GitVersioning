namespace NerdBank.GitVersioning.Managed
{
    public class GitTreeEntry
    {
        public string Name { get; set; }
        public bool IsFile { get; set; }
        public GitObjectId Sha { get; set; }

        public override string ToString()
        {
            return this.Name;
        }
    }
}
