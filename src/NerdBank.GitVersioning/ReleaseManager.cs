namespace Nerdbank.GitVersioning
{
    using System;
    using System.IO;
    using System.Linq;
    using LibGit2Sharp;

    /// <summary>
    /// Methods for creating releases 
    /// </summary>
    public class ReleaseManager
    {
        /// <summary>
        /// Defines the possible errors that can occur when preparing a release
        /// </summary>
        public enum ReleasePreparationError
        {
            /// <summary>
            /// The project directory is not a git repository
            /// </summary>
            NoGitRepo,
            /// <summary>
            /// There are pending changes in the project directory
            /// </summary>
            UncommittedChanges,
            /// <summary>
            /// The "branchName" setting in "version.json" is invalid
            /// </summary>
            InvalidBranchNameSetting,
            /// <summary>
            /// version.json/version.txt not found
            /// </summary>
            NoVersionFile            
        }

        /// <summary>
        /// Exception indicating an error during preparation of a release
        /// </summary>
        public class ReleasePreparationException : Exception
        {
            /// <summary>
            /// Gets the error that occurred.
            /// </summary>
            public ReleasePreparationError Error { get; }

            /// <summary>
            /// Initializes a new instance of <see cref="ReleasePreparationException"/>
            /// </summary>
            /// <param name="error">The error that occurred.</param>
            public ReleasePreparationException(ReleasePreparationError error) => this.Error = error;
        }


        /// <summary>
        /// Prepares a release for the specified directory by creating a release branch and incrementing the version in the current branch.
        /// </summary>
        /// <exception cref="ReleasePreparationException">Thrown when the release could not be created.</exception>
        public static void PrepareRelease(string projectDirectory, string releaseUnstableTag = null, TextWriter stdout = null, TextWriter stderr = null)
        {
            stdout = stdout ?? Console.Out;
            stderr = stderr ?? Console.Error;
            
            // open the git repository
            var repository = GetRepository(projectDirectory, stdout, stderr);
            
            // get the current version
            var versionOptions = VersionFile.GetVersion(projectDirectory);
            if(versionOptions == null)
            {
                stderr.WriteLine($"Failed to load version file for directory '{projectDirectory}'");
                throw new ReleasePreparationException(ReleasePreparationError.NoVersionFile);
            }

            var releaseOptions = versionOptions.ReleaseOrDefault;

            var releaseBranchName = GetReleaseBranchName(versionOptions, stdout, stderr);
            var mainBranchName = repository.Branches.Single(x => x.IsCurrentRepositoryHead);

            // check if the current branch is the release branch
            if (mainBranchName.FriendlyName.Equals(releaseBranchName, StringComparison.OrdinalIgnoreCase))
            {
                UpdateVersion(projectDirectory, repository,
                    version =>
                        string.IsNullOrEmpty(releaseUnstableTag)
                            ? version.WithoutPrepreleaseTags()
                            : version.SetFirstPrereleaseTag(releaseUnstableTag)
                );
                return;
            }

            // create release branch            
            var releaseBranch = repository.CreateBranch(releaseBranchName);

            // update version in release branch    
            Commands.Checkout(repository, releaseBranch);
            UpdateVersion(projectDirectory, repository,
                version =>
                    string.IsNullOrEmpty(releaseUnstableTag)
                        ? version.WithoutPrepreleaseTags()
                        : version.SetFirstPrereleaseTag(releaseUnstableTag)
            );
            
            // update version on main branch
            Commands.Checkout(repository, mainBranchName);
            UpdateVersion(projectDirectory, repository,
                version => version
                    .Increment(releaseOptions.VersionIncrementOrDefault)
                    .SetFirstPrereleaseTag(releaseOptions.FirstUnstableTagOrDefault)
            );
            
            // Merge release branch back to main branch
            var mergeOptions = new MergeOptions()
            {
                CommitOnSuccess = true,
                MergeFileFavor = MergeFileFavor.Ours
            };
            repository.Merge(releaseBranch, GetSignature(repository), mergeOptions);
        }


        private static string GetReleaseBranchName(VersionOptions versionOptions, TextWriter stdout, TextWriter stderr)
        {
            var branchNameFormat = versionOptions.ReleaseOrDefault.BranchNameOrDefault;

            if(string.IsNullOrEmpty(branchNameFormat) || !branchNameFormat.Contains("{0}"))
            {
                stderr.WriteLine($"Invalid 'branchName' setting '{branchNameFormat}'. Missing version placeholder '{{0}}'");
                throw new ReleasePreparationException(ReleasePreparationError.InvalidBranchNameSetting);
            }
            
            return string.Format(branchNameFormat, versionOptions.Version.Version);                        
        }

        private static void UpdateVersion(string projectDirectory, Repository repository, Func<SemanticVersion, SemanticVersion> updateAction)
        {
            var signature = GetSignature(repository);

            var versionOptions = VersionFile.GetVersion(projectDirectory);
            versionOptions.Version = updateAction(versionOptions.Version);
            var filePath = VersionFile.SetVersion(projectDirectory, versionOptions);
            
            Commands.Stage(repository, filePath);
            repository.Commit($"Set version to '{versionOptions.Version}'", signature, signature);
        }

        private static Signature GetSignature(Repository repository)
        {
            var signature = repository.Config.BuildSignature(DateTimeOffset.Now);
            if (signature == null)
            {
                //TODO
            }
            return signature;
        }

        private static Repository GetRepository(string projectDirectory, TextWriter stdput, TextWriter stderr)
        {
            var repository = GitExtensions.OpenGitRepo(projectDirectory);
            if (repository == null)
            {
                stderr.WriteLine($"No git repository found above directory '{projectDirectory}'");
                throw new ReleasePreparationException(ReleasePreparationError.NoGitRepo);
            }

            // abort if there are any pending changes
            if (repository.RetrieveStatus().IsDirty)
            {
                stderr.WriteLine($"Uncommitted changes in directory '{projectDirectory}'");
                throw new ReleasePreparationException(ReleasePreparationError.UncommittedChanges);
            }

            return repository;
        }
    }
}
