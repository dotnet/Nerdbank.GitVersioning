// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;

[Trait("Engine", EngineString)]
public class BuildIntegrationInProjectManagedCombinatorialTests : BuildIntegrationManagedCombinatorialTests
{
    public BuildIntegrationInProjectManagedCombinatorialTests(Xunit.ITestOutputHelper logger)
        : base(logger)
    {
    }

    /// <inheritdoc/>
    protected override void ApplyGlobalProperties(IDictionary<string, string> globalProperties)
    {
        base.ApplyGlobalProperties(globalProperties);
        globalProperties["NBGV_CacheMode"] = "None";
    }
}
