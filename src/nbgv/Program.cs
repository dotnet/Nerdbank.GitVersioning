namespace Nerdbank.GitVersioning.Tool
{
    using System;
    using System.Collections.Generic;
    using System.CommandLine;
    using System.CommandLine.Binding;
    using System.CommandLine.Builder;
    using System.CommandLine.Collections;
    using System.CommandLine.Help;
    using System.CommandLine.Invocation;
    using System.CommandLine.IO;
    using System.CommandLine.Parsing;
    using System.CommandLine.Suggestions;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Build.Construction;
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
                new Option<string>(new[] { "--path", "-p" }, "The path to the directory that should contain the version.json file. The default is the root of the git repo."),
                new Option<string>(new[] { "--version", "-v" }, $"The initial version to set. The default is {DefaultVersionSpec}."),
                new Option<string>(new[] { "--source", "-s" }, $"The URI(s) of the NuGet package source(s) used to determine the latest stable version of the {PackageId} package. This setting overrides all of the sources specified in the NuGet.Config files."),
            };

            install.Handler = CommandHandler.Create((string path, string version, IReadOnlyList<string> source) => (int)OnInstallCommand(path, version, source));

            var getVersion = new Command("get-version", "Gets the version information for a project.")
            {
                new Option<string>(new[] { "--project", "-p" }, "The path to the project or project directory. The default is the current directory."),
                new Option<string[]>("--metadata", () => Array.Empty<string>(), "Adds an identifier to the build metadata part of a semantic version.")
                {
                    Argument = new Argument<string[]>
                    {
                        Arity = ArgumentArity.OneOrMore,
                    },
                },
                new Option<string>(new[] { "--format", "-f" }, $"The format to write the version information. Allowed values are: {string.Join(", ", SupportedFormats)}. The default is {DefaultOutputFormat}.").FromAmong(SupportedFormats),
                new Option<string>(new[] { "--variable", "-v" }, "The name of just one version property to print to stdout. When specified, the output is always in raw text. Useful in scripts."),
                new Argument<string>("commit-ish", $"The commit/ref to get the version information for. The default is {DefaultRef}.")
                {
                    Arity = ArgumentArity.ZeroOrOne,
                },
            };

            getVersion.Handler = CommandHandler.Create((string project, IReadOnlyList<string> metadata, string format, string variable, string commitIsh) => (int)OnGetVersionCommand(project, metadata, format, variable, commitIsh));

            var setVersion = new Command("set-version", "Updates the version stamp that is applied to a project.")
            {
                new Option<string>(new[] { "--project", "-p" }, "The path to the project or project directory. The default is the root directory of the repo that spans the current directory, or an existing version.json file, if applicable."),
                new Argument<string>("version", "The version to set."),
            };

            setVersion.Handler = CommandHandler.Create((string project, string version) => (int)OnSetVersionCommand(project, version));

            var tag = new Command("tag", "Creates a git tag to mark a version.")
            {
                new Option<string>(new[] { "--project", "-p" }, "The path to the project or project directory. The default is the root directory of the repo that spans the current directory, or an existing version.json file, if applicable."),
                new Argument<string>("versionOrRef",  $"The a.b.c[.d] version or git ref to be tagged. If not specified, {DefaultRef} is used.")
                {
                    Arity = ArgumentArity.ZeroOrOne,
                },
            };

            tag.Handler = CommandHandler.Create((string project, string versionOrRef) => (int)OnTagCommand(project, versionOrRef));

            var getCommits = new Command("get-commits", "Gets the commit(s) that match a given version.")
            {
                new Option<string>(new[] { "--project", "-p" }, "The path to the project or project directory. The default is the root directory of the repo that spans the current directory, or an existing version.json file, if applicable."),
                new Option<bool>(new[] { "--quiet", "-q" }, "Use minimal output."),
                new Argument<string>("version", "The a.b.c[.d] version to find."),
            };

            getCommits.Handler = CommandHandler.Create((string project, bool quiet, string version) => (int)OnGetCommitsCommand(project, version, quiet));

            var cloud = new Command("cloud", "Communicates with the ambient cloud build to set the build number and/or other cloud build variables.")
            {
                new Option<string>(new[] { "--project", "-p" }, "The path to the project or project directory used to calculate the version. The default is the current directory. Ignored if the -v option is specified."),
                new Option<string[]>("--metadata", () => Array.Empty<string>(), "Adds an identifier to the build metadata part of a semantic version.")
                {
                    Argument = new Argument<string[]>
                    {
                        Arity = ArgumentArity.OneOrMore,
                    },
                },
                new Option<string>(new[] { "--version", "-v" }, "The string to use for the cloud build number. If not specified, the computed version will be used."),
                new Option<string>(new[] { "--ci-system", "-s" }, "Force activation for a particular CI system. If not specified, auto-detection will be used. Supported values are: " + string.Join(", ", CloudProviderNames)).FromAmong(CloudProviderNames),
                new Option<bool>(new[] { "--all-vars", "-a" }, "Defines ALL version variables as cloud build variables, with a \"NBGV_\" prefix."),
                new Option<bool>(new[] { "--common-vars", "-c" }, "Defines a few common version variables as cloud build variables, with a \"Git\" prefix (e.g. GitBuildVersion, GitBuildVersionSimple, GitAssemblyInformationalVersion)."),
                new Option<string[]>(new[] { "--define", "-d" }, () => Array.Empty<string>(), "Additional cloud build variables to define. Each should be in the NAME=VALUE syntax."),
            };

            cloud.Handler = CommandHandler.Create((string project, IReadOnlyList<string> metadata, string version, string ciSystem, bool allVars, bool commonVars, IReadOnlyList<string> define) => (int)OnCloudCommand(project, metadata, version, ciSystem, allVars, commonVars, define));

            var prepareRelease = new Command("prepare-release", "Prepares a release by creating a release branch for the current version and adjusting the version on the current branch.")
            {
                new Option<string>(new[] { "--project", "-p" }, "The path to the project or project directory. The default is the current directory."),
                new Option<string>("--nextVersion", "The version to set for the current branch. If omitted, the next version is determined automatically by incrementing the current version."),
                new Option<string>("--versionIncrement", "Overrides the 'versionIncrement' setting set in version.json for determining the next version of the current branch."),
                new Option<string>(new[] { "--format", "-f" }, $"The format to write information about the release. Allowed values are: {string.Join(", ", SupportedFormats)}. The default is {DefaultOutputFormat}.").FromAmong(SupportedFormats),
                new Argument<string>("tag", "The prerelease tag to apply on the release branch (if any). If not specified, any existing prerelease tag will be removed. The preceding hyphen may be omitted.")
                {
                    Arity = ArgumentArity.ZeroOrOne,
                },
            };

            prepareRelease.Handler = CommandHandler.Create((string project, string nextVersion, string versionIncrement, string format, string tag) => (int)OnPrepareReleaseCommand(project, tag, nextVersion, versionIncrement, format));

            var root = new RootCommand($"{ThisAssembly.AssemblyName} {ThisAssembly.AssemblyInformationalVersion}")
            {
                install,
                getVersion,
                setVersion,
                tag,
                getCommits,
                cloud,
                prepareRelease,
            };

            var builder = new CommandLineBuilder(root);
            builder.UseMiddleware(context =>
            {
                // System.CommandLine 0.1 parsed arguments after optional --. Restore that behavior for compatibility.
                if (context.ParseResult.Errors.Count > 0 && context.ParseResult.UnparsedTokens.Count > 0)
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
            }, MiddlewareOrder.Default);

            return builder.Build();
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

        private static ExitCodes OnInstallCommand(string versionJsonRoot, string version, IReadOnlyList<string> sources)
        {
            if (!SemanticVersion.TryParse(string.IsNullOrEmpty(version) ? DefaultVersionSpec : version, out var semver))
            {
                Console.Error.WriteLine($"\"{version}\" is not a semver-compliant version spec.");
                return ExitCodes.InvalidVersionSpec;
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
            string searchPath = GetSpecifiedOrCurrentDirectoryPath(versionJsonRoot);
            if (!Directory.Exists(searchPath))
            {
                Console.Error.WriteLine("\"{0}\" is not an existing directory.", searchPath);
                return ExitCodes.NoGitRepo;
            }

            using var context = GitContext.Create(searchPath, writable: true);
            if (!context.IsRepository)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return ExitCodes.NoGitRepo;
            }

            if (string.IsNullOrEmpty(versionJsonRoot))
            {
                versionJsonRoot = context.WorkingTreePath;
            }

            var existingOptions = context.VersionFile.GetVersion();
            if (existingOptions != null)
            {
                if (!string.IsNullOrEmpty(version))
                {
                    var setVersionExitCode = OnSetVersionCommand(versionJsonRoot, version);
                    if (setVersionExitCode != ExitCodes.OK)
                    {
                        return setVersionExitCode;
                    }
                }
            }
            else
            {
                string versionJsonPath = context.VersionFile.SetVersion(versionJsonRoot, options);
                context.Stage(versionJsonPath);
            }

            // Create/update the Directory.Build.props file in the directory of the version.json file to add the NB.GV package.
            string directoryBuildPropsPath = Path.Combine(versionJsonRoot, "Directory.Build.props");
            ProjectRootElement propsFile = File.Exists(directoryBuildPropsPath)
                ? ProjectRootElement.Open(directoryBuildPropsPath)
                : ProjectRootElement.Create(directoryBuildPropsPath);

            const string PackageReferenceItemType = "PackageReference";
            if (!propsFile.Items.Any(i => i.ItemType == PackageReferenceItemType && i.Include == PackageId))
            {
                // Validate given sources
                foreach (var source in sources)
                {
                    // TODO: Can declare Option<Uri> to validate argument during parsing.
                    if (!Uri.TryCreate(source, UriKind.Absolute, out var _))
                    {
                        Console.Error.WriteLine($"\"{source}\" is not a valid NuGet package source.");
                        return ExitCodes.InvalidNuGetPackageSource;
                    }
                }

                string packageVersion = GetLatestPackageVersionAsync(PackageId, versionJsonRoot, sources).GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(packageVersion))
                {
                    string verifyPhrase = sources.Any()
                        ? "Please verify the given 'source' option(s)."
                        : "Please verify the package sources in the NuGet.Config files.";
                    Console.Error.WriteLine($"Latest stable version of the {PackageId} package could not be determined. " + verifyPhrase);
                    return ExitCodes.PackageIdNotFound;
                }

                var item = propsFile.AddItem(
                    PackageReferenceItemType,
                    PackageId,
                    new Dictionary<string, string>
                    {
                        { "Version", packageVersion },
                        { "PrivateAssets", "all" },
                    });
                item.Condition = "!Exists('packages.config')";

                propsFile.Save(directoryBuildPropsPath);
            }

            context.Stage(directoryBuildPropsPath);

            return ExitCodes.OK;
        }

        private static ExitCodes OnGetVersionCommand(string projectPath, IReadOnlyList<string> buildMetadata, string format, string singleVariable, string versionOrRef)
        {
            if (string.IsNullOrEmpty(format))
            {
                format = DefaultOutputFormat;
            }

            if (string.IsNullOrEmpty(versionOrRef))
            {
                versionOrRef = DefaultRef;
            }

            string searchPath = GetSpecifiedOrCurrentDirectoryPath(projectPath);

            using var context = GitContext.Create(searchPath, writable: AlwaysUseLibGit2);
            if (!context.IsRepository)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return ExitCodes.NoGitRepo;
            }

            if (!context.TrySelectCommit(versionOrRef))
            {
                Console.Error.WriteLine("rev-parse produced no commit for {0}", versionOrRef);
                return ExitCodes.BadGitRef;
            }

            var oracle = new VersionOracle(context, CloudBuild.Active);
            if (buildMetadata != null)
            {
                oracle.BuildMetadata.AddRange(buildMetadata);
            }

            // Take the PublicRelease environment variable into account, since the build would as well.
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PublicRelease")) && bool.TryParse(Environment.GetEnvironmentVariable("PublicRelease"), out bool publicRelease))
            {
                oracle.PublicRelease = publicRelease;
            }

            if (string.IsNullOrEmpty(singleVariable))
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
                        return ExitCodes.UnsupportedFormat;
                }
            }
            else
            {
                if (format != "text")
                {
                    Console.Error.WriteLine("Format must be \"text\" when querying for an individual variable's value.");
                    return ExitCodes.UnsupportedFormat;
                }

                var property = oracle.GetType().GetProperty(singleVariable, CaseInsensitiveFlags);
                if (property == null)
                {
                    Console.Error.WriteLine("Variable \"{0}\" not a version property.", singleVariable);
                    return ExitCodes.BadVariable;
                }

                Console.WriteLine(property.GetValue(oracle));
            }

            return ExitCodes.OK;
        }

        private static ExitCodes OnSetVersionCommand(string projectPath, string version)
        {
            if (!SemanticVersion.TryParse(string.IsNullOrEmpty(version) ? DefaultVersionSpec : version, out var semver))
            {
                Console.Error.WriteLine($"\"{version}\" is not a semver-compliant version spec.");
                return ExitCodes.InvalidVersionSpec;
            }

            var defaultOptions = new VersionOptions
            {
                Version = semver,
            };

            string searchPath = GetSpecifiedOrCurrentDirectoryPath(projectPath);
            using var context = GitContext.Create(searchPath, writable: true);
            var existingOptions = context.VersionFile.GetVersion(out string actualDirectory);
            string versionJsonPath;
            if (existingOptions != null)
            {
                existingOptions.Version = semver;
                versionJsonPath = context.VersionFile.SetVersion(actualDirectory, existingOptions);
            }
            else if (string.IsNullOrEmpty(projectPath))
            {
                if (!context.IsRepository)
                {
                    Console.Error.WriteLine("No version file and no git repo found at or above: \"{0}\"", searchPath);
                    return ExitCodes.NoGitRepo;
                }

                versionJsonPath = context.VersionFile.SetVersion(context.WorkingTreePath, defaultOptions);
            }
            else
            {
                versionJsonPath = context.VersionFile.SetVersion(projectPath, defaultOptions);
            }

            if (context.IsRepository)
            {
                context.Stage(versionJsonPath);
            }

            return ExitCodes.OK;
        }

        private static ExitCodes OnTagCommand(string projectPath, string versionOrRef)
        {
            if (string.IsNullOrEmpty(versionOrRef))
            {
                versionOrRef = DefaultRef;
            }

            string searchPath = GetSpecifiedOrCurrentDirectoryPath(projectPath);

            using var context = (LibGit2Context)GitContext.Create(searchPath, writable: true);
            if (context is null)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return ExitCodes.NoGitRepo;
            }

            var repository = context.Repository;
            if (!context.TrySelectCommit(versionOrRef))
            {
                if (!Version.TryParse(versionOrRef, out Version parsedVersion))
                {
                    Console.Error.WriteLine($"\"{versionOrRef}\" is not a simple a.b.c[.d] version spec or git reference.");
                    return ExitCodes.InvalidVersionSpec;
                }

                string repoRelativeProjectDir = GetRepoRelativePath(searchPath, repository);
                var candidateCommits = LibGit2GitExtensions.GetCommitsFromVersion(context, parsedVersion).ToList();
                if (candidateCommits.Count == 0)
                {
                    Console.Error.WriteLine("No commit with that version found.");
                    return ExitCodes.NoMatchingVersion;
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
                return ExitCodes.NoVersionJsonFound;
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
                return correctTag ? ExitCodes.OK : ExitCodes.TagConflict;
            }

            Console.WriteLine("{0} tag created at {1}.", tagName, context.GitCommitId);
            Console.WriteLine("Remember to push to a remote: git push origin {0}", tagName);

            return ExitCodes.OK;
        }

        private static ExitCodes OnGetCommitsCommand(string projectPath, string version, bool quiet)
        {
            if (!Version.TryParse(version, out Version parsedVersion))
            {
                Console.Error.WriteLine($"\"{version}\" is not a simple a.b.c[.d] version spec.");
                return ExitCodes.InvalidVersionSpec;
            }

            string searchPath = GetSpecifiedOrCurrentDirectoryPath(projectPath);

            using var context = (LibGit2Context)GitContext.Create(searchPath, writable: true);
            if (!context.IsRepository)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return ExitCodes.NoGitRepo;
            }

            var candidateCommits = LibGit2GitExtensions.GetCommitsFromVersion(context, parsedVersion);
            PrintCommits(quiet, context, candidateCommits);

            return ExitCodes.OK;
        }

        private static ExitCodes OnCloudCommand(string projectPath, IReadOnlyList<string> buildMetadata, string version, string cisystem, bool cloudBuildAllVars, bool cloudBuildCommonVars, IReadOnlyList<string> cloudVariables)
        {
            string searchPath = GetSpecifiedOrCurrentDirectoryPath(projectPath);
            if (!Directory.Exists(searchPath))
            {
                Console.Error.WriteLine("\"{0}\" is not an existing directory.", searchPath);
                return ExitCodes.NoGitRepo;
            }

            ICloudBuild activeCloudBuild = CloudBuild.Active;
            if (!string.IsNullOrEmpty(cisystem))
            {
                int matchingIndex = Array.FindIndex(CloudProviderNames, m => string.Equals(m, cisystem, StringComparison.OrdinalIgnoreCase));
                if (matchingIndex == -1)
                {
                    Console.Error.WriteLine("No cloud provider found by the name: \"{0}\"", cisystem);
                    return ExitCodes.NoCloudBuildProviderMatch;
                }

                activeCloudBuild = CloudBuild.SupportedCloudBuilds[matchingIndex];
            }

            using var context = GitContext.Create(searchPath, writable: AlwaysUseLibGit2);
            var oracle = new VersionOracle(context, cloudBuild: activeCloudBuild);
            if (buildMetadata != null)
            {
                oracle.BuildMetadata.AddRange(buildMetadata);
            }

            var variables = new Dictionary<string, string>();
            if (cloudBuildAllVars)
            {
                foreach (var pair in oracle.CloudBuildAllVars)
                {
                    variables.Add(pair.Key, pair.Value);
                }
            }

            if (cloudBuildCommonVars)
            {
                foreach (var pair in oracle.CloudBuildVersionVars)
                {
                    variables.Add(pair.Key, pair.Value);
                }
            }

            foreach (string def in cloudVariables)
            {
                string[] split = def.Split(new char[] { '=' }, 2);
                if (split.Length < 2)
                {
                    Console.Error.WriteLine($"\"{def}\" is not in the NAME=VALUE syntax required for cloud variables.");
                    return ExitCodes.BadCloudVariable;
                }

                if (variables.ContainsKey(split[0]))
                {
                    Console.Error.WriteLine($"Cloud build variable \"{split[0]}\" specified more than once.");
                    return ExitCodes.DuplicateCloudVariable;
                }

                variables[split[0]] = split[1];
            }

            if (activeCloudBuild != null)
            {
                if (string.IsNullOrEmpty(version))
                {
                    version = oracle.CloudBuildNumber;
                }

                activeCloudBuild.SetCloudBuildNumber(version, Console.Out, Console.Error);

                foreach (var pair in variables)
                {
                    activeCloudBuild.SetCloudBuildVariable(pair.Key, pair.Value, Console.Out, Console.Error);
                }

                return ExitCodes.OK;
            }
            else
            {
                Console.Error.WriteLine("No cloud build detected.");
                return ExitCodes.NoCloudBuildEnvDetected;
            }
        }

        private static ExitCodes OnPrepareReleaseCommand(string projectPath, string prereleaseTag, string nextVersion, string versionIncrement, string format)
        {
            // validate project path property
            string searchPath = GetSpecifiedOrCurrentDirectoryPath(projectPath);
            if (!Directory.Exists(searchPath))
            {
                Console.Error.WriteLine($"\"{searchPath}\" is not an existing directory.");
                return ExitCodes.NoGitRepo;
            }

            // nextVersion and versionIncrement parameters cannot be combined
            if (!string.IsNullOrEmpty(nextVersion) && !string.IsNullOrEmpty(versionIncrement))
            {
                Console.Error.WriteLine("Options 'nextVersion' and 'versionIncrement' cannot be used at the same time.");
                return ExitCodes.InvalidParameters;
            }

            // parse versionIncrement if parameter was specified
            VersionOptions.ReleaseVersionIncrement? versionIncrementParsed = default;
            if (!string.IsNullOrEmpty(versionIncrement))
            {
                if (!Enum.TryParse<VersionOptions.ReleaseVersionIncrement>(versionIncrement, true, out var parsed))
                {
                    Console.Error.WriteLine($"\"{versionIncrement}\" is not a valid version increment");
                    return ExitCodes.InvalidVersionIncrement;
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
                    return ExitCodes.InvalidVersionSpec;
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
                return ExitCodes.UnsupportedFormat;
            }

            // run prepare-release
            try
            {
                var releaseManager = new ReleaseManager(Console.Out, Console.Error);
                releaseManager.PrepareRelease(searchPath, prereleaseTag, nextVersionParsed, versionIncrementParsed, outputMode);
                return ExitCodes.OK;
            }
            catch (ReleaseManager.ReleasePreparationException ex)
            {
                // map error codes
                switch (ex.Error)
                {
                    case ReleaseManager.ReleasePreparationError.NoGitRepo:
                        return ExitCodes.NoGitRepo;
                    case ReleaseManager.ReleasePreparationError.UncommittedChanges:
                        return ExitCodes.UncommittedChanges;
                    case ReleaseManager.ReleasePreparationError.InvalidBranchNameSetting:
                        return ExitCodes.InvalidBranchNameSetting;
                    case ReleaseManager.ReleasePreparationError.NoVersionFile:
                        return ExitCodes.NoVersionJsonFound;
                    case ReleaseManager.ReleasePreparationError.VersionDecrement:
                        return ExitCodes.InvalidVersionSpec;
                    case ReleaseManager.ReleasePreparationError.BranchAlreadyExists:
                        return ExitCodes.BranchAlreadyExists;
                    case ReleaseManager.ReleasePreparationError.UserNotConfigured:
                        return ExitCodes.UserNotConfigured;
                    case ReleaseManager.ReleasePreparationError.DetachedHead:
                        return ExitCodes.DetachedHead;
                    case ReleaseManager.ReleasePreparationError.InvalidVersionIncrementSetting:
                        return ExitCodes.InvalidVersionIncrementSetting;
                    default:
                        Report.Fail($"{nameof(ReleaseManager.ReleasePreparationError)}: {ex.Error}");
                        return (ExitCodes)(-1);
                }
            }
        }

        private static async Task<string> GetLatestPackageVersionAsync(string packageId, string root, IReadOnlyList<string> sources, CancellationToken cancellationToken = default)
        {
            var settings = Settings.LoadDefaultSettings(root);

            var providers = new List<Lazy<INuGetResourceProvider>>();
            providers.AddRange(Repository.Provider.GetCoreV3());  // Add v3 API support

            var sourceRepositoryProvider = new SourceRepositoryProvider(settings, providers);

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
