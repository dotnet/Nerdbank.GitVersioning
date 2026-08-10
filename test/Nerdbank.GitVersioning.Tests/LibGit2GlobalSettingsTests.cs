// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using LibGit2Sharp;
using Nerdbank.GitVersioning;
using Xunit;

[Collection("LibGit2 global settings")]
public class LibGit2GlobalSettingsTests
{
    [Fact]
    public void CreatingContextDoesNotAlterConfigSearchPaths()
    {
        string[] originalGlobalPaths = GlobalSettings.GetConfigSearchPaths(ConfigurationLevel.Global).ToArray();
        string[] originalSystemPaths = GlobalSettings.GetConfigSearchPaths(ConfigurationLevel.System).ToArray();
        string testDirectory = Path.Combine(Path.GetTempPath(), $"{nameof(LibGit2GlobalSettingsTests)}_{Path.GetRandomFileName()}");
        string repositoryPath = Path.Combine(testDirectory, "repo");

        try
        {
            Directory.CreateDirectory(testDirectory);
            GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.Global, testDirectory);
            GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.System, testDirectory);
            Repository.Init(repositoryPath);

            using GitContext context = GitContext.Create(repositoryPath, engine: GitContext.Engine.ReadWrite);

            Assert.Equal(new[] { testDirectory }, GlobalSettings.GetConfigSearchPaths(ConfigurationLevel.Global));
            Assert.Equal(new[] { testDirectory }, GlobalSettings.GetConfigSearchPaths(ConfigurationLevel.System));
        }
        finally
        {
            GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.Global, originalGlobalPaths);
            GlobalSettings.SetConfigSearchPaths(ConfigurationLevel.System, originalSystemPaths);
            TestUtilities.DeleteDirectory(testDirectory);
        }
    }
}
