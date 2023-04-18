// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;
using Xunit.Abstractions;

[Trait("Engine", EngineString)]
[Collection("Build")] // msbuild sets current directory in the process, so we can't have it be concurrent with other build tests.
public class BuildIntegrationInProjectManagedTests : BuildIntegrationManagedTests
{
    public BuildIntegrationInProjectManagedTests(ITestOutputHelper logger)
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
