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
            NoVersionFile,
            /// <summary>
            /// Updating the version would result in a version lower than the previous version
            /// </summary>
            VersionDecrement,
            /// <summary>
            /// Cannot create a branch because it already exists
            /// </summary>
            BranchAlreadyExists
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
        

        private readonly TextWriter stdout;
        private readonly TextWriter stderr;

        /// <summary>
        /// Initializes a new instance of <see cref="ReleaseManager"/>
        /// </summary>
        /// <param name="stdout">The <see cref="TextWriter"/> to write output to.</param>
        /// <param name="stderr">The <see cref="TextWriter"/> to write error messages to to.</param>
        public ReleaseManager(TextWriter stdout = null, TextWriter stderr = null)
        {
            this.stdout = stdout ?? Console.Out;
            this.stderr = stderr ?? Console.Error;
        }


        //TODO: Parameter to explicitly specify the next version
        /// <summary>
        /// Prepares a release for the specified directory by creating a release branch and incrementing the version in the current branch.
        /// </summary>
        /// <exception cref="ReleasePreparationException">Thrown when the release could not be created.</exception>
        /// <param name="projectDirectory">
        /// The path to the directory which may (or its ancestors may) define the version file.
        /// </param>
        /// <param name="releaseUnstableTag">
        /// The prerelease tag to add to the version on the release branch. Pass <c>null</c> to omit/remove the prerelease tag.
        /// </param>
        /// <param name="nextVersion">
        /// The next version to save to the version file on the current branch. Pass <c>null</c> to automatically determine the next
        /// version based on the current version and the <c>versionIncrement</c> setting in <c>version.json</c>.
        /// Parameter will be ignored if the current branch is a release branch.
        /// </param>
        public void PrepareRelease(string projectDirectory, string releaseUnstableTag = null, SemanticVersion nextVersion = null)
        {
            // open the git repository
            var repository = this.GetRepository(projectDirectory);
            
            // get the current version
            var versionOptions = VersionFile.GetVersion(projectDirectory);
            if(versionOptions == null)
            {
                this.stderr.WriteLine($"Failed to load version file for directory '{projectDirectory}'");
                throw new ReleasePreparationException(ReleasePreparationError.NoVersionFile);
            }

            var releaseOptions = versionOptions.ReleaseOrDefault;

            var releaseBranchName = this.GetReleaseBranchName(versionOptions);
            var mainBranchName = repository.Branches.Single(x => x.IsCurrentRepositoryHead).FriendlyName;

            // check if the current branch is the release branch
            if (mainBranchName.Equals(releaseBranchName, StringComparison.OrdinalIgnoreCase))
            {
                this.stdout.WriteLine($"Current branch '{releaseBranchName}' is a release branch. Updating version");
                this.UpdateVersion(projectDirectory, repository,
                    version =>
                        string.IsNullOrEmpty(releaseUnstableTag)
                            ? version.WithoutPrepreleaseTags()
                            : version.SetFirstPrereleaseTag(releaseUnstableTag));
                return;
            }

            // check if the release branch already exists
            if(repository.Branches[releaseBranchName] != null)
            {
                this.stderr.WriteLine($"Cannot create branch '{releaseBranchName}' because it already exists");
                throw new ReleasePreparationException(ReleasePreparationError.BranchAlreadyExists);
            }

            // create release branch and update version 
            this.stdout.WriteLine($"Creating release branch '{releaseBranchName}'");
            var releaseBranch = repository.CreateBranch(releaseBranchName);
            Commands.Checkout(repository, releaseBranch);
            this.UpdateVersion(projectDirectory, repository,
                version =>
                    string.IsNullOrEmpty(releaseUnstableTag)
                        ? version.WithoutPrepreleaseTags()
                        : version.SetFirstPrereleaseTag(releaseUnstableTag));

            // update version on main branch
            this.stdout.WriteLine($"Updating version on branch '{mainBranchName}'");
            Commands.Checkout(repository, mainBranchName);
            this.UpdateVersion(projectDirectory, repository,
                version => 
                    nextVersion ?? 
                    version
                        .Increment(releaseOptions.VersionIncrementOrDefault)
                        .SetFirstPrereleaseTag(releaseOptions.FirstUnstableTagOrDefault));

            // Merge release branch back to main branch
            this.stdout.WriteLine($"Merging branch '{releaseBranchName}' into '{mainBranchName}' ");
            var mergeOptions = new MergeOptions()
            {
                CommitOnSuccess = true,
                MergeFileFavor = MergeFileFavor.Ours
            };
            repository.Merge(releaseBranch, GetSignature(repository), mergeOptions);
        }


        private string GetReleaseBranchName(VersionOptions versionOptions)
        {
            var branchNameFormat = versionOptions.ReleaseOrDefault.BranchNameOrDefault;

            // ensure there is a '{version}' placeholder in the branch name
            if(string.IsNullOrEmpty(branchNameFormat) || !branchNameFormat.Contains("{version}"))
            {
                this.stderr.WriteLine($"Invalid 'branchName' setting '{branchNameFormat}'. Missing version placeholder '{{version}}'");
                throw new ReleasePreparationException(ReleasePreparationError.InvalidBranchNameSetting);
            }

            // replace the "{version}" placeholder with the actual version
            return branchNameFormat.Replace("{version}", versionOptions.Version.Version.ToString());
        }

        private void UpdateVersion(string projectDirectory, Repository repository, Func<SemanticVersion, SemanticVersion> updateAction)
        {
            var signature = GetSignature(repository);

            var versionOptions = VersionFile.GetVersion(projectDirectory);
            var oldVersion = versionOptions.Version;
            var newVersion = updateAction(oldVersion);

            if(IsVersionDecrement(oldVersion, newVersion))
            {
                this.stderr.WriteLine($"Cannot change version from {oldVersion} to {newVersion} because {newVersion} is older than {oldVersion}");
                throw new ReleasePreparationException(ReleasePreparationError.VersionDecrement);
            }

            this.stdout.WriteLine($"Setting version to {newVersion}");

            versionOptions.Version = newVersion;
            var filePath = VersionFile.SetVersion(projectDirectory, versionOptions);
            
            Commands.Stage(repository, filePath);
            repository.Commit($"Set version to '{versionOptions.Version}'", signature, signature, new CommitOptions() { AllowEmptyCommit = true } );
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

        private Repository GetRepository(string projectDirectory)
        {
            // open git repo and use default configuration (in order to commit we need a configured user name and email
            // which is most likely configured on a user/system level rather than the repo level
            var repository = GitExtensions.OpenGitRepo(projectDirectory, useDefaultConfigSearchPaths: true);
            if (repository == null)
            {
                this.stderr.WriteLine($"No git repository found above directory '{projectDirectory}'");
                throw new ReleasePreparationException(ReleasePreparationError.NoGitRepo);
            }

            // abort if there are any pending changes
            if (repository.RetrieveStatus().IsDirty)
            {
                this.stderr.WriteLine($"Uncommitted changes in directory '{projectDirectory}'");
                throw new ReleasePreparationException(ReleasePreparationError.UncommittedChanges);
            }

            return repository;
        }

        private static bool IsVersionDecrement(SemanticVersion oldVersion, SemanticVersion newVersion)
        {
            if (newVersion.Version > oldVersion.Version)
            {
                return false;
            }
            else if (newVersion.Version == oldVersion.Version)
            {
                return string.IsNullOrEmpty(oldVersion.Prerelease) &&
                      !string.IsNullOrEmpty(newVersion.Prerelease);
            }
            else
            {
                // newVersion.Version < oldVersion.Version
                return true;
            }
        }
    }
}
