using System;

namespace NerdBank.GitVersioning.Managed
{
    /// <summary>
    /// The exception which is thrown by the managed Git layer.
    /// </summary>
    public class GitException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GitException"/> class.
        /// </summary>
        public GitException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GitException"/> with an error
        /// message.
        /// </summary>
        /// <param name="message">
        /// A message which describes the error.
        /// </param>
        public GitException(string message) : base(message)
        {
        }
    }
}
