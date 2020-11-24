#nullable enable

using System;
using System.Runtime.Serialization;

namespace Nerdbank.GitVersioning
{
    /// <summary>
    /// The exception which is thrown by the managed Git layer.
    /// </summary>
    [Serializable]
    public class GitException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GitException"/> class.
        /// </summary>
        public GitException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GitException"/> class.
        /// </summary>
        /// <param name="message"><inheritdoc cref="Exception(string)" path="/param"/></param>
        public GitException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GitException"/> with an
        /// error message and an inner message.
        /// </summary>
        /// <param name="message">
        /// A message which describes the error.
        /// </param>
        /// <param name="innerException">
        /// The <see cref="Exception"/> which caused this exception to be thrown.
        /// </param>
        public GitException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GitException"/> class.
        /// </summary>
        protected GitException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            this.ErrorCode = (ErrorCodes)info.GetUInt32(nameof(this.ErrorCode));
            this.iSShallowClone = info.GetBoolean(nameof(this.iSShallowClone));
        }

        /// <summary>
        /// Gets the error code for this exception.
        /// </summary>
        public ErrorCodes ErrorCode { get; set; }

        /// <summary>
        /// Gets a value indicating whether the exception was thrown from a shallow clone.
        /// </summary>
        public bool iSShallowClone { get; set; }

        /// <inheritdoc/>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(this.ErrorCode), (int)this.ErrorCode);
            info.AddValue(nameof(this.iSShallowClone), this.iSShallowClone);
        }

        /// <summary>
        /// Describes specific error conditions that may warrant branching code paths.
        /// </summary>
        public enum ErrorCodes
        {
            /// <summary>
            /// No error code was specified.
            /// </summary>
            Unspecified = 0,

            /// <summary>
            /// An object could not be found.
            /// </summary>
            ObjectNotFound,
        }
    }
}
