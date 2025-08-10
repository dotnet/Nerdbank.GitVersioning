// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Construction;
using Nerdbank.GitVersioning.Commands;
using Nerdbank.GitVersioning.LibGit2;
using Newtonsoft.Json;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.PackageManagement;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using Validation;

namespace Nerdbank.GitVersioning.Tool
{
    internal class Program
    {
        private const string DefaultVersionSpec = "1.0-beta";

        private const string DefaultOutputFormat = "text";

        private const string DefaultRef = "HEAD";

        private const string PackageId = "Nerdbank.GitVersioning";

        private const BindingFlags CaseInsensitiveFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase;

        private static readonly string[] SupportedFormats = new[] { "text", "json" };
        private static ExitCodes exitCode;

        private enum ExitCodes
        {
            OK,
            NoGitRepo,
            InvalidVersionSpec,
            BadCloudVariable,
            DuplicateCloudVariable,
            NoCloudBuildEnvDetected,
            UnsupportedFormat,
            NoMatchingVersion,
            BadGitRef,
            NoVersionJsonFound,
            TagConflict,
            NoCloudBuildProviderMatch,
            BadVariable,
            UncommittedChanges,
            InvalidBranchNameSetting,
            BranchAlreadyExists,
            UserNotConfigured,
            DetachedHead,
            InvalidVersionIncrementSetting,
            InvalidParameters,
            InvalidVersionIncrement,
            InvalidNuGetPackageSource,
            PackageIdNotFound,
            ShallowClone,
            InternalError,
            InvalidTagNameSetting,
            InvalidUnformattedCommitMessage,
        }

        private static bool AlwaysUseLibGit2 => string.Equals(Environment.GetEnvironmentVariable("NBGV_GitEngine"), "LibGit2", StringComparison.Ordinal);

        private static string[] CloudProviderNames => CloudBuild.SupportedCloudBuilds.Select(cb => cb.GetType().Name).ToArray();

        public static int Main(string[] args)
        {
            string thisAssemblyPath = typeof(Program).GetTypeInfo().Assembly.Location;

            GitLoaderContext loaderContext = new(Path.GetDirectoryName(thisAssemblyPath));
            Assembly inContextAssembly = loaderContext.LoadFromAssemblyPath(thisAssemblyPath);
            Type innerProgramType = inContextAssembly.GetType(typeof(Program).FullName);
            object innerProgram = Activator.CreateInstance(innerProgramType);

            MethodInfo mainInnerMethod = innerProgramType.GetMethod(nameof(MainInner), BindingFlags.Static | BindingFlags.NonPublic);
            int result = (int)mainInnerMethod.Invoke(null, new object[] { args });
            return result;
        }

        private static RootCommand BuildCommandLine()
        {
#pragma warning disable IDE0008
            Command install;
            {
                var path = new Option<string>("--path", ["-p"])
                {
                    Description = "The path to the directory that should contain the version.json file. The default is the root of the git repo.",
                };
                var version = new Option<string>("--version", ["-v"])
                {
                    Description = $"The initial version to set.",
                    DefaultValueFactory = _ => DefaultVersionSpec,
                };
                var source = new Option<string[]>("--source", ["-s"])
                {
                    Description = $"The URI(s) of the NuGet package source(s) used to determine the latest stable version of the {PackageId} package. This setting overrides all of the sources specified in the NuGet.Config files.",
                    DefaultValueFactory = _ => Array.Empty<string>(),
                    Arity = ArgumentArity.OneOrMore,
                    AllowMultipleArgumentsPerToken = true,
                };
                install = new Command("install", "Prepares a project to have version stamps applied using Nerdbank.GitVersioning.")
                {
                    path,
                    version,
                    source,
                };
                install.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
                {
                    var pathValue = parseResult.GetValue(path);
                    var versionValue = parseResult.GetValue(version);
                    var sourceValue = parseResult.GetValue(source);
                    return await OnInstallCommand(pathValue, versionValue, sourceValue);
                });
            }

            Command getVersion;
            {
                var project = new Option<string>("--project", ["-p"])
                {
                    Description = "The path to the project or project directory. The default is the current directory.",
                };
                var metadata = new Option<string[]>("--metadata", Array.Empty<string>())
                {
                    Description = "Adds an identifier to the build metadata part of a semantic version.",
                    Arity = ArgumentArity.OneOrMore,
                    AllowMultipleArgumentsPerToken = true,
                };
                var format = new Option<string>("--format", ["-f"])
                {
                    Description = $"The format to write the version information. Allowed values are: {string.Join(", ", SupportedFormats)}. The default is {DefaultOutputFormat}.",
                };
                var variable = new Option<string>("--variable", ["-v"])
                {
                    Description = "The name of just one version property to print to stdout. When specified, the output is always in raw text. Useful in scripts.",
                };
                var commit = new Argument<string>("commit-ish")
                {
                    Description = $"The commit/ref to get the version information for.",
                    DefaultValueFactory = _ => DefaultRef,
                    Arity = ArgumentArity.ZeroOrOne,
                };
                getVersion = new Command("get-version", "Gets the version information for a project.")
                {
                    project,
                    metadata,
                    format,
                    variable,
                    commit,
                };

                getVersion.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
                {
                    var projectValue = parseResult.GetValue(project);
                    var metadataValue = parseResult.GetValue(metadata);
                    var formatValue = parseResult.GetValue(format);
                    var variableValue = parseResult.GetValue(variable);
                    var commitValue = parseResult.GetValue(commit);
                    return await OnGetVersionCommand(projectValue, metadataValue, formatValue, variableValue, commitValue);
                });
            }

