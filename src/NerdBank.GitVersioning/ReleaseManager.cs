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
            /// The current branch is already a release branch
            /// </summary>
            OnReleaseBranch
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
        //TODO: prerelease tag parameter
        public static void PrepareRelease(string projectDirectory, TextWriter stdout = null, TextWriter stderr = null)
        {
            stdout = stdout ?? Console.Out;
            stderr = stderr ?? Console.Error;
            
            // get the current version
            var currentVersionOptions = VersionFile.GetVersion(projectDirectory);

            // open the git repository
            var repository = GitExtensions.OpenGitRepo(projectDirectory);
            if(repository == null)
            {
                stderr.WriteLine($"No git repository found above directory '{projectDirectory}'");
                throw new ReleasePreparationException(ReleasePreparationError.NoGitRepo);
            }
            
            // abort if there are any pending changes
            if(repository.RetrieveStatus().IsDirty)
            {
                stderr.WriteLine($"Uncommitted changes in directory '{projectDirectory}'");
                throw new ReleasePreparationException(ReleasePreparationError.UncommittedChanges);
            }

            var releaseBranchName = GetReleaseBranchName(currentVersionOptions);
            var currentBranch = repository.Branches.Single(x => x.IsCurrentRepositoryHead);

            // check if the current branch is the release branch
            if (currentBranch.FriendlyName.Equals(releaseBranchName, StringComparison.OrdinalIgnoreCase))
            {
                //TODO: only update the version. For now, this is an error
                throw new ReleasePreparationException(ReleasePreparationError.OnReleaseBranch);
            }

            // create release branch
            
            var releaseBranch = repository.CreateBranch(releaseBranchName);

            // update version in release branch    
            var releaseVersion = VersionFile.GetVersion(projectDirectory);
            releaseVersion.Version = new SemanticVersion(releaseVersion.Version.Version);
            Commands.Checkout(repository, releaseBranch);
            var filePath = VersionFile.SetVersion(projectDirectory, releaseVersion);
            Commands.Stage(repository, filePath);

            var signature = repository.Config.BuildSignature(DateTimeOffset.Now);
            if(signature == null)
            {
                //TODO
            }
            repository.Commit($"Create release branch for version '{releaseVersion.Version}'", signature, signature);

            // switch back to previous branch
            Commands.Checkout(repository, currentBranch);

            // edit version on current branch
            var newVersion = VersionFile.GetVersion(projectDirectory);
            newVersion.Version = newVersion
                .Version
                .Increment(currentVersionOptions.ReleaseOrDefault.VersionIncrementOrDefault)
                .SetFirstPrereleaseTag(currentVersionOptions.ReleaseOrDefault.FirstUnstableTagOrDefault);

            filePath = VersionFile.SetVersion(projectDirectory, newVersion);
            Commands.Stage(repository, filePath);
            repository.Commit($"Set version to {newVersion.Version}", signature, signature);

            // Merge release branch back to initial branch
            var mergeResult = repository.Merge(
                releaseBranch, 
                signature, 
                new MergeOptions()
                {
                    CommitOnSuccess = true,
                    MergeFileFavor = MergeFileFavor.Ours
                });
        }


        private static string GetReleaseBranchName(VersionOptions versionOptions)
        {
            //TODO: Check format
            return String.Format(versionOptions.ReleaseOrDefault.BranchNameOrDefault, versionOptions.Version.Version);                        
        }

    }
}
