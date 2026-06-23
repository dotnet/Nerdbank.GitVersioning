// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Graph;
using Microsoft.Build.Locator;
using Nerdbank.GitVersioning.Commands;
using Nerdbank.GitVersioning.LibGit2;
using Newtonsoft.Json;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.PackageManagement;
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
            PathFiltersMismatch,
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
                var metadata = new Option<string[]>("--metadata")
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
                var publicRelease = new Option<bool?>("--public-release")
                {
                    Description = "Specifies whether this is a public release. When specified, overrides the PublicRelease environment variable. Use --public-release=true or --public-release=false to explicitly set the value.",
                    Arity = ArgumentArity.ZeroOrOne,
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
                    publicRelease,
                    commit,
                };

                getVersion.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
                {
                    var projectValue = parseResult.GetValue(project);
                    var metadataValue = parseResult.GetValue(metadata);
                    var formatValue = parseResult.GetValue(format);
                    var variableValue = parseResult.GetValue(variable);
                    var publicReleaseValue = parseResult.GetValue(publicRelease);
                    var commitValue = parseResult.GetValue(commit);
                    return await OnGetVersionCommand(projectValue, metadataValue, formatValue, variableValue, publicReleaseValue, commitValue);
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
                var whatIf = new Option<bool>("--what-if")
                {
                    Description = "Calculates and outputs the tag name without creating the tag.",
                };
                tag = new Command("tag", "Creates a git tag to mark a version.")
                {
                    project,
                    versionOrRef,
                    whatIf,
                };

                tag.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
                {
                    var projectValue = parseResult.GetValue(project);
                    var versionOrRefValue = parseResult.GetValue(versionOrRef);
                    var whatIfValue = parseResult.GetValue(whatIf);
                    return await OnTagCommand(projectValue, versionOrRefValue, whatIfValue);
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
                var metadata = new Option<string[]>("--metadata")
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
                var skipCloudBuildNumber = new Option<bool>("--skip-cloud-build-number")
                {
                    Description = "Do not emit the cloud build variable to set the build number. This is useful when you want to set other cloud build variables but not the build number.",
                };
                var define = new Option<string[]>("--define", ["-d"])
                {
                    Description = "Additional cloud build variables to define. Each should be in the NAME=VALUE syntax.",
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
                var nextVersion = new Option<string>("--nextVersion")
                {
                    Description = "The version to set for the current branch. If omitted, the next version is determined automatically by incrementing the current version.",
                };
                var versionIncrement = new Option<string>("--versionIncrement")
                {
                    Description = "Overrides the 'versionIncrement' setting set in version.json for determining the next version of the current branch.",
                };
                var format = new Option<string>("--format", ["-f"])
                {
                    Description = $"The format to write information about the release. Allowed values are: {string.Join(", ", SupportedFormats)}. The default is {DefaultOutputFormat}.",
                };
                var unformattedCommitMessage = new Option<string>("--commit-message-pattern")
                {
                    Description = "A custom message to use for the commit that changes the version number. May include {0} for the version number. If not specified, the default is \"Set version to '{0}'\".",
                };
                var whatIf = new Option<bool>("--what-if")
                {
                    Description = "Simulates the prepare-release operation and prints the new version that would be set, but does not actually make any changes.",
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
                    whatIf,
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
                    var whatIfValue = parseResult.GetValue(whatIf);
                    return await OnPrepareReleaseCommand(projectValue, nextVersionValue, versionIncrementValue, formatValue, tagArgumentValue, unformattedCommitMessageValue, whatIfValue);
                });
            }

            Command pathFilters;
            {
                var paths = new Argument<string[]>("path")
                {
                    Description = "One or more paths to search. Each may be a directory (recursively searched for version.json files) or a version.json file directly (processed without recursive search). Defaults to the current directory.",
                    Arity = ArgumentArity.ZeroOrMore,
                };
                var extraExtensions = new Option<string[]>("--ext")
                {
                    Description = "Additional MSBuild project file extensions to include (e.g. --ext .myproj). Default extensions are .csproj, .vbproj, .fsproj, and .vcxproj.",
                    Arity = ArgumentArity.OneOrMore,
                    AllowMultipleArgumentsPerToken = true,
                };

                var updateSubCommand = new Command("update", "Computes pathFilters based on MSBuild project references and imports and writes the result to each applicable version.json file.")
                {
                    paths,
                    extraExtensions,
                };
                updateSubCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
                {
                    var pathsValue = parseResult.GetValue(paths);
                    var extraExtensionsValue = parseResult.GetValue(extraExtensions);
                    return await OnPathFiltersUpdateCommand(pathsValue, extraExtensionsValue);
                });

                var checkSubCommand = new Command("check", "Computes pathFilters based on MSBuild project references and imports and verifies they match what is in each applicable version.json file. Exits with a non-zero exit code when differences are found.")
                {
                    paths,
                    extraExtensions,
                };
                checkSubCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
                {
                    var pathsValue = parseResult.GetValue(paths);
                    var extraExtensionsValue = parseResult.GetValue(extraExtensions);
                    return await OnPathFiltersCheckCommand(pathsValue, extraExtensionsValue);
                });

                pathFilters = new Command("path-filters", "Manages the pathFilters property in version.json files based on MSBuild project references and imports.")
                {
                    updateSubCommand,
                    checkSubCommand,
                };
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
                pathFilters,
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

        /// <summary>
        /// Gets the effective git engine to use based on environment variables and command settings.
        /// </summary>
        /// <param name="preferReadWrite">Whether to prefer ReadWrite (LibGit2) engine when not explicitly specified.</param>
        /// <returns>The engine to use.</returns>
        private static GitContext.Engine GetEffectiveGitEngine(bool preferReadWrite = false)
        {
            // Use the shared logic from GitContext which handles both NBGV_GitEngine and DEPENDABOT env vars
            return GitContext.GetEffectiveGitEngine(preferReadWrite ? GitContext.Engine.ReadWrite : GitContext.Engine.ReadOnly);
        }

        private static int MainInner(string[] args)
        {
            // Register MSBuild locator to ensure SDK resolvers are available for project evaluation.
            // This must be called before any MSBuild code is loaded.
            if (!MSBuildLocator.IsRegistered)
            {
                try
                {
                    MSBuildLocator.RegisterDefaults();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to register MSBuildLocator: {ex.Message}");
                }
            }

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

        private static Task<int> OnGetVersionCommand(string project, string[] metadata, string format, string variable, bool? publicReleaseArg, string commitish)
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

            using var context = GitContext.Create(searchPath, engine: GetEffectiveGitEngine(preferReadWrite: AlwaysUseLibGit2));
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

            // Set PublicRelease - prioritize command line argument over environment variable
            if (publicReleaseArg.HasValue)
            {
                oracle.PublicRelease = publicReleaseArg.Value;
            }
            else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PublicRelease")) && bool.TryParse(Environment.GetEnvironmentVariable("PublicRelease"), out bool publicRelease))
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
            VersionOptions existingOptions = context.VersionFile.GetVersion(VersionFileRequirements.NonMergedResult | VersionFileRequirements.VersionSpecified | VersionFileRequirements.AcceptInheritingFile, out VersionFileLocations locations);
            string versionJsonPath;
            if (existingOptions is not null && locations.VersionSpecifyingVersionDirectory is not null)
            {
                existingOptions.Version = semver;
                versionJsonPath = context.VersionFile.SetVersion(locations.VersionSpecifyingVersionDirectory, existingOptions);
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

        private static Task<int> OnTagCommand(string project, string versionOrRef, bool whatIf)
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

            if (whatIf)
            {
                // In what-if mode, just output the tag name and exit
                Console.WriteLine(tagName);
                return Task.FromResult((int)ExitCodes.OK);
            }

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

            using var context = GitContext.Create(searchPath, engine: GitContext.Engine.ReadWrite);
            if (!context.IsRepository)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return Task.FromResult((int)ExitCodes.NoGitRepo);
            }

            IEnumerable<LibGit2Sharp.Commit> candidateCommits = LibGit2GitExtensions.GetCommitsFromVersion((LibGit2Context)context, parsedVersion);
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

        private static Task<int> OnPrepareReleaseCommand(string project, string nextVersion, string versionIncrement, string format, string tag, string unformattedCommitMessage, bool whatIf)
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
                if (!Enum.TryParse(versionIncrement, true, out VersionOptions.ReleaseVersionIncrement parsed))
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

            // run prepare-release or simulate
            try
            {
                var releaseManager = new ReleaseManager(Console.Out, Console.Error);

                ReleaseManager.ReleaseInfo releaseInfo = releaseManager.PrepareRelease(searchPath, tag, nextVersionParsed, versionIncrementParsed, outputMode, unformattedCommitMessage, whatIf);

                if (whatIf && outputMode == ReleaseManager.ReleaseManagerOutputMode.Json)
                {
                    releaseManager.WriteToOutput(releaseInfo);
                }

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

#nullable enable
        private static Task<int> OnPathFiltersUpdateCommand(string[] paths, string[] extraExtensions)
        {
            return OnPathFiltersCommandCore(paths, extraExtensions, updateMode: true);
        }

        private static Task<int> OnPathFiltersCheckCommand(string[] paths, string[] extraExtensions)
        {
            return OnPathFiltersCommandCore(paths, extraExtensions, updateMode: false);
        }

        private static Task<int> OnPathFiltersCommandCore(string[] paths, string[] extraExtensions, bool updateMode)
        {
            IReadOnlyList<string> projectExtensions = GetProjectExtensions(extraExtensions);
            bool anyFound = false;
            bool anyMismatch = false;

            // First, collect all version.json paths so we can use them for boundary checking
            IEnumerable<string> allVersionJsonPaths = FindVersionJsonPaths(paths).ToList();
            var allVersionJsonDirs = new HashSet<string>(
                allVersionJsonPaths.Select(p => Path.GetDirectoryName(p)!),
                StringComparer.OrdinalIgnoreCase);

            foreach (string versionJsonPath in allVersionJsonPaths)
            {
                anyFound = true;
                string versionJsonDir = Path.GetDirectoryName(versionJsonPath)!;

                using GitContext context = GitContext.Create(versionJsonDir, engine: GetEffectiveGitEngine());
                if (!context.IsRepository)
                {
                    Console.Error.WriteLine($"No git repository found for version.json at: {versionJsonPath}");
                    continue;
                }

                try
                {
                    IReadOnlyList<FilterPath> computed = ComputePathFilters(versionJsonDir, context.WorkingTreePath, projectExtensions, allVersionJsonDirs);

                    // Skip version.json files that have no associated projects
                    if (computed.Count == 0)
                    {
                        continue;
                    }

                    VersionOptions? versionOptions = context.VersionFile.GetWorkingCopyVersion(
                        VersionFileRequirements.NonMergedResult | VersionFileRequirements.AcceptInheritingFile);

                    if (versionOptions is null)
                    {
                        Console.Error.WriteLine($"Could not load version.json at: {versionJsonPath}");
                        continue;
                    }

                    if (updateMode)
                    {
                        versionOptions.PathFilters = computed.Count > 0 ? computed : null;
                        context.VersionFile.SetVersion(versionJsonDir, versionOptions);
                        Console.WriteLine($"Updated pathFilters in: {versionJsonPath}");
                    }
                    else
                    {
                        // Check mode: compare computed with existing
                        IReadOnlyList<FilterPath> existing = versionOptions.PathFilters ?? [];
                        var computedPaths = new SortedSet<string>(
                            computed.Select(f => f.RepoRelativePath),
                            StringComparer.OrdinalIgnoreCase);
                        var existingPaths = new SortedSet<string>(
                            existing.Select(f => f.RepoRelativePath),
                            StringComparer.OrdinalIgnoreCase);

                        if (!computedPaths.SetEquals(existingPaths))
                        {
                            anyMismatch = true;
                            Console.Error.WriteLine($"pathFilters mismatch in: {versionJsonPath}");

                            foreach (string m in computedPaths.Except(existingPaths, StringComparer.OrdinalIgnoreCase))
                            {
                                Console.Error.WriteLine($"  + missing: :/{m}");
                            }

                            foreach (string e in existingPaths.Except(computedPaths, StringComparer.OrdinalIgnoreCase))
                            {
                                Console.Error.WriteLine($"  - extra:   :/{e}");
                            }

                            Console.Error.WriteLine("Use the 'nbgv path-filters update' command to update the pathFilters.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error processing {versionJsonPath}: {ex.Message}");
                }
            }

            if (!anyFound)
            {
                Console.Error.WriteLine("No version.json files found.");
                return Task.FromResult((int)ExitCodes.NoVersionJsonFound);
            }

            return Task.FromResult(anyMismatch ? (int)ExitCodes.PathFiltersMismatch : (int)ExitCodes.OK);
        }

        /// <summary>
        /// Computes the <see cref="FilterPath"/> list for a given version.json directory
        /// by using the MSBuild project graph API to find the transitive closure of project references
        /// and the MSBuild evaluation model to find all imported files within the repository.
        /// </summary>
        /// <param name="versionJsonDir">The directory containing the version.json file.</param>
        /// <param name="repoRoot">The absolute path to the root of the git repository.</param>
        /// <param name="projectExtensions">The MSBuild project file extensions to search for.</param>
        /// <param name="versionJsonDirs">Set of all other version.json directories (for boundary checking).</param>
        /// <returns>A sorted, deduplicated list of repo-root-relative <see cref="FilterPath"/> objects, or empty if no projects found.</returns>
        private static IReadOnlyList<FilterPath> ComputePathFilters(
            string versionJsonDir,
            string repoRoot,
            IReadOnlyList<string> projectExtensions,
            ISet<string> versionJsonDirs)
        {
            // Find all project files under the version.json directory, but stop at other version.json directories.
            List<string> projectFiles = new();
            foreach (string ext in projectExtensions)
            {
                projectFiles.AddRange(FindProjectFilesRespectingBoundaries(versionJsonDir, ext, versionJsonDir, versionJsonDirs));
            }

            projectFiles = projectFiles
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (projectFiles.Count == 0)
            {
                // No projects under this version.json, return empty to signal it should be skipped
                return [];
            }

            var globalProperties = new Dictionary<string, string>
            {
                ["BuildingProject"] = "false",
            };

            // Use ProjectGraph to find the transitive closure of all project references.
            IEnumerable<ProjectGraphEntryPoint> entryPoints = projectFiles.Select(f => new ProjectGraphEntryPoint(f, globalProperties));
            ProjectGraph graph = new ProjectGraph(entryPoints);

            List<string> allProjectFiles = graph.ProjectNodes
                .Select(n => Path.GetFullPath(n.ProjectInstance.FullPath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // For each project in the transitive closure, add its directory to the results.
            // We include entire project directories rather than individual files.
            var results = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string projectFile in allProjectFiles)
            {
                if (IsWithinRepo(projectFile, repoRoot))
                {
                    // Add the project directory, not the individual .csproj file
                    string projectDir = Path.GetDirectoryName(projectFile)!;
                    results.Add(projectDir);
                }
            }

            // For MSBuild imports, we add specific imported files (not their directories)
            // since these are typically shared build files like Directory.Build.props
            using var collection = new ProjectCollection(globalProperties);
            foreach (string projectFile in allProjectFiles)
            {
                try
                {
                    Project project = collection.LoadProject(projectFile, globalProperties, toolsVersion: null);
                    foreach (ResolvedImport import in project.Imports)
                    {
                        string importPath = Path.GetFullPath(import.ImportedProject.FullPath);
                        if (IsWithinRepo(importPath, repoRoot))
                        {
                            // Skip files in obj and bin directories as they are generated
                            string normalizedPath = importPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                            if (normalizedPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                                normalizedPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                            {
                                continue;
                            }

                            results.Add(importPath);
                        }
                    }

                    collection.UnloadProject(project);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Could not load imports for {projectFile}: {ex.Message}");
                }
            }

            // Convert absolute paths to repo-root-relative FilterPath objects.
            return results
                .Select(p =>
                {
                    string repoRelative = Path.GetRelativePath(repoRoot, p).Replace('\\', '/');
                    return new FilterPath(":/" + repoRelative, string.Empty);
                })
                .ToList();
        }

        /// <summary>
        /// Recursively finds project files starting from searchDir, but stops descending into
        /// directories that contain their own version.json files (except for the root startDir).
        /// </summary>
        private static IEnumerable<string> FindProjectFilesRespectingBoundaries(
            string searchDir,
            string projectExtension,
            string startDir,
            ISet<string> versionJsonDirs)
        {
            // Find all projects in the current directory
            foreach (string file in Directory.EnumerateFiles(searchDir, "*" + projectExtension))
            {
                yield return file;
            }

            // Recursively search subdirectories, but skip those with their own version.json
            foreach (string subDir in Directory.EnumerateDirectories(searchDir))
            {
                // Skip if this subdirectory (or any ancestor) has a version.json (except the start directory)
                if (subDir != startDir && versionJsonDirs.Contains(subDir))
                {
                    continue;
                }

                foreach (string file in FindProjectFilesRespectingBoundaries(subDir, projectExtension, startDir, versionJsonDirs))
                {
                    yield return file;
                }
            }
        }

        /// <summary>
        /// Returns a sequence of version.json file paths found by interpreting each of the given
        /// input paths. Directories are searched recursively; a path directly to a version.json file
        /// is used as-is without recursive search.
        /// </summary>
        private static IEnumerable<string> FindVersionJsonPaths(string[]? paths)
        {
            IEnumerable<string> searchRoots = paths is { Length: > 0 }
                ? paths
                : [Directory.GetCurrentDirectory()];

            foreach (string path in searchRoots)
            {
                string fullPath = Path.GetFullPath(path);

                if (string.Equals(Path.GetFileName(fullPath), VersionFile.JsonFileName, StringComparison.OrdinalIgnoreCase))
                {
                    // Direct path to a version.json file – use it without recursive search.
                    if (File.Exists(fullPath))
                    {
                        yield return fullPath;
                    }
                    else
                    {
                        Console.Error.WriteLine($"File not found: {fullPath}");
                    }
                }
                else if (Directory.Exists(fullPath))
                {
                    // Directory – recursively find all version.json files.
                    foreach (string versionJson in Directory.EnumerateFiles(fullPath, VersionFile.JsonFileName, SearchOption.AllDirectories))
                    {
                        yield return versionJson;
                    }
                }
                else
                {
                    Console.Error.WriteLine($"Path not found: {fullPath}");
                }
            }
        }

        /// <summary>
        /// Gets the default project file extensions plus any extras provided on the command line.
        /// </summary>
        private static IReadOnlyList<string> GetProjectExtensions(string[]? extraExtensions)
        {
            var extensions = new List<string> { ".csproj", ".vbproj", ".fsproj", ".vcxproj" };
            if (extraExtensions is { Length: > 0 })
            {
                foreach (string ext in extraExtensions)
                {
                    string normalized = ext.StartsWith('.') ? ext : "." + ext;
                    if (!extensions.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    {
                        extensions.Add(normalized);
                    }
                }
            }

            return extensions;
        }

        /// <summary>
        /// Determines whether <paramref name="absolutePath"/> is located within the repository root.
        /// </summary>
        private static bool IsWithinRepo(string absolutePath, string repoRoot)
        {
            string normalizedPath = Path.GetFullPath(absolutePath);
            string normalizedRoot = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            StringComparison comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return normalizedPath.StartsWith(normalizedRoot, comparison);
        }

#nullable restore
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
