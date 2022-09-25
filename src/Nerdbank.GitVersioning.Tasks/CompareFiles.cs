namespace Nerdbank.GitVersioning.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.MemoryMappedFiles;
    using System.Linq;
    using System.Text;
    using Microsoft.Build.Framework;
    using Microsoft.Build.Utilities;

    public class CompareFiles : Task
    {
        /// <summary>
        /// One set of items to compare.
        /// </summary>
        [Required]
        public ITaskItem[] OriginalItems { get; set; }

        /// <summary>
        /// The other set of items to compare.
        /// </summary>
        [Required]
        public ITaskItem[] NewItems { get; set; }

        /// <summary>
        /// Gets whether the items lists contain items that are identical going down the list.
        /// </summary>
        [Output]
        public bool AreSame { get; private set; }

        /// <summary>
        /// Same as <see cref="AreSame"/>, but opposite.
        /// </summary>
        [Output]
        public bool AreChanged { get { return !this.AreSame; } }

        public override bool Execute()
        {
            this.AreSame = this.AreFilesIdentical();
            return true;
        }

        private bool AreFilesIdentical()
        {
            if (this.OriginalItems.Length != this.NewItems.Length)
            {
                return false;
            }

            for (int i = 0; i < this.OriginalItems.Length; i++)
            {
                if (!this.IsContentOfFilesTheSame(this.OriginalItems[i].ItemSpec, this.NewItems[i].ItemSpec))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsContentOfFilesTheSame(string file1, string file2)
        {
            // If exactly one file is missing, that's different.
            if (File.Exists(file1) ^ File.Exists(file2)) return false;
            // If both are missing, that's the same.
            if (!File.Exists(file1)) return true;

            if (new FileInfo(file1).Length != new FileInfo(file2).Length) return false;

            // If both are present, we need to do a content comparison.
            // Keep our comparison simple by loading both in memory.
            byte[] file1Content = File.ReadAllBytes(file1);
            byte[] file2Content = File.ReadAllBytes(file2);

            // One more sanity check.
            if (file1Content.Length != file2Content.Length) return false;

            for (int i = 0; i < file1Content.Length; i++)
            {
                if (file1Content[i] != file2Content[i]) return false;
            }

            return true;
        }
    }
}