            Command setVersion;
            {
                var project = new Option<string>("--project", ["-p"])
                {
                    Description = "The path to the project or project directory. The default is the root directory of the repo that spans the current directory, or an existing version.json file, if applicable.",
                };
                var version = new Argument<string>("version")
                {
                    Description = "The version to set.",
                };
                setVersion = new Command("set-version", "Updates the version stamp that is applied to a project.")
                {
                    project,
                    version,
                };

                setVersion.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
                {
                    var projectValue = parseResult.GetValue(project);
                    var versionValue = parseResult.GetValue(version);
                    return await OnSetVersionCommand(projectValue, versionValue);
                });
            }

            Command tag;
            {
                var project = new Option<string>("--project", ["-p"])
                {
                    Description = "The path to the project or project directory. The default is the root directory of the repo that spans the current directory, or an existing version.json file, if applicable.",
                };
                var versionOrRef = new Argument<string>("versionOrRef")
                {
                    Description = $"The a.b.c[.d] version or git ref to be tagged.",
                    DefaultValueFactory = _ => DefaultRef,
                    Arity = ArgumentArity.ZeroOrOne,
                };
                tag = new Command("tag", "Creates a git tag to mark a version.")
                {
                    project,
                    versionOrRef,
                };

                tag.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
                {
                    var projectValue = parseResult.GetValue(project);
                    var versionOrRefValue = parseResult.GetValue(versionOrRef);
                    return await OnTagCommand(projectValue, versionOrRefValue);
                });
            }

            Command getCommits;
            {
                var project = new Option<string>("--project", ["-p"])
                {
                    Description = "The path to the project or project directory. The default is the root directory of the repo that spans the current directory, or an existing version.json file, if applicable.",
                };
                var quiet = new Option<bool>("--quiet", ["-q"])
                {
                    Description = "Use minimal output.",
                };
                var version = new Argument<string>("version")
                {
                    Description = "The a.b.c[.d] version to find.",
                };
                getCommits = new Command("get-commits", "Gets the commit(s) that match a given version.")
                {
                    project,
                    quiet,
                    version,
                };

                getCommits.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
                {
                    var projectValue = parseResult.GetValue(project);
                    var quietValue = parseResult.GetValue(quiet);
                    var versionValue = parseResult.GetValue(version);
                    return await OnGetCommitsCommand(projectValue, quietValue, versionValue);
                });
            }

            Command cloud;
            {
                var project = new Option<string>("--project", ["-p"])
                {
                    Description = "The path to the project or project directory used to calculate the version. The default is the current directory. Ignored if the -v option is specified.",
                };
                var metadata = new Option<string[]>("--metadata", Array.Empty<string>())
                {
                    Description = "Adds an identifier to the build metadata part of a semantic version.",
                    Arity = ArgumentArity.OneOrMore,
                    AllowMultipleArgumentsPerToken = true,
                };
                var version = new Option<string>("--version", ["-v"])
                {
                    Description = "The string to use for the cloud build number. If not specified, the computed version will be used.",
                };
                var ciSystem = new Option<string>("--ci-system", ["-s"])
                {
                    Description = "Force activation for a particular CI system. If not specified, auto-detection will be used. Supported values are: " + string.Join(", ", CloudProviderNames),
                };
                var allVars = new Option<bool>("--all-vars", ["-a"])
                {
                    Description = "Defines ALL version variables as cloud build variables, with a \"NBGV_\" prefix.",
                };
                var commonVars = new Option<bool>("--common-vars", ["-c"])
                {
                    Description = "Defines a few common version variables as cloud build variables, with a \"Git\" prefix (e.g. GitBuildVersion, GitBuildVersionSimple, GitAssemblyInformationalVersion).",
                };
                var skipCloudBuildNumber = new Option<bool>("--skip-cloud-build-number", Array.Empty<string>())
                {
                    Description = "Do not emit the cloud build variable to set the build number. This is useful when you want to set other cloud build variables but not the build number.",
                };
                var define = new Option<string[]>("--define", ["-d"])
                {
                    Description = "Additional cloud build variables to define. Each should be in the NAME=VALUE syntax.",
                    DefaultValueFactory = _ => Array.Empty<string>(),
                    Arity = ArgumentArity.OneOrMore,
                    AllowMultipleArgumentsPerToken = true,
                };
                cloud = new Command("cloud", "Communicates with the ambient cloud build to set the build number and/or other cloud build variables.")
                {
                    project,
                    metadata,
                    version,
                    ciSystem,
                    allVars,
                    commonVars,
                    skipCloudBuildNumber,
                    define,
                };

                cloud.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
                {
                    var projectValue = parseResult.GetValue(project);
                    var metadataValue = parseResult.GetValue(metadata);
                    var versionValue = parseResult.GetValue(version);
                    var ciSystemValue = parseResult.GetValue(ciSystem);
                    var allVarsValue = parseResult.GetValue(allVars);
                    var commonVarsValue = parseResult.GetValue(commonVars);
                    var skipCloudBuildNumberValue = parseResult.GetValue(skipCloudBuildNumber);
                    var defineValue = parseResult.GetValue(define);
                    return await OnCloudCommand(projectValue, metadataValue, versionValue, ciSystemValue, allVarsValue, commonVarsValue, skipCloudBuildNumberValue, defineValue);
                });
            }

            Command prepareRelease;
            {
                var project = new Option<string>("--project", ["-p"])
                {
                    Description = "The path to the project or project directory. The default is the current directory.",
                };
                var nextVersion = new Option<string>("--nextVersion", Array.Empty<string>())
                {
                    Description = "The version to set for the current branch. If omitted, the next version is determined automatically by incrementing the current version.",
                };
                var versionIncrement = new Option<string>("--versionIncrement", Array.Empty<string>())
                {
                    Description = "Overrides the 'versionIncrement' setting set in version.json for determining the next version of the current branch.",
                };
                var format = new Option<string>("--format", ["-f"])
                {
                    Description = $"The format to write information about the release. Allowed values are: {string.Join(", ", SupportedFormats)}. The default is {DefaultOutputFormat}.",
                };
                var unformattedCommitMessage = new Option<string>("--commit-message-pattern", Array.Empty<string>())
                {
                    Description = "A custom message to use for the commit that changes the version number. May include {0} for the version number. If not specified, the default is \"Set version to '{0}'\".",
                };
                var tagArgument = new Argument<string>("tag")
                {
                    Description = "The prerelease tag to apply on the release branch (if any). If not specified, any existing prerelease tag will be removed. The preceding hyphen may be omitted.",
                    Arity = ArgumentArity.ZeroOrOne,
                };
                prepareRelease = new Command("prepare-release", "Prepares a release by creating a release branch for the current version and adjusting the version on the current branch.")
                {
                    project,
                    nextVersion,
                    versionIncrement,
                    format,
                    unformattedCommitMessage,
                    tagArgument,
                };

                prepareRelease.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
                {
                    var projectValue = parseResult.GetValue(project);
                    var nextVersionValue = parseResult.GetValue(nextVersion);
                    var versionIncrementValue = parseResult.GetValue(versionIncrement);
                    var formatValue = parseResult.GetValue(format);
                    var tagArgumentValue = parseResult.GetValue(tagArgument);
                    var unformattedCommitMessageValue = parseResult.GetValue(unformattedCommitMessage);
                    return await OnPrepareReleaseCommand(projectValue, nextVersionValue, versionIncrementValue, formatValue, tagArgumentValue, unformattedCommitMessageValue);
                });
            }

            var root = new RootCommand($"{ThisAssembly.AssemblyTitle} v{ThisAssembly.AssemblyInformationalVersion}")
            {
                install,
                getVersion,
                setVersion,
                tag,
                getCommits,
                cloud,
                prepareRelease,
            };

            return root;
