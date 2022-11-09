// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Nerdbank.GitVersioning.Tasks
{
    public class CompareFiles : Microsoft.Build.Utilities.Task
    {
        /// <summary>
        /// Gets or sets one set of items to compare.
        /// </summary>
        [Required]
        public ITaskItem[] OriginalItems { get; set; }

        /// <summary>
        /// Gets or sets the other set of items to compare.
        /// </summary>
        [Required]
        public ITaskItem[] NewItems { get; set; }

        /// <summary>
        /// Gets a value indicating whether the items lists contain items that are identical going down the list.
        /// </summary>
        [Output]
        public bool AreSame { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the item lists contain items that are <em>not</em> identical, going down the list.
        /// </summary>
        [Output]
        public bool AreChanged
        {
            get { return !this.AreSame; }
        }

        /// <inheritdoc/>
        public override bool Execute()
        {
            this.AreSame = this.AreFilesIdentical();
            return true;
        }

        /// <summary>
        /// Tests whether a file is up to date with respect to another,
        /// based on existence, last write time and file size.
        /// </summary>
        /// <param name="sourcePath">The source path.</param>
        /// <param name="destPath">The dest path.</param>
        /// <returns><see langword="true"/> if the files are the same; <see langword="false"/> if the files are different.</returns>
        internal static bool FastFileEqualityCheck(string sourcePath, string destPath)
        {
            FileInfo sourceInfo = new FileInfo(sourcePath);
            FileInfo destInfo = new FileInfo(destPath);

            if (sourceInfo.Exists ^ destInfo.Exists)
            {
                // Either the source file or the destination file is missing.
                return false;
            }

            if (!sourceInfo.Exists)
            {
                // Neither file exists.
                return true;
            }

            // We'll say the files are the same if their modification date and length are the same.
            return
                sourceInfo.LastWriteTimeUtc == destInfo.LastWriteTimeUtc &&
                sourceInfo.Length == destInfo.Length;
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
            if (File.Exists(file1) ^ File.Exists(file2))
            {
                return false;
            }

            // If both are missing, that's the same.
            if (!File.Exists(file1))
            {
                return true;
            }

            if (new FileInfo(file1).Length != new FileInfo(file2).Length)
            {
                return false;
            }

            // If both are present, we need to do a content comparison.
            // Keep our comparison simple by loading both in memory.
            byte[] file1Content = File.ReadAllBytes(file1);
            byte[] file2Content = File.ReadAllBytes(file2);

            // One more sanity check.
            if (file1Content.Length != file2Content.Length)
            {
                return false;
            }

            for (int i = 0; i < file1Content.Length; i++)
            {
                if (file1Content[i] != file2Content[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
