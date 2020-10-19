using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NerdBank.GitVersioning.Managed
{
    internal class WalkingVersionResolver : VersionResolver
    {
        // A list of trees which lead to a version.json file which is not semantically different
        // than the current version.json file.
        private readonly List<GitObjectId> knownTreeIds = new List<GitObjectId>();

        // A list of all commits and their known git heights
        private readonly Dictionary<GitObjectId, int> knownGitHeights = new Dictionary<GitObjectId, int>();

        public WalkingVersionResolver(GitRepository gitRepository, string versionPath)
            : base(gitRepository, versionPath)
        {
        }

        public override int GetGitHeight()
        {
            // Get the commit at which the version number changed, and calculate the git height
            // this.logger.LogInformation("Determining the version based on '{versionPath}' in repository '{repositoryPath}'", this.versionPath, this.gitRepository.GitDirectory);
            Debug.WriteLine($"Determining the version based on '{this.versionPath}' in repository '{this.gitRepository.GitDirectory}'");

            var version = VersionFile.GetVersion(Path.Combine(this.gitRepository.WorkingDirectory, this.versionPath));
            // this.logger.LogInformation("The current version is '{version}'", version);
            Debug.WriteLine($"The current version is '{version}'");

            var pathComponents = GetPathComponents(this.versionPath);
            var headCommit = this.gitRepository.GetHeadCommit().Value;
            var commit = headCommit;

            Stack<GitCommit> commitsToAnalyze = new Stack<GitCommit>();
            commitsToAnalyze.Push(commit);

            while (commitsToAnalyze.Count > 0)
            {
                // Analyze the current commit
                // this.logger.LogDebug("Analyzing commit '{sha}'. '{commitCount}' commits to analyze.", commit.Sha, commitsToAnalyze.Count);
                Debug.WriteLine($"Analyzing commit '{commit.Sha}'. '{commitsToAnalyze.Count}' commits to analyze.");

                commit = commitsToAnalyze.Peek();

                if (this.knownGitHeights.ContainsKey(commit.Sha))
                {
                    // The same commit can be pushed to the stack if two other commits had the same parent.
                    commitsToAnalyze.Pop();
                    continue;
                }

                // If this commit has a version.json file which is semantically different from the current version.json, the git height
                // of this commit is 1.
                var treeId = commit.Tree;

                bool versionChanged = false;

                for (int i = 0; i <= pathComponents.Length; i++)
                {
                    if (treeId == GitObjectId.Empty)
                    {
                        // A version.json file was added in this revision
                        // this.logger.LogDebug("The component '{pathComponent}' could not be found in this commit. Assuming the version.json file was not present.", i == pathComponents.Length ? Array.Empty<byte>() : pathComponents[i]);
                        Debug.WriteLine($"The component '{(i == pathComponents.Length ? Array.Empty<byte>() : pathComponents[i])}' could not be found in this commit. Assuming the version.json file was not present.");
                        versionChanged = true;
                        break;
                    }

                    if (this.knownTreeIds.Contains(treeId))
                    {
                        // Nothing changed, no need to recurse.
                        // this.logger.LogDebug("The tree ID did not change in this commit. Not inspecting the contents of the tree.");
                        Debug.WriteLine("The tree ID did not change in this commit. Not inspecting the contents of the tree.");
                        break;
                    }

                    this.knownTreeIds.Add(treeId);

                    if (i == pathComponents.Length)
                    {
                        // Read the updated version information
                        using (Stream versionStream = this.gitRepository.GetObjectBySha(treeId, "blob"))
                        {
                            var currentVersion = VersionFile.GetVersion(versionStream);
                            // this.logger.LogDebug("The version for this commit is '{version}'", currentVersion);
                            Debug.WriteLine($"The version for this commit is '{currentVersion}'");

                            versionChanged = currentVersion != version;
                            if (versionChanged)
                            {
                                // this.logger.LogInformation("The version number changed from '{version}' to '{currentVersion}' in commit '{commit}'. Using this commit as the baseline.", version, currentVersion, commit.Sha);
                                Debug.WriteLine($"The version number changed from '{version}' to '{currentVersion}' in commit '{commit.Sha}'. Using this commit as the baseline.");
                            }
                        }
                    }
                    else
                    {
                        treeId = this.gitRepository.GetTreeEntry(treeId, pathComponents[i]);
                        // this.logger.LogDebug("The tree ID for '{pathComponent}' is '{treeId}'", pathComponents[i], treeId);
                        Debug.WriteLine($"The tree ID for '{Encoding.UTF8.GetString(pathComponents[i])}' is '{treeId}'");
                    }
                }

                if (versionChanged)
                {
                    // We detected a version change. Because we're walking _backwards_, this means that
                    // the version actually changed a child of this commit.
                    // Assign this commit git height 0; this will cause the child to end up with
                    // git height 1.
                    this.knownGitHeights.Add(commit.Sha, 0);
                    var poppedCommit = commitsToAnalyze.Pop();

                    Debug.Assert(poppedCommit == commit);
                }
                else if (commit.Parents.Count == 0)
                {
                    // This is the first commit in the repository. This commit has git height 1 by definition.
                    this.knownGitHeights.Add(commit.Sha, 1);
                    var poppedCommit = commitsToAnalyze.Pop();

                    Debug.Assert(poppedCommit == commit);
                }
                else
                {
                    bool hasParentWithUnknownGitHeight = false;
                    int currentHeight = -1;

                    foreach (var parent in commit.Parents)
                    {
                        if (this.knownGitHeights.ContainsKey(parent))
                        {
                            var parentHeight = this.knownGitHeights[parent];
                            if (parentHeight > currentHeight)
                            {
                                currentHeight = parentHeight + 1;
                            }
                        }
                        else
                        {
                            commitsToAnalyze.Push(this.gitRepository.GetCommit(parent));
                            hasParentWithUnknownGitHeight = true;
                        }
                    }

                    if (!hasParentWithUnknownGitHeight)
                    {
                        // The current height of this commit is exact.
                        this.knownGitHeights.Add(commit.Sha, currentHeight);
                        var poppedCommit = commitsToAnalyze.Pop();

                        Debug.Assert(poppedCommit == commit);
                    }
                }
            }

            var gitHeight = this.knownGitHeights[headCommit.Sha];
            return gitHeight;
        }
    }
}
