// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit;

[Property("Engine", EngineString)]
[InheritsTests]
public class BuildIntegrationInProjectManagedTests : BuildIntegrationManagedTests
{
    public BuildIntegrationInProjectManagedTests()
    {
    }

    /// <inheritdoc/>
    protected override void ApplyGlobalProperties(IDictionary<string, string> globalProperties)
    {
        base.ApplyGlobalProperties(globalProperties);
        globalProperties["NBGV_CacheMode"] = "None";
    }
}
