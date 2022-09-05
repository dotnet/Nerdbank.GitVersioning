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
        /// <returns><c>true</c> if the files are the same; <c>false</c> if the files are different.</returns>
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

            // If both are present, we need to do a content comparison.
            using (FileStream fileStream1 = File.OpenRead(file1))
            {
                using (FileStream fileStream2 = File.OpenRead(file2))
                {
                    if (fileStream1.Length != fileStream2.Length)
                    {
                        return false;
                    }

                    byte[] buffer1 = new byte[4096];
                    byte[] buffer2 = new byte[buffer1.Length];
                    int bytesRead;
                    do
                    {
                        bytesRead = fileStream1.Read(buffer1, 0, buffer1.Length);
                        if (fileStream2.Read(buffer2, 0, buffer2.Length) != bytesRead)
                        {
                            // We should never get here since we compared file lengths, but
                            // this is a sanity check.
                            return false;
                        }

                        for (int i = 0; i < bytesRead; i++)
                        {
                            if (buffer1[i] != buffer2[i])
                            {
                                return false;
                            }
                        }
                    }
                    while (bytesRead == buffer1.Length);
                }
            }

            return true;
        }
    }
}
