using System;
using System.Collections.Generic;
using System.Text;

namespace Nerdbank.GitVersioning.ManagedGit
{
    /// <summary>
    /// Base class for classes which support reading data stored in a Git Pack file.
    /// </summary>
    /// <seealso href="https://git-scm.com/docs/pack-format"/>
    public abstract class GitPackIndexReader : IDisposable
    {
        /// <summary>
        /// The header of the index file.
        /// </summary>
        protected static readonly byte[] Header = new byte[] { 0xff, 0x74, 0x4f, 0x63 };

        /// <summary>
        /// Gets the offset of a Git object in the index file.
        /// </summary>
        /// <param name="objectId">
        /// The Git object Id of the Git object for which to get the offset.
        /// </param>
        /// <returns>
        /// If found, the offset of the Git object in the index file; otherwise,
        /// <see langword="null"/>.
        /// </returns>
        public abstract int? GetOffset(GitObjectId objectId);

        /// <inheritdoc/>
        public abstract void Dispose();
    }
}