namespace Nerdbank.GitVersioning.Tool
{
    using System;
    using System.Collections.Generic;
    using System.CommandLine;
    using System.CommandLine.Builder;
    using System.CommandLine.Invocation;
    using System.CommandLine.Parsing;
    using System.IO;
    using System.Linq;
    using System.Reflection;
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

    internal class Program
    {
        private const string DefaultVersionSpec = "1.0-beta";

        private const string DefaultOutputFormat = "text";

        private const string DefaultRef = "HEAD";

        private const string PackageId = "Nerdbank.GitVersioning";

        private const BindingFlags CaseInsensitiveFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.IgnoreCase;

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
        }

        private static readonly string[] SupportedFormats = new[] { "text", "json" };
        private static ExitCodes exitCode;

        private static bool AlwaysUseLibGit2 => string.Equals(Environment.GetEnvironmentVariable("NBGV_GitEngine"), "LibGit2", StringComparison.Ordinal);

        public static int Main(string[] args)
        {
            string thisAssemblyPath = new Uri(typeof(Program).GetTypeInfo().Assembly.CodeBase).LocalPath;

            Assembly inContextAssembly = GitLoaderContext.Instance.LoadFromAssemblyPath(thisAssemblyPath);
            Type innerProgramType = inContextAssembly.GetType(typeof(Program).FullName);
            object innerProgram = Activator.CreateInstance(innerProgramType);

            var mainInnerMethod = innerProgramType.GetMethod(nameof(MainInner), BindingFlags.Static | BindingFlags.NonPublic);
            int result = (int)mainInnerMethod.Invoke(null, new object[] { args });
            return result;
        }

