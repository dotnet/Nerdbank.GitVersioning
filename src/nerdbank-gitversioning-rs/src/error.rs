// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/// An error produced while reading a repository or calculating a version.
#[derive(Debug, thiserror::Error)]
pub enum Error {
    /// A version or configuration value has an invalid format.
    #[error("{0}")]
    InvalidFormat(String),

    /// An operation cannot be completed in the current state.
    #[error("{0}")]
    InvalidOperation(String),

    /// An I/O operation failed.
    #[error(transparent)]
    Io(#[from] std::io::Error),

    /// A Git operation failed.
    #[error(transparent)]
    Git(#[from] git2::Error),

    /// JSON serialization or deserialization failed.
    #[error(transparent)]
    Json(#[from] serde_json::Error),
}

/// A result returned by this crate.
pub type Result<T> = std::result::Result<T, Error>;
