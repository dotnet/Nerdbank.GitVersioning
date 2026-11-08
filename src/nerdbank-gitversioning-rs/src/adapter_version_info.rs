// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

use chrono::{SecondsFormat, Utc};
use serde::{Deserialize, Serialize};

use crate::VersionOracle;

/// Versioned, stable information for ecosystem adapters.
#[derive(Clone, Debug, Deserialize, Eq, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct AdapterVersionInfo {
    /// Version of this JSON contract.
    pub schema_version: u32,
    /// Complete SemVer 2.0 version.
    pub sem_ver2: String,
    /// Numeric version.
    pub version: String,
    /// Calculated version height, before any configured offset.
    pub version_height: u32,
    /// Full commit ID, when available.
    pub git_commit_id: Option<String>,
    /// Abbreviated commit ID, when available.
    pub git_commit_id_short: Option<String>,
    /// UTC commit date at second precision, when available.
    pub git_commit_date: Option<String>,
    /// Whether this is a public release.
    pub public_release: bool,
}

impl From<&VersionOracle> for AdapterVersionInfo {
    fn from(oracle: &VersionOracle) -> Self {
        Self {
            schema_version: 1,
            sem_ver2: oracle.sem_ver2(),
            version: oracle.version.to_string(),
            version_height: oracle.version_height,
            git_commit_id: oracle.git_commit_id.clone(),
            git_commit_id_short: oracle.git_commit_id_short.clone(),
            git_commit_date: oracle.git_commit_date().map(|date| {
                date.with_timezone(&Utc)
                    .to_rfc3339_opts(SecondsFormat::Secs, true)
            }),
            public_release: oracle.public_release,
        }
    }
}
