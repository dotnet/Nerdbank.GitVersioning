// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Nerdbank.GitVersioning;
using Xunit;
using Xunit.Abstractions;

[Trait("Engine", EngineString)]
[Collection("Build")] // msbuild sets current directory in the process, so we can't have it be concurrent with other build tests.
public class BuildIntegrationLibGit2Tests : SomeGitBuildIntegrationTests
{
    private const string EngineString = "LibGit2";

    public BuildIntegrationLibGit2Tests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, GitContext.Engine.ReadWrite);

    protected override void ApplyGlobalProperties(IDictionary<string, string> globalProperties)
        => globalProperties["NBGV_GitEngine"] = EngineString;
}
