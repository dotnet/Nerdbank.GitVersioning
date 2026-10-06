// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// These tests share process-wide state (the current directory, environment variables, MSBuild and libgit2 global settings),
// so like the former xunit.runner.json (parallelizeTestCollections: false), run them one at a time.
[assembly: NotInParallel]