#pragma warning restore IDE0008
        }

        private static void PrintException(Exception ex)
        {
            try
            {
                Console.Error.WriteLine("Unhandled exception: {0}", ex);
            }
            catch (Exception ex2)
            {
                Console.Error.WriteLine("Unhandled exception: {0}", ex.Message);
                Console.Error.WriteLine("Unhandled exception while trying to print string version of the above exception: {0}", ex2);
            }
        }

        private static int MainInner(string[] args)
        {
            try
            {
                RootCommand rootCommand = BuildCommandLine();
                ParseResult parseResult = rootCommand.Parse(args);
                exitCode = (ExitCodes)parseResult.Invoke();
            }
            catch (GitException ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                exitCode = ex.ErrorCode switch
                {
                    GitException.ErrorCodes.ObjectNotFound when ex.IsShallowClone => ExitCodes.ShallowClone,
                    _ => ExitCodes.InternalError,
                };
            }

            return (int)exitCode;
        }

        private static async Task<int> OnInstallCommand(string path, string version, string[] source)
        {
            if (!SemanticVersion.TryParse(string.IsNullOrEmpty(version) ? DefaultVersionSpec : version, out SemanticVersion semver))
            {
                Console.Error.WriteLine($"\"{version}\" is not a semver-compliant version spec.");
                return (int)ExitCodes.InvalidVersionSpec;
            }

            var options = new VersionOptions
            {
                Version = semver,
                PublicReleaseRefSpec = new string[]
                {
                    @"^refs/heads/master$",
                    @"^refs/heads/v\d+(?:\.\d+)?$",
                },
                CloudBuild = new VersionOptions.CloudBuildOptions
                {
                    BuildNumber = new VersionOptions.CloudBuildNumberOptions
                    {
                        Enabled = true,
                    },
                },
            };
            string searchPath = GetSpecifiedOrCurrentDirectoryPath(path);
            if (!Directory.Exists(searchPath))
            {
                Console.Error.WriteLine("\"{0}\" is not an existing directory.", searchPath);
                return (int)ExitCodes.NoGitRepo;
            }

            using var context = GitContext.Create(searchPath, engine: GitContext.Engine.ReadWrite);
            if (!context.IsRepository)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return (int)ExitCodes.NoGitRepo;
            }

            if (string.IsNullOrEmpty(path))
            {
                path = context.WorkingTreePath;
            }

            VersionOptions existingOptions = context.VersionFile.GetVersion();
            if (existingOptions is not null)
            {
                if (!string.IsNullOrEmpty(version) && version != DefaultVersionSpec)
                {
                    int setVersionExitCode = await OnSetVersionCommand(path, version);
                    if (setVersionExitCode != (int)ExitCodes.OK)
                    {
                        return setVersionExitCode;
                    }
                }
            }
            else
            {
                string versionJsonPath = context.VersionFile.SetVersion(path, options);
                context.Stage(versionJsonPath);
            }

            // Create/update the Directory.Build.props file in the directory of the version.json file to add the NB.GV package.
            string directoryBuildPropsPath = Path.Combine(path, "Directory.Build.props");
            ProjectRootElement propsFile = File.Exists(directoryBuildPropsPath)
                ? ProjectRootElement.Open(directoryBuildPropsPath)
                : ProjectRootElement.Create(directoryBuildPropsPath);

            // Validate given sources
            foreach (string src in source)
            {
                // TODO: Can declare Option<Uri> to validate argument during parsing.
                if (!Uri.TryCreate(src, UriKind.Absolute, out Uri _))
                {
                    Console.Error.WriteLine($"\"{src}\" is not a valid NuGet package source.");
                    return (int)ExitCodes.InvalidNuGetPackageSource;
                }
            }

            string packageVersion = GetLatestPackageVersionAsync(PackageId, path, source).GetAwaiter().GetResult();
            if (string.IsNullOrEmpty(packageVersion))
            {
                string verifyPhrase = source.Any()
                    ? "Please verify the given 'source' option(s)."
                    : "Please verify the package sources in the NuGet.Config files.";
                Console.Error.WriteLine($"Latest stable version of the {PackageId} package could not be determined. " + verifyPhrase);
                return (int)ExitCodes.PackageIdNotFound;
            }

            const string PackageReferenceItemType = "PackageReference";
            const string PrivateAssetsMetadataName = "PrivateAssets";
            const string VersionMetadataName = "Version";

            // Check if Central Package Management is enabled
            bool isCpmEnabled = IsCentralPackageManagementEnabled(path);

            if (isCpmEnabled)
            {
                // Update Directory.Packages.props with PackageVersion
                UpdateDirectoryPackagesProps(path, PackageId, packageVersion);
                string directoryPackagesPropsPath = Path.Combine(path, "Directory.Packages.props");
                context.Stage(directoryPackagesPropsPath);
            }

            ProjectItemElement item = propsFile.Items.FirstOrDefault(i =>
                string.Equals(i.ItemType, PackageReferenceItemType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(i.Include, PackageId, StringComparison.OrdinalIgnoreCase));

            if (item is null)
            {
                var metadata = new Dictionary<string, string>
                {
                    { PrivateAssetsMetadataName, "all" },
                };

                // Only add Version if CPM is not enabled
                if (!isCpmEnabled)
                {
                    metadata[VersionMetadataName] = packageVersion;
                }

                item = propsFile.AddItem(
                    PackageReferenceItemType,
                    PackageId,
                    metadata);
            }
            else
            {
                if (isCpmEnabled)
                {
                    // Remove Version metadata if CPM is enabled
                    ProjectMetadataElement versionMetadata = item.Metadata.FirstOrDefault(m =>
                        string.Equals(m.Name, VersionMetadataName, StringComparison.OrdinalIgnoreCase));
                    if (versionMetadata is not null)
                    {
                        item.RemoveChild(versionMetadata);
                    }
                }
                else
                {
                    // Update Version metadata if CPM is not enabled
                    ProjectMetadataElement versionMetadata = item.Metadata.FirstOrDefault(m =>
                        string.Equals(m.Name, VersionMetadataName, StringComparison.OrdinalIgnoreCase));
                    if (versionMetadata is not null)
                    {
                        versionMetadata.Value = packageVersion;
                    }
                    else
                    {
                        item.AddMetadata(VersionMetadataName, packageVersion);
                    }
                }
            }

            item.Condition = "!Exists('packages.config')";

            propsFile.Save(directoryBuildPropsPath);
            context.Stage(directoryBuildPropsPath);

            return (int)ExitCodes.OK;
        }

        private static Task<int> OnGetVersionCommand(string project, string[] metadata, string format, string variable, string commitish)
        {
            if (string.IsNullOrEmpty(format))
            {
                format = DefaultOutputFormat;
            }

            if (string.IsNullOrEmpty(commitish))
            {
                commitish = DefaultRef;
            }

            string searchPath = GetSpecifiedOrCurrentDirectoryPath(project);

            using var context = GitContext.Create(searchPath, engine: AlwaysUseLibGit2 ? GitContext.Engine.ReadWrite : GitContext.Engine.ReadOnly);
            if (!context.IsRepository)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return Task.FromResult((int)ExitCodes.NoGitRepo);
            }

            if (!context.TrySelectCommit(commitish))
            {
                Console.Error.WriteLine("rev-parse produced no commit for {0}", commitish);
                return Task.FromResult((int)ExitCodes.BadGitRef);
            }

            var oracle = new VersionOracle(context, CloudBuild.Active);
            if (metadata is not null)
            {
                oracle.BuildMetadata.AddRange(metadata);
            }

            // Take the PublicRelease environment variable into account, since the build would as well.
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PublicRelease")) && bool.TryParse(Environment.GetEnvironmentVariable("PublicRelease"), out bool publicRelease))
            {
                oracle.PublicRelease = publicRelease;
            }

            if (string.IsNullOrEmpty(variable))
            {
                switch (format.ToLowerInvariant())
                {
                    case "text":
                        Console.WriteLine("Version:                      {0}", oracle.Version);
                        Console.WriteLine("AssemblyVersion:              {0}", oracle.AssemblyVersion);
                        Console.WriteLine("AssemblyInformationalVersion: {0}", oracle.AssemblyInformationalVersion);
                        Console.WriteLine("NuGetPackageVersion:          {0}", oracle.NuGetPackageVersion);
                        Console.WriteLine("NpmPackageVersion:            {0}", oracle.NpmPackageVersion);
                        break;
                    case "json":
                        var converters = new JsonConverter[]
                        {
                        new Newtonsoft.Json.Converters.VersionConverter(),
                        };
                        Console.WriteLine(JsonConvert.SerializeObject(oracle, Formatting.Indented, converters));
                        break;
                    default:
                        Console.Error.WriteLine("Unsupported format: {0}", format);
                        return Task.FromResult((int)ExitCodes.UnsupportedFormat);
                }
            }
            else
            {
                if (format != "text")
                {
                    Console.Error.WriteLine("Format must be \"text\" when querying for an individual variable's value.");
                    return Task.FromResult((int)ExitCodes.UnsupportedFormat);
                }

                PropertyInfo property = oracle.GetType().GetProperty(variable, CaseInsensitiveFlags);
                if (property is null)
                {
                    Console.Error.WriteLine("Variable \"{0}\" not a version property.", variable);
                    return Task.FromResult((int)ExitCodes.BadVariable);
                }

                object propertyValue = property.GetValue(oracle);
                string output = propertyValue switch
                {
                    DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                    _ => propertyValue?.ToString() ?? string.Empty,
                };
                Console.WriteLine(output);
            }

            return Task.FromResult((int)ExitCodes.OK);
        }

        private static Task<int> OnSetVersionCommand(string project, string version)
        {
            if (!SemanticVersion.TryParse(string.IsNullOrEmpty(version) ? DefaultVersionSpec : version, out SemanticVersion semver))
            {
                Console.Error.WriteLine($"\"{version}\" is not a semver-compliant version spec.");
                return Task.FromResult((int)ExitCodes.InvalidVersionSpec);
            }

            var defaultOptions = new VersionOptions
            {
                Version = semver,
            };

            string searchPath = GetSpecifiedOrCurrentDirectoryPath(project);
            using var context = GitContext.Create(searchPath, engine: GitContext.Engine.ReadWrite);
            VersionOptions existingOptions = context.VersionFile.GetVersion(out string actualDirectory);
            string versionJsonPath;
            if (existingOptions is not null)
            {
                existingOptions.Version = semver;
                versionJsonPath = context.VersionFile.SetVersion(actualDirectory, existingOptions);
            }
            else if (string.IsNullOrEmpty(project))
            {
                if (!context.IsRepository)
                {
                    Console.Error.WriteLine("No version file and no git repo found at or above: \"{0}\"", searchPath);
                    return Task.FromResult((int)ExitCodes.NoGitRepo);
                }

                versionJsonPath = context.VersionFile.SetVersion(context.WorkingTreePath, defaultOptions);
            }
            else
            {
                versionJsonPath = context.VersionFile.SetVersion(project, defaultOptions);
            }

            if (context.IsRepository)
            {
                context.Stage(versionJsonPath);
            }

            return Task.FromResult((int)ExitCodes.OK);
        }

        private static Task<int> OnTagCommand(string project, string versionOrRef)
        {
            if (string.IsNullOrEmpty(versionOrRef))
            {
                versionOrRef = DefaultRef;
            }

            string searchPath = GetSpecifiedOrCurrentDirectoryPath(project);

            using var context = (LibGit2Context)GitContext.Create(searchPath, engine: GitContext.Engine.ReadWrite);
            if (context is null)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return Task.FromResult((int)ExitCodes.NoGitRepo);
            }

            // get tag name format
            VersionOptions versionOptions = context.VersionFile.GetVersion();
            if (versionOptions is null)
            {
                Console.Error.WriteLine($"Failed to load version file for directory '{searchPath}'.");
                return Task.FromResult((int)ExitCodes.NoVersionJsonFound);
            }

            string tagNameFormat = versionOptions.ReleaseOrDefault.TagNameOrDefault;

            // ensure there is a '{version}' placeholder in the tag name
            if (string.IsNullOrEmpty(tagNameFormat) || !tagNameFormat.Contains("{version}"))
            {
                Console.Error.WriteLine($"Invalid 'tagName' setting '{tagNameFormat}'. Missing version placeholder '{{version}}'.");
                return Task.FromResult((int)ExitCodes.InvalidTagNameSetting);
            }

            // get commit to tag
            LibGit2Sharp.Repository repository = context.Repository;
            if (!context.TrySelectCommit(versionOrRef))
            {
                if (!Version.TryParse(versionOrRef, out Version parsedVersion))
                {
                    Console.Error.WriteLine($"\"{versionOrRef}\" is not a simple a.b.c[.d] version spec or git reference.");
                    return Task.FromResult((int)ExitCodes.InvalidVersionSpec);
                }

                string repoRelativeProjectDir = GetRepoRelativePath(searchPath, repository);
                var candidateCommits = LibGit2GitExtensions.GetCommitsFromVersion(context, parsedVersion).ToList();
                if (candidateCommits.Count == 0)
                {
                    Console.Error.WriteLine("No commit with that version found.");
                    return Task.FromResult((int)ExitCodes.NoMatchingVersion);
                }
                else if (candidateCommits.Count > 1)
                {
                    PrintCommits(false, context, candidateCommits, includeOptions: true);
                    int selection;
                    do
                    {
                        Console.Write("Enter selection: ");
                    }
                    while (!int.TryParse(Console.ReadLine(), out selection) || selection > candidateCommits.Count || selection < 1);
                    context.TrySelectCommit(candidateCommits[selection - 1].Sha);
                }
                else
                {
                    context.TrySelectCommit(candidateCommits.Single().Sha);
                }
            }

            var oracle = new VersionOracle(context, CloudBuild.Active);
            if (!oracle.VersionFileFound)
            {
                Console.Error.WriteLine("No version.json file found in or above \"{0}\" in commit {1}.", searchPath, context.GitCommitId);
                return Task.FromResult((int)ExitCodes.NoVersionJsonFound);
            }

            // assume a public release so we don't get a redundant -gCOMMITID in the tag name
            oracle.PublicRelease = true;

            // replace the "{version}" placeholder with the actual version
            string tagName = tagNameFormat.Replace("{version}", oracle.SemVer2);

            try
            {
                context.ApplyTag(tagName);
            }
            catch (LibGit2Sharp.NameConflictException)
            {
                var taggedCommit = repository.Tags[tagName].Target as LibGit2Sharp.Commit;
                bool correctTag = taggedCommit?.Sha == context.GitCommitId;
                Console.Error.WriteLine("The tag {0} is already defined ({1}).", tagName, correctTag ? "to the right commit" : $"expected {context.GitCommitId} but was on {taggedCommit.Sha}");
                return Task.FromResult((int)(correctTag ? ExitCodes.OK : ExitCodes.TagConflict));
            }

            Console.WriteLine("{0} tag created at {1}.", tagName, context.GitCommitId);
            Console.WriteLine("Remember to push to a remote: git push origin {0}", tagName);

            return Task.FromResult((int)ExitCodes.OK);
        }

        private static Task<int> OnGetCommitsCommand(string project, bool quiet, string version)
        {
            if (!Version.TryParse(version, out Version parsedVersion))
            {
                Console.Error.WriteLine($"\"{version}\" is not a simple a.b.c[.d] version spec.");
                return Task.FromResult((int)ExitCodes.InvalidVersionSpec);
            }

            string searchPath = GetSpecifiedOrCurrentDirectoryPath(project);

            using var context = (LibGit2Context)GitContext.Create(searchPath, engine: GitContext.Engine.ReadWrite);
            if (!context.IsRepository)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return Task.FromResult((int)ExitCodes.NoGitRepo);
            }

            IEnumerable<LibGit2Sharp.Commit> candidateCommits = LibGit2GitExtensions.GetCommitsFromVersion(context, parsedVersion);
            PrintCommits(quiet, context, candidateCommits);

            return Task.FromResult((int)ExitCodes.OK);
        }

        private static Task<int> OnCloudCommand(string project, string[] metadata, string version, string ciSystem, bool allVars, bool commonVars, bool skipCloudBuildNumber, string[] define)
        {
            string searchPath = GetSpecifiedOrCurrentDirectoryPath(project);
            if (!Directory.Exists(searchPath))
            {
                Console.Error.WriteLine("\"{0}\" is not an existing directory.", searchPath);
                return Task.FromResult((int)ExitCodes.NoGitRepo);
            }

            var additionalVariables = new Dictionary<string, string>();
            if (define is not null)
            {
                foreach (string def in define)
                {
                    string[] split = def.Split(new char[] { '=' }, 2);
                    if (split.Length < 2)
                    {
                        Console.Error.WriteLine($"\"{def}\" is not in the NAME=VALUE syntax required for cloud variables.");
                        return Task.FromResult((int)ExitCodes.BadCloudVariable);
                    }

                    if (additionalVariables.ContainsKey(split[0]))
                    {
                        Console.Error.WriteLine($"Cloud build variable \"{split[0]}\" specified more than once.");
                        return Task.FromResult((int)ExitCodes.DuplicateCloudVariable);
                    }

                    additionalVariables[split[0]] = split[1];
                }
            }

            try
            {
                var cloudCommand = new CloudCommand(Console.Out, Console.Error);
                cloudCommand.SetBuildVariables(searchPath, metadata, version, ciSystem, allVars, commonVars, !skipCloudBuildNumber, additionalVariables, AlwaysUseLibGit2);
            }
            catch (CloudCommand.CloudCommandException ex)
            {
                Console.Error.WriteLine(ex.Message);

                // map error codes
                switch (ex.Error)
                {
                    case CloudCommand.CloudCommandError.NoCloudBuildProviderMatch:
                        return Task.FromResult((int)ExitCodes.NoCloudBuildProviderMatch);
                    case CloudCommand.CloudCommandError.DuplicateCloudVariable:
                        return Task.FromResult((int)ExitCodes.DuplicateCloudVariable);
                    case CloudCommand.CloudCommandError.NoCloudBuildEnvDetected:
                        return Task.FromResult((int)ExitCodes.NoCloudBuildEnvDetected);
                    default:
                        Report.Fail($"{nameof(CloudCommand.CloudCommandError)}: {ex.Error}");
                        return Task.FromResult(-1);
                }
            }

            return Task.FromResult((int)ExitCodes.OK);
        }

        private static Task<int> OnPrepareReleaseCommand(string project, string nextVersion, string versionIncrement, string format, string tag, string unformattedCommitMessage)
        {
            // validate project path property
            string searchPath = GetSpecifiedOrCurrentDirectoryPath(project);
            if (!Directory.Exists(searchPath))
            {
                Console.Error.WriteLine($"\"{searchPath}\" is not an existing directory.");
                return Task.FromResult((int)ExitCodes.NoGitRepo);
            }

            // nextVersion and versionIncrement parameters cannot be combined
            if (!string.IsNullOrEmpty(nextVersion) && !string.IsNullOrEmpty(versionIncrement))
            {
                Console.Error.WriteLine("Options 'nextVersion' and 'versionIncrement' cannot be used at the same time.");
                return Task.FromResult((int)ExitCodes.InvalidParameters);
            }

            // parse versionIncrement if parameter was specified
            VersionOptions.ReleaseVersionIncrement? versionIncrementParsed = default;
            if (!string.IsNullOrEmpty(versionIncrement))
            {
                if (!Enum.TryParse<VersionOptions.ReleaseVersionIncrement>(versionIncrement, true, out VersionOptions.ReleaseVersionIncrement parsed))
                {
                    Console.Error.WriteLine($"\"{versionIncrement}\" is not a valid version increment");
                    return Task.FromResult((int)ExitCodes.InvalidVersionIncrement);
                }

                versionIncrementParsed = parsed;
            }

            // parse nextVersion if parameter was specified
            Version nextVersionParsed = default;
            if (!string.IsNullOrEmpty(nextVersion))
            {
                if (!Version.TryParse(nextVersion, out nextVersionParsed))
                {
                    Console.Error.WriteLine($"\"{nextVersion}\" is not a valid version spec.");
                    return Task.FromResult((int)ExitCodes.InvalidVersionSpec);
                }
            }

            // parse format
            if (string.IsNullOrEmpty(format))
            {
                format = DefaultOutputFormat;
            }

            if (!Enum.TryParse(format, true, out ReleaseManager.ReleaseManagerOutputMode outputMode))
            {
                Console.Error.WriteLine($"Unsupported format: {format}");
                return Task.FromResult((int)ExitCodes.UnsupportedFormat);
            }

            if (!string.IsNullOrEmpty(unformattedCommitMessage))
            {
                try
                {
                    string.Format(unformattedCommitMessage, "FormatValidator");
                }
                catch (FormatException ex)
                {
                    Console.Error.WriteLine($"Invalid commit message pattern: {ex.Message}");
                    return Task.FromResult((int)ExitCodes.InvalidUnformattedCommitMessage);
                }
            }

            // run prepare-release
            try
            {
                var releaseManager = new ReleaseManager(Console.Out, Console.Error);
                releaseManager.PrepareRelease(searchPath, tag, nextVersionParsed, versionIncrementParsed, outputMode, unformattedCommitMessage);
                return Task.FromResult((int)ExitCodes.OK);
            }
            catch (ReleaseManager.ReleasePreparationException ex)
            {
                // map error codes
                switch (ex.Error)
                {
                    case ReleaseManager.ReleasePreparationError.NoGitRepo:
                        return Task.FromResult((int)ExitCodes.NoGitRepo);
                    case ReleaseManager.ReleasePreparationError.UncommittedChanges:
                        return Task.FromResult((int)ExitCodes.UncommittedChanges);
                    case ReleaseManager.ReleasePreparationError.InvalidBranchNameSetting:
                        return Task.FromResult((int)ExitCodes.InvalidBranchNameSetting);
                    case ReleaseManager.ReleasePreparationError.NoVersionFile:
                        return Task.FromResult((int)ExitCodes.NoVersionJsonFound);
                    case ReleaseManager.ReleasePreparationError.VersionDecrement:
                    case ReleaseManager.ReleasePreparationError.NoVersionIncrement:
                        return Task.FromResult((int)ExitCodes.InvalidVersionSpec);
                    case ReleaseManager.ReleasePreparationError.BranchAlreadyExists:
                        return Task.FromResult((int)ExitCodes.BranchAlreadyExists);
                    case ReleaseManager.ReleasePreparationError.UserNotConfigured:
                        return Task.FromResult((int)ExitCodes.UserNotConfigured);
                    case ReleaseManager.ReleasePreparationError.DetachedHead:
                        return Task.FromResult((int)ExitCodes.DetachedHead);
                    case ReleaseManager.ReleasePreparationError.InvalidVersionIncrementSetting:
                        return Task.FromResult((int)ExitCodes.InvalidVersionIncrementSetting);
                    default:
                        Report.Fail($"{nameof(ReleaseManager.ReleasePreparationError)}: {ex.Error}");
                        return Task.FromResult(-1);
                }
            }
        }

        private static async Task<string> GetLatestPackageVersionAsync(string packageId, string root, IReadOnlyList<string> sources, CancellationToken cancellationToken = default)
        {
            ISettings settings = Settings.LoadDefaultSettings(root);

            var providers = new List<Lazy<INuGetResourceProvider>>();
            providers.AddRange(Repository.Provider.GetCoreV3());  // Add v3 API support

            var sourceRepositoryProvider = new SourceRepositoryProvider(new PackageSourceProvider(settings), providers);

            // Select package sources based on NuGet.Config files or given options, as 'nuget.exe restore' command does
            // See also 'DownloadCommandBase.GetPackageSources(ISettings)' at https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Clients/NuGet.CommandLine/Commands/DownloadCommandBase.cs
            IEnumerable<PackageSource> availableSources = sourceRepositoryProvider.PackageSourceProvider.LoadPackageSources().Where(s => s.IsEnabled);
            var packageSources = new List<PackageSource>();

            foreach (string source in sources)
            {
                PackageSource resolvedSource = availableSources.FirstOrDefault(s => s.Source.Equals(source, StringComparison.OrdinalIgnoreCase) || s.Name.Equals(source, StringComparison.OrdinalIgnoreCase));
                packageSources.Add(resolvedSource ?? new PackageSource(source));
            }

            if (sources.Count == 0)
            {
                packageSources.AddRange(availableSources);
            }

            SourceRepository[] sourceRepositories = packageSources.Select(sourceRepositoryProvider.CreateRepository).ToArray();
            var resolutionContext = new ResolutionContext(
                DependencyBehavior.Highest,
                includePrelease: false,
                includeUnlisted: false,
                VersionConstraints.None);

            // The target framework doesn't matter, since our package doesn't depend on this for its target projects.
            var framework = new NuGet.Frameworks.NuGetFramework("net45");

            ResolvedPackage pkg = await NuGetPackageManager.GetLatestVersionAsync(
                packageId,
                framework,
                resolutionContext,
                sourceRepositories,
                NullLogger.Instance,
                cancellationToken);

            return pkg.LatestVersion?.ToNormalizedString();
        }

        private static bool IsCentralPackageManagementEnabled(string path)
        {
            string directoryPackagesPropsPath = Path.Combine(path, "Directory.Packages.props");
            if (!File.Exists(directoryPackagesPropsPath))
            {
                return false;
            }

            return true;
        }

        private static void UpdateDirectoryPackagesProps(string path, string packageId, string packageVersion)
        {
            string directoryPackagesPropsPath = Path.Combine(path, "Directory.Packages.props");
            ProjectRootElement propsFile = File.Exists(directoryPackagesPropsPath)
                ? ProjectRootElement.Open(directoryPackagesPropsPath)
                : ProjectRootElement.Create(directoryPackagesPropsPath);

            const string PackageVersionItemType = "PackageVersion";
            const string VersionMetadataName = "Version";

            ProjectItemElement item = propsFile.Items.FirstOrDefault(i =>
                string.Equals(i.ItemType, PackageVersionItemType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(i.Include, packageId, StringComparison.OrdinalIgnoreCase));

            if (item is null)
            {
                item = propsFile.AddItem(
                    PackageVersionItemType,
                    packageId,
                    new Dictionary<string, string>
                    {
                        { VersionMetadataName, packageVersion },
                    });
            }
            else
            {
                ProjectMetadataElement versionMetadata = item.Metadata.FirstOrDefault(m =>
                    string.Equals(m.Name, VersionMetadataName, StringComparison.OrdinalIgnoreCase));
                if (versionMetadata is not null)
                {
                    versionMetadata.Value = packageVersion;
                }
                else
                {
                    item.AddMetadata(VersionMetadataName, packageVersion);
                }
            }

            propsFile.Save(directoryPackagesPropsPath);
        }

        private static string GetSpecifiedOrCurrentDirectoryPath(string versionJsonRoot)
        {
            return ShouldHaveTrailingDirectorySeparator(Path.GetFullPath(string.IsNullOrEmpty(versionJsonRoot) ? "." : versionJsonRoot));
        }

        private static string GetRepoRelativePath(string searchPath, LibGit2Sharp.Repository repository)
        {
            return searchPath.Substring(repository.Info.WorkingDirectory.Length);
        }

        private static string ShouldHaveTrailingDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static void PrintCommits(bool quiet, GitContext context, IEnumerable<LibGit2Sharp.Commit> candidateCommits, bool includeOptions = false)
        {
            int index = 1;
            foreach (LibGit2Sharp.Commit commit in candidateCommits)
            {
                if (includeOptions)
                {
                    Console.Write($"{index++,3}. ");
                }

                if (quiet)
                {
                    Console.WriteLine(commit.Sha);
                }
                else
                {
                    Assumes.True(context.TrySelectCommit(commit.Sha));
                    var oracle = new VersionOracle(context, null);
                    Console.WriteLine($"{commit.Sha} {oracle.Version} {commit.MessageShort}");
                }
            }
        }
    }
}
