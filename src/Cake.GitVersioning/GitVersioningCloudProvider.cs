// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cake.GitVersioning
{
    /// <summary>
    /// Defines the supported cloud build providers for the <see cref="GitVersioningAliases.GitVersioningCloud" /> alias.
    /// </summary>
    public enum GitVersioningCloudProvider
    {
        /// <summary>
        /// Use AppVeyor cloud build provider.
        /// </summary>
        AppVeyor,

        /// <summary>
        /// Use Azure Pipeline / Visual Studio Team Services / TFS cloud build provider.
        /// </summary>
        VisualStudioTeamServices,

        /// <summary>
        /// Use GitHub Actions cloud build provider.
        /// </summary>
        GitHubActions,

        /// <summary>
        /// Use the TeamCity cloud build provider.
        /// </summary>
        TeamCity,

        /// <summary>
        /// Use the Atlassian Bamboo cloud build provider.
        /// </summary>
        AtlassianBamboo,

        /// <summary>
        /// Use the Jenkins cloud build provider.
        /// </summary>
        Jenkins,

        /// <summary>
        /// Use the GitLab CI cloud build provider.
        /// </summary>
        GitLab,

        /// <summary>
        /// Use the Travis CI cloud build provider.
        /// </summary>
        Travis,

        /// <summary>
        /// Use the Jetbrains Space cloud build provider.
        /// </summary>
        SpaceAutomation,

        /// <summary>
        /// Use the Bitbucket cloud build provider.
        /// </summary>
        BitbucketCloud,
    }
}
