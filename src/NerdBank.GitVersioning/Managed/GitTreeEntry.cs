namespace NerdBank.GitVersioning.Managed
{
    /// <summary>
    /// Represents an individual entry in the Git tree.
    /// </summary>
    public class GitTreeEntry
    {
        /// <summary>
        /// Gets or sets the name of the entry.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the current entry is a file.
        /// </summary>
        public bool IsFile { get; set; }

        /// <summary>
        /// Gets or sets the Git object Id of the blob or tree of the current entry.
        /// </summary>
        public GitObjectId Sha { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return this.Name;
        }
    }
}
