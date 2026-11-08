// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Nerdbank.GitVersioning;
using Xunit;

[Trait("Engine", EngineString)]
public class BuildIntegrationManagedCombinatorialTests : CombinatorialBuildIntegrationTests
{
    protected const string EngineString = "Managed";

    public BuildIntegrationManagedCombinatorialTests(Xunit.ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override GitContext CreateGitContext(string path, string committish = null)
        => GitContext.Create(path, committish, GitContext.Engine.ReadOnly);

    protected override void ApplyGlobalProperties(IDictionary<string, string> globalProperties)
        => globalProperties["NBGV_GitEngine"] = EngineString;
}
