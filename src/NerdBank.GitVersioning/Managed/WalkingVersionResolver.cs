using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Nerdbank.GitVersioning;

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

        public override int GetGitHeight(Func<GitCommit, bool> continueStepping)
        {
            // Get the commit at which the version number changed, and calculate the git height
            // this.logger.LogInformation("Determining the version based on '{versionPath}' in repository '{repositoryPath}'", this.versionPath, this.gitRepository.GitDirectory);
            Debug.WriteLine($"Determining the version based on '{this.versionPath}' in repository '{this.gitRepository.GitDirectory}'");

            var version = this.versionPath != null ? VersionFile.GetVersion(Path.Combine(this.gitRepository.WorkingDirectory, this.versionPath)) : null;
            version = version ?? "0.0";
            var semanticVersion = SemanticVersion.Parse(version);
            var versionOptions = this.versionPath != null ? VersionFile.TryReadVersion(Path.Combine(this.gitRepository.WorkingDirectory, this.versionPath), Path.GetDirectoryName(this.versionPath)) : null;
            bool hasPathFilters =
                versionOptions?.PathFilters != null
                && versionOptions.PathFilters.Count > 0
                // If there are no include paths, then do not
                // filter the diff at all.
                && versionOptions.PathFilters.Any(p => p.IsInclude);

            // this.logger.LogInformation("The current version is '{version}'", version);
            Debug.WriteLine($"The current version is '{version}'");

            var pathComponents = GetPathComponents(this.versionPath);
            var maybeHeadCommit = this.gitRepository.GetHeadCommit();

            if (maybeHeadCommit == null)
            {
                return 0;
            }

            var headCommit = maybeHeadCommit.Value;
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

                for (int i = 0; i <= (pathComponents.Length == 0 ? -1 : pathComponents.Length); i++)
                {
                    if (treeId == GitObjectId.Empty && version != null)
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
                            var currentVersion = VersionFile.GetVersion(versionStream) ?? "0.0";
                            // this.logger.LogDebug("The version for this commit is '{version}'", currentVersion);
                            Debug.WriteLine($"The version for this commit is '{currentVersion}'");

                            if (currentVersion != version)
                            {
                                var currentSemanticVersion = SemanticVersion.Parse(currentVersion);

                                if (currentSemanticVersion.VersionHeightPosition != semanticVersion.VersionHeightPosition)
                                {
                                    // If the version height position moved, that's an automatic reset in version height.
                                    versionChanged = true;
                                }
                                else if (semanticVersion.VersionHeightPosition == SemanticVersion.Position.Prerelease)
                                {
                                    // The entire version spec must match exactly.
                                    versionChanged = true;
                                }
                                else
                                {
                                    for (SemanticVersion.Position position = SemanticVersion.Position.Major; position <= semanticVersion.VersionHeightPosition; position++)
                                    {
                                        int currentValue = currentSemanticVersion.ReadVersionPosition(position);
                                        var value = semanticVersion.ReadVersionPosition(position);

                                        if (currentValue != value)
                                        {
                                            versionChanged = true;
                                        }
                                    }
                                }

                                version = currentVersion;
                                semanticVersion = currentSemanticVersion;

                                if (versionChanged)
                                {
                                    // this.logger.LogInformation("The version number changed from '{version}' to '{currentVersion}' in commit '{commit}'. Using this commit as the baseline.", version, currentVersion, commit.Sha);
                                    Debug.WriteLine($"The version number changed from '{version}' to '{currentVersion}' in commit '{commit.Sha}'. Using this commit as the baseline.");
                                }
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
                else
                {
                    bool hasParentWithUnknownGitHeight = false;
                    bool hasParent = false;
                    int currentHeight = -1;

                    foreach (var parent in commit.Parents)
                    {
                        if (this.knownGitHeights.ContainsKey(parent))
                        {
                            var parentHeight = this.knownGitHeights[parent];
                            if (parentHeight > currentHeight)
                            {
                                currentHeight = parentHeight;

                                if (!hasPathFilters || this.IsRelevantCommit(commit, this.gitRepository.GetCommit(parent), versionOptions.PathFilters))
                                {
                                    currentHeight += 1;
                                }
                            }

                            hasParent = true;
                        }
                        else
                        {
                            var parentCommit = this.gitRepository.GetCommit(parent);
                            if (continueStepping == null || continueStepping(parentCommit))
                            {
                                commitsToAnalyze.Push(this.gitRepository.GetCommit(parent));
                                hasParentWithUnknownGitHeight = true;
                                hasParent = true;
                            }
                        }
                    }

                    if (!hasParent)
                    {
                        // This is the first commit in the repository. This commit has git height 1 by definition.
                        this.knownGitHeights.Add(commit.Sha, 1);
                        var poppedCommit = commitsToAnalyze.Pop();

                        Debug.Assert(poppedCommit == commit);
                    }
                    else if (!hasParentWithUnknownGitHeight)
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

        private bool IsRelevantCommit(GitCommit commit, GitCommit parent, IReadOnlyList<FilterPath> filters)
        {
            return this.IsRelevantCommit(
                this.gitRepository.GetTree(commit.Tree),
                this.gitRepository.GetTree(parent.Tree),
                relativePath: string.Empty,
                filters);
        }

        private bool IsRelevantCommit(GitTree tree, GitTree parent, string relativePath, IReadOnlyList<FilterPath> filters)
        {
            // Walk over all child nodes in the current tree. If a child node was found in the parent,
            // remove it, so that after the iteration the parent contains all nodes which have been
            // deleted.
            foreach (var child in tree.Children)
            {
                var entry = child.Value;
                GitTreeEntry parentEntry = null;

                // If the entry is not present in the parent commit, it was added;
                // if the Sha does not match, it was modified.
                if (!parent.Children.TryGetValue(child.Key, out parentEntry)
                    || parentEntry.Sha != child.Value.Sha)
                {
                    // Determine whether the change was relevant.
                    var fullPath = $"{relativePath}{entry.Name}";

                    bool isRelevant =
                        // Either there are no include filters at all (i.e. everything is included), or there's an explicit include filter
                        (!filters.Any(f => f.IsInclude) || filters.Any(f => f.Includes(fullPath, this.gitRepository.IgnoreCase)))
                        // The path is not excluded by any filters
                        && !filters.Any(f => f.Excludes(fullPath, this.gitRepository.IgnoreCase));

                    // If the change was relevant, and the item is a directory, we need to recurse.
                    if (isRelevant && !entry.IsFile)
                    {
                        isRelevant = this.IsRelevantCommit(
                            this.gitRepository.GetTree(entry.Sha),
                            parentEntry == null ? GitTree.Empty : this.gitRepository.GetTree(parentEntry.Sha),
                            $"{fullPath}/",
                            filters);
                    }

                    // Quit as soon as any relevant change has been detected.
                    if (isRelevant)
                    {
                        return true;
                    }
                }

                if (parentEntry != null)
                {
                    parent.Children.Remove(child.Key);
                }
            }

            // Inspect removed entries (i.e. present in parent but not in the current tree)
            foreach (var child in parent.Children)
            {
                // Determine whether the change was relevant.
                var fullPath = Path.Combine(relativePath, child.Key);

                bool isRelevant =
                    filters.Any(f => f.Includes(fullPath, this.gitRepository.IgnoreCase))
                    && !filters.Any(f => f.Excludes(fullPath, this.gitRepository.IgnoreCase));

                if (isRelevant)
                {
                    return true;
                }
            }

            // No relevant changes have been detected
            return false;
        }
    }
}
