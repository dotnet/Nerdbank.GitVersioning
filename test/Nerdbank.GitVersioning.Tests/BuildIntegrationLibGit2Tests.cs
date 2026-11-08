// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Nerdbank.GitVersioning;
using Xunit;

[Property("Engine", EngineString)]
[InheritsTests]
public class BuildIntegrationLibGit2Tests : SomeGitBuildIntegrationTests
{
    private const string EngineString = "LibGit2";

    public BuildIntegrationLibGit2Tests()
        : base(TestOutputHelper.Instance)
    {
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, GitContext.Engine.ReadWrite);

    protected override void ApplyGlobalProperties(IDictionary<string, string> globalProperties)
        => globalProperties["NBGV_GitEngine"] = EngineString;
}
