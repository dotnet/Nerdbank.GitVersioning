// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using LibGit2Sharp;
using Nerdbank.GitVersioning;

public partial class RepoTestBase
{
    /// <summary>
    /// Gets the number of commits in the longest single path between
    /// the specified commit and the most distant ancestor (inclusive)
    /// that set the version to the value at <paramref name="committish"/>.
    /// </summary>
    /// <param name="committish">The commit, branch or tag to measure the height of. Leave as null to check HEAD.</param>
    /// <param name="repoRelativeProjectDirectory">The repo-relative project directory for which to calculate the version.</param>
    /// <returns>The height of the commit. Always a positive integer.</returns>
    protected int GetVersionHeight(string? committish, string? repoRelativeProjectDirectory = null) => this.GetVersionOracle(repoRelativeProjectDirectory, committish).VersionHeight;

    /// <summary>
    /// Gets the number of commits in the longest single path between
    /// HEAD in a repo and the most distant ancestor (inclusive)
    /// that set the version to the value in the working copy
    /// (or HEAD for bare repositories).
    /// </summary>
    /// <param name="repoRelativeProjectDirectory">The repo-relative project directory for which to calculate the version.</param>
    /// <returns>The height of the repo at HEAD. Always a positive integer.</returns>
    protected int GetVersionHeight(string? repoRelativeProjectDirectory = null) => this.GetVersionHeight(committish: null, repoRelativeProjectDirectory);

    private class FakeCloudBuild : ICloudBuild
    {
        public FakeCloudBuild(string gitCommitId)
        {
            this.GitCommitId = gitCommitId;
        }

        public bool IsApplicable => true;

        public bool IsPullRequest => false;

        public string? BuildingBranch => null;

        public string? BuildingTag => null;

        public string? GitCommitId { get; private set; }

        public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter? stdout, TextWriter? stderr)
        {
            return new Dictionary<string, string>();
        }

        public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter? stdout, TextWriter? stderr)
        {
            return new Dictionary<string, string>();
        }
    }
}