        private static Parser BuildCommandLine()
        {
            var install = new Command("install", "Prepares a project to have version stamps applied using Nerdbank.GitVersioning.")
            {
                new Option<string>(new[] { "--path", "-p" }, "The path to the directory that should contain the version.json file. The default is the root of the git repo.").LegalFilePathsOnly(),
                new Option<string>(new[] { "--version", "-v" }, () => DefaultVersionSpec, $"The initial version to set."),
                new Option<string[]>(new[] { "--source", "-s" }, $"The URI(s) of the NuGet package source(s) used to determine the latest stable version of the {PackageId} package. This setting overrides all of the sources specified in the NuGet.Config files.")
                {
                    Argument = new Argument<string[]>(() => Array.Empty<string>())
                    {
                        Arity = ArgumentArity.OneOrMore,
                    },
                },
            };

            install.Handler = CommandHandler.Create<string, string, IReadOnlyList<string>>(OnInstallCommand);

            var getVersion = new Command("get-version", "Gets the version information for a project.")
            {
                new Option<string>(new[] { "--project", "-p" }, "The path to the project or project directory. The default is the current directory.").LegalFilePathsOnly(),
                new Option<string[]>("--metadata", "Adds an identifier to the build metadata part of a semantic version.")
                {
                    Argument = new Argument<string[]>(() => Array.Empty<string>())
                    {
                        Arity = ArgumentArity.OneOrMore,
                    },
                },
                new Option<string>(new[] { "--format", "-f" }, $"The format to write the version information. Allowed values are: {string.Join(", ", SupportedFormats)}. The default is {DefaultOutputFormat}.").FromAmong(SupportedFormats),
                new Option<string>(new[] { "--variable", "-v" }, "The name of just one version property to print to stdout. When specified, the output is always in raw text. Useful in scripts."),
                new Argument<string>("commit-ish", () => DefaultRef, $"The commit/ref to get the version information for.")
                {
                    Arity = ArgumentArity.ZeroOrOne,
                },
            };

            getVersion.Handler = CommandHandler.Create<string, IReadOnlyList<string>, string, string, string>(OnGetVersionCommand);

            var setVersion = new Command("set-version", "Updates the version stamp that is applied to a project.")
            {
                new Option<string>(new[] { "--project", "-p" }, "The path to the project or project directory. The default is the root directory of the repo that spans the current directory, or an existing version.json file, if applicable.").LegalFilePathsOnly(),
                new Argument<string>("version", "The version to set."),
            };

            setVersion.Handler = CommandHandler.Create<string, string>(OnSetVersionCommand);

            var tag = new Command("tag", "Creates a git tag to mark a version.")
            {
                new Option<string>(new[] { "--project", "-p" }, "The path to the project or project directory. The default is the root directory of the repo that spans the current directory, or an existing version.json file, if applicable.").LegalFilePathsOnly(),
                new Argument<string>("versionOrRef", () => DefaultRef, $"The a.b.c[.d] version or git ref to be tagged.")
                {
                    Arity = ArgumentArity.ZeroOrOne,
                },
            };

            tag.Handler = CommandHandler.Create<string, string>(OnTagCommand);

            var getCommits = new Command("get-commits", "Gets the commit(s) that match a given version.")
            {
                new Option<string>(new[] { "--project", "-p" }, "The path to the project or project directory. The default is the root directory of the repo that spans the current directory, or an existing version.json file, if applicable.").LegalFilePathsOnly(),
                new Option<bool>(new[] { "--quiet", "-q" }, "Use minimal output."),
                new Argument<string>("version", "The a.b.c[.d] version to find."),
            };

            getCommits.Handler = CommandHandler.Create<string, bool, string>(OnGetCommitsCommand);

            var cloud = new Command("cloud", "Communicates with the ambient cloud build to set the build number and/or other cloud build variables.")
            {
                new Option<string>(new[] { "--project", "-p" }, "The path to the project or project directory used to calculate the version. The default is the current directory. Ignored if the -v option is specified.").LegalFilePathsOnly(),
                new Option<string[]>("--metadata", "Adds an identifier to the build metadata part of a semantic version.")
                {
                    Argument = new Argument<string[]>(() => Array.Empty<string>())
                    {
                        Arity = ArgumentArity.OneOrMore,
                    },
                },
                new Option<string>(new[] { "--version", "-v" }, "The string to use for the cloud build number. If not specified, the computed version will be used."),
                new Option<string>(new[] { "--ci-system", "-s" }, "Force activation for a particular CI system. If not specified, auto-detection will be used. Supported values are: " + string.Join(", ", CloudProviderNames)).FromAmong(CloudProviderNames),
                new Option<bool>(new[] { "--all-vars", "-a" }, "Defines ALL version variables as cloud build variables, with a \"NBGV_\" prefix."),
                new Option<bool>(new[] { "--common-vars", "-c" }, "Defines a few common version variables as cloud build variables, with a \"Git\" prefix (e.g. GitBuildVersion, GitBuildVersionSimple, GitAssemblyInformationalVersion)."),
                new Option<string[]>(new[] { "--define", "-d" }, "Additional cloud build variables to define. Each should be in the NAME=VALUE syntax.")
                {
                    Argument = new Argument<string[]>(() => Array.Empty<string>())
                    {
                        Arity = ArgumentArity.OneOrMore,
                    },
                },
            };

            cloud.Handler = CommandHandler.Create<string, IReadOnlyList<string>, string, string, bool, bool, IReadOnlyList<string>>(OnCloudCommand);

            var prepareRelease = new Command("prepare-release", "Prepares a release by creating a release branch for the current version and adjusting the version on the current branch.")
            {
                new Option<string>(new[] { "--project", "-p" }, "The path to the project or project directory. The default is the current directory.").LegalFilePathsOnly(),
                new Option<string>("--nextVersion", "The version to set for the current branch. If omitted, the next version is determined automatically by incrementing the current version."),
                new Option<string>("--versionIncrement", "Overrides the 'versionIncrement' setting set in version.json for determining the next version of the current branch."),
                new Option<string>(new[] { "--format", "-f" }, $"The format to write information about the release. Allowed values are: {string.Join(", ", SupportedFormats)}. The default is {DefaultOutputFormat}.").FromAmong(SupportedFormats),
                new Argument<string>("tag", "The prerelease tag to apply on the release branch (if any). If not specified, any existing prerelease tag will be removed. The preceding hyphen may be omitted.")
                {
                    Arity = ArgumentArity.ZeroOrOne,
                },
            };

            prepareRelease.Handler = CommandHandler.Create<string, string, string, string, string>(OnPrepareReleaseCommand);

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

            return new CommandLineBuilder(root)
                .UseDefaults()
                .UseMiddleware(context =>
                {
                    // System.CommandLine 0.1 parsed arguments after optional --. Restore that behavior for compatibility.
                    // TODO: Remove this middleware when https://github.com/dotnet/command-line-api/issues/1238 is resolved.
                    if (context.ParseResult.UnparsedTokens.Count > 0)
                    {
                        var arguments = context.ParseResult.CommandResult.Command.Arguments;
                        if (arguments.Count() == context.ParseResult.UnparsedTokens.Count)
                        {
                            context.ParseResult = context.Parser.Parse(
                                context.ParseResult.Tokens
                                    .Where(token => token.Type != TokenType.EndOfArguments)
                                    .Select(token => token.Value)
                                    .ToArray());
                        }
                    }
                }, (MiddlewareOrder)(-3000)) // MiddlewareOrderInternal.ExceptionHandler so [parse] directive is accurate.
                .UseExceptionHandler((ex, context) => PrintException(ex, context))
                .Build();
        }

        private static void PrintException(Exception ex, InvocationContext context)
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
                var parser = BuildCommandLine();
                exitCode = (ExitCodes)parser.Invoke(args);
            }
            catch (GitException ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                exitCode = ex.ErrorCode switch
                {
                    GitException.ErrorCodes.ObjectNotFound when ex.iSShallowClone => ExitCodes.ShallowClone,
                    _ => ExitCodes.InternalError,
                };
            }

            return (int)exitCode;
        }

        private static int OnInstallCommand(string path, string version, IReadOnlyList<string> source)
        {
            if (!SemanticVersion.TryParse(string.IsNullOrEmpty(version) ? DefaultVersionSpec : version, out var semver))
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

            using var context = GitContext.Create(searchPath, writable: true);
            if (!context.IsRepository)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return (int)ExitCodes.NoGitRepo;
            }

            if (string.IsNullOrEmpty(path))
            {
                path = context.WorkingTreePath;
            }

            var existingOptions = context.VersionFile.GetVersion();
            if (existingOptions is not null)
            {
                if (!string.IsNullOrEmpty(version) && version != DefaultVersionSpec)
                {
                    var setVersionExitCode = OnSetVersionCommand(path, version);
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
            foreach (var src in source)
            {
                // TODO: Can declare Option<Uri> to validate argument during parsing.
                if (!Uri.TryCreate(src, UriKind.Absolute, out var _))
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

            var item = propsFile.Items.FirstOrDefault(i => i.ItemType == PackageReferenceItemType && i.Include == PackageId);

            if (item is null)
            {
                item = propsFile.AddItem(
                    PackageReferenceItemType,
                    PackageId,
                    new Dictionary<string, string>
                    {
                        { PrivateAssetsMetadataName, "all" },
                        { VersionMetadataName, packageVersion }
                    });
            }
            else
            {
                var versionMetadata = item.Metadata.Single(m => m.Name == VersionMetadataName);
                versionMetadata.Value = packageVersion;
            }

            item.Condition = "!Exists('packages.config')";

            propsFile.Save(directoryBuildPropsPath);
            context.Stage(directoryBuildPropsPath);

            return (int)ExitCodes.OK;
        }

        private static int OnGetVersionCommand(string project, IReadOnlyList<string> metadata, string format, string variable, string commitish)
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

            using var context = GitContext.Create(searchPath, writable: AlwaysUseLibGit2);
            if (!context.IsRepository)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return (int)ExitCodes.NoGitRepo;
            }

            if (!context.TrySelectCommit(commitish))
            {
                Console.Error.WriteLine("rev-parse produced no commit for {0}", commitish);
                return (int)ExitCodes.BadGitRef;
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
                        return (int)ExitCodes.UnsupportedFormat;
                }
            }
            else
            {
                if (format != "text")
                {
                    Console.Error.WriteLine("Format must be \"text\" when querying for an individual variable's value.");
                    return (int)ExitCodes.UnsupportedFormat;
                }

                var property = oracle.GetType().GetProperty(variable, CaseInsensitiveFlags);
                if (property is null)
                {
                    Console.Error.WriteLine("Variable \"{0}\" not a version property.", variable);
                    return (int)ExitCodes.BadVariable;
                }

                Console.WriteLine(property.GetValue(oracle));
            }

            return (int)ExitCodes.OK;
        }

        private static int OnSetVersionCommand(string project, string version)
        {
            if (!SemanticVersion.TryParse(string.IsNullOrEmpty(version) ? DefaultVersionSpec : version, out var semver))
            {
                Console.Error.WriteLine($"\"{version}\" is not a semver-compliant version spec.");
                return (int)ExitCodes.InvalidVersionSpec;
            }

            var defaultOptions = new VersionOptions
            {
                Version = semver,
            };

            string searchPath = GetSpecifiedOrCurrentDirectoryPath(project);
            using var context = GitContext.Create(searchPath, writable: true);
            var existingOptions = context.VersionFile.GetVersion(out string actualDirectory);
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
                    return (int)ExitCodes.NoGitRepo;
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

            return (int)ExitCodes.OK;
        }

        private static int OnTagCommand(string project, string versionOrRef)
        {
            if (string.IsNullOrEmpty(versionOrRef))
            {
                versionOrRef = DefaultRef;
            }

            string searchPath = GetSpecifiedOrCurrentDirectoryPath(project);

            using var context = (LibGit2Context)GitContext.Create(searchPath, writable: true);
            if (context is null)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return (int)ExitCodes.NoGitRepo;
            }

            var repository = context.Repository;
            if (!context.TrySelectCommit(versionOrRef))
            {
                if (!Version.TryParse(versionOrRef, out Version parsedVersion))
                {
                    Console.Error.WriteLine($"\"{versionOrRef}\" is not a simple a.b.c[.d] version spec or git reference.");
                    return (int)ExitCodes.InvalidVersionSpec;
                }

                string repoRelativeProjectDir = GetRepoRelativePath(searchPath, repository);
                var candidateCommits = LibGit2GitExtensions.GetCommitsFromVersion(context, parsedVersion).ToList();
                if (candidateCommits.Count == 0)
                {
                    Console.Error.WriteLine("No commit with that version found.");
                    return (int)ExitCodes.NoMatchingVersion;
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
                return (int)ExitCodes.NoVersionJsonFound;
            }

            oracle.PublicRelease = true; // assume a public release so we don't get a redundant -gCOMMITID in the tag name
            string tagName = $"v{oracle.SemVer2}";
            try
            {
                context.ApplyTag(tagName);
            }
            catch (LibGit2Sharp.NameConflictException)
            {
                var taggedCommit = repository.Tags[tagName].Target as LibGit2Sharp.Commit;
                bool correctTag = taggedCommit?.Sha == context.GitCommitId;
                Console.Error.WriteLine("The tag {0} is already defined ({1}).", tagName, correctTag ? "to the right commit" : $"expected {context.GitCommitId} but was on {taggedCommit.Sha}");
                return (int)(correctTag ? ExitCodes.OK : ExitCodes.TagConflict);
            }

            Console.WriteLine("{0} tag created at {1}.", tagName, context.GitCommitId);
            Console.WriteLine("Remember to push to a remote: git push origin {0}", tagName);

            return (int)ExitCodes.OK;
        }

        private static int OnGetCommitsCommand(string project, bool quiet, string version)
        {
            if (!Version.TryParse(version, out Version parsedVersion))
            {
                Console.Error.WriteLine($"\"{version}\" is not a simple a.b.c[.d] version spec.");
                return (int)ExitCodes.InvalidVersionSpec;
            }

            string searchPath = GetSpecifiedOrCurrentDirectoryPath(project);

            using var context = (LibGit2Context)GitContext.Create(searchPath, writable: true);
            if (!context.IsRepository)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return (int)ExitCodes.NoGitRepo;
            }

            var candidateCommits = LibGit2GitExtensions.GetCommitsFromVersion(context, parsedVersion);
            PrintCommits(quiet, context, candidateCommits);

            return (int)ExitCodes.OK;
        }

        private static int OnCloudCommand(string project, IReadOnlyList<string> metadata, string version, string ciSystem, bool allVars, bool commonVars, IReadOnlyList<string> define)
        {
            string searchPath = GetSpecifiedOrCurrentDirectoryPath(project);
            if (!Directory.Exists(searchPath))
            {
                Console.Error.WriteLine("\"{0}\" is not an existing directory.", searchPath);
                return (int)ExitCodes.NoGitRepo;
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
                        return (int)ExitCodes.BadCloudVariable;
                    }

                    if (additionalVariables.ContainsKey(split[0]))
                    {
                        Console.Error.WriteLine($"Cloud build variable \"{split[0]}\" specified more than once.");
                        return (int)ExitCodes.DuplicateCloudVariable;
                    }

                    additionalVariables[split[0]] = split[1];
                }
            }

            try
            {
                var cloudCommand = new CloudCommand(Console.Out, Console.Error);
                cloudCommand.SetBuildVariables(searchPath, metadata, version, ciSystem, allVars, commonVars, additionalVariables, AlwaysUseLibGit2);
            }
            catch (CloudCommand.CloudCommandException ex)
            {
                Console.Error.WriteLine(ex.Message);
                // map error codes
                switch (ex.Error)
                {
                    case CloudCommand.CloudCommandError.NoCloudBuildProviderMatch:
                        return (int)ExitCodes.NoCloudBuildProviderMatch;
                    case CloudCommand.CloudCommandError.DuplicateCloudVariable:
                        return (int)ExitCodes.DuplicateCloudVariable;
                    case CloudCommand.CloudCommandError.NoCloudBuildEnvDetected:
                        return (int)ExitCodes.NoCloudBuildEnvDetected;
                    default:
                        Report.Fail($"{nameof(CloudCommand.CloudCommandError)}: {ex.Error}");
                        return -1;
                }
            }

            return (int)ExitCodes.OK;

        }

        private static int OnPrepareReleaseCommand(string project, string nextVersion, string versionIncrement, string format, string tag)
        {
            // validate project path property
            string searchPath = GetSpecifiedOrCurrentDirectoryPath(project);
            if (!Directory.Exists(searchPath))
            {
                Console.Error.WriteLine($"\"{searchPath}\" is not an existing directory.");
                return (int)ExitCodes.NoGitRepo;
            }

            // nextVersion and versionIncrement parameters cannot be combined
            if (!string.IsNullOrEmpty(nextVersion) && !string.IsNullOrEmpty(versionIncrement))
            {
                Console.Error.WriteLine("Options 'nextVersion' and 'versionIncrement' cannot be used at the same time.");
                return (int)ExitCodes.InvalidParameters;
            }

            // parse versionIncrement if parameter was specified
            VersionOptions.ReleaseVersionIncrement? versionIncrementParsed = default;
            if (!string.IsNullOrEmpty(versionIncrement))
            {
                if (!Enum.TryParse<VersionOptions.ReleaseVersionIncrement>(versionIncrement, true, out var parsed))
                {
                    Console.Error.WriteLine($"\"{versionIncrement}\" is not a valid version increment");
                    return (int)ExitCodes.InvalidVersionIncrement;
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
                    return (int)ExitCodes.InvalidVersionSpec;
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
                return (int)ExitCodes.UnsupportedFormat;
            }

            // run prepare-release
            try
            {
                var releaseManager = new ReleaseManager(Console.Out, Console.Error);
                releaseManager.PrepareRelease(searchPath, tag, nextVersionParsed, versionIncrementParsed, outputMode);
                return (int)ExitCodes.OK;
            }
            catch (ReleaseManager.ReleasePreparationException ex)
            {
                // map error codes
                switch (ex.Error)
                {
                    case ReleaseManager.ReleasePreparationError.NoGitRepo:
                        return (int)ExitCodes.NoGitRepo;
                    case ReleaseManager.ReleasePreparationError.UncommittedChanges:
                        return (int)ExitCodes.UncommittedChanges;
                    case ReleaseManager.ReleasePreparationError.InvalidBranchNameSetting:
                        return (int)ExitCodes.InvalidBranchNameSetting;
                    case ReleaseManager.ReleasePreparationError.NoVersionFile:
                        return (int)ExitCodes.NoVersionJsonFound;
                    case ReleaseManager.ReleasePreparationError.VersionDecrement:
                    case ReleaseManager.ReleasePreparationError.NoVersionIncrement:
                        return (int)ExitCodes.InvalidVersionSpec;
                    case ReleaseManager.ReleasePreparationError.BranchAlreadyExists:
                        return (int)ExitCodes.BranchAlreadyExists;
                    case ReleaseManager.ReleasePreparationError.UserNotConfigured:
                        return (int)ExitCodes.UserNotConfigured;
                    case ReleaseManager.ReleasePreparationError.DetachedHead:
                        return (int)ExitCodes.DetachedHead;
                    case ReleaseManager.ReleasePreparationError.InvalidVersionIncrementSetting:
                        return (int)ExitCodes.InvalidVersionIncrementSetting;
                    default:
                        Report.Fail($"{nameof(ReleaseManager.ReleasePreparationError)}: {ex.Error}");
                        return -1;
                }
            }
        }

        private static async Task<string> GetLatestPackageVersionAsync(string packageId, string root, IReadOnlyList<string> sources, CancellationToken cancellationToken = default)
        {
            var settings = Settings.LoadDefaultSettings(root);

            var providers = new List<Lazy<INuGetResourceProvider>>();
            providers.AddRange(Repository.Provider.GetCoreV3());  // Add v3 API support

            var sourceRepositoryProvider = new SourceRepositoryProvider(new PackageSourceProvider(settings), providers);

            // Select package sources based on NuGet.Config files or given options, as 'nuget.exe restore' command does
            // See also 'DownloadCommandBase.GetPackageSources(ISettings)' at https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Clients/NuGet.CommandLine/Commands/DownloadCommandBase.cs
            var availableSources = sourceRepositoryProvider.PackageSourceProvider.LoadPackageSources().Where(s => s.IsEnabled);
            var packageSources = new List<PackageSource>();

            foreach (var source in sources)
            {
                var resolvedSource = availableSources.FirstOrDefault(s => s.Source.Equals(source, StringComparison.OrdinalIgnoreCase) || s.Name.Equals(source, StringComparison.OrdinalIgnoreCase));
                packageSources.Add(resolvedSource ?? new PackageSource(source));
            }

            if (sources.Count == 0)
            {
                packageSources.AddRange(availableSources);
            }

            var sourceRepositories = packageSources.Select(sourceRepositoryProvider.CreateRepository).ToArray();
            var resolutionContext = new ResolutionContext(
                DependencyBehavior.Highest,
                includePrelease: false,
                includeUnlisted: false,
                VersionConstraints.None);

            // The target framework doesn't matter, since our package doesn't depend on this for its target projects.
            var framework = new NuGet.Frameworks.NuGetFramework("net45");

            var pkg = await NuGetPackageManager.GetLatestVersionAsync(
                packageId,
                framework,
                resolutionContext,
                sourceRepositories,
                NullLogger.Instance,
                cancellationToken);

            return pkg.LatestVersion?.ToNormalizedString();
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
            foreach (var commit in candidateCommits)
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

        private static string[] CloudProviderNames => CloudBuild.SupportedCloudBuilds.Select(cb => cb.GetType().Name).ToArray();
    }
}
