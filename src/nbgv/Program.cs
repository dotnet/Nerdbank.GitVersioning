namespace Nerdbank.GitVersioning.Tool
{
    using System;
    using System.Collections.Generic;
    using System.CommandLine;
    using System.ComponentModel.DataAnnotations;
    using System.IO;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    internal class Program
    {
        private const string DefaultVersionSpec = "1.0-beta";

        private const string DefaultVersionInfoFormat = "json";

        private enum ExitCodes
        {
            OK,
            NoGitRepo,
            InvalidVersionSpec,
            BadCloudVariable,
            DuplicateCloudVariable,
            NoCloudBuildEnvDetected,
            UnsupportedFormat,
        }


        private static ExitCodes exitCode;

        public static int Main(string[] args)
        {
            ArgumentSyntax.Parse(args, syntax =>
            {
                var commandText = string.Empty;
                var projectPath = string.Empty;
                var versionJsonRoot = string.Empty;
                var version = string.Empty;
                IReadOnlyList<string> cloudVariables = Array.Empty<string>();
                var format = string.Empty;

                var install = syntax.DefineCommand("install", ref commandText, "Prepares a project to have version stamps applied using Nerdbank.GitVersioning.");
                syntax.DefineOption("p|path", ref versionJsonRoot, "The path to the directory that should contain the version.json file. The default is the root of the git repo.");
                syntax.DefineOption("v|version", ref version, $"The initial version to set. The default is {DefaultVersionSpec}.");

                var getVersion = syntax.DefineCommand("get-version", ref commandText, "Gets the version information for a project.");
                syntax.DefineOption("p|project", ref projectPath, "The path to the project or project directory. The default is the current directory.");
                syntax.DefineOption("f|format", ref format, $"The format to write the version information. Allowed values are: json. The default is {DefaultVersionInfoFormat}.");

                var setVersion = syntax.DefineCommand("set-version", ref commandText, "Updates the version stamp that is applied to a project.");
                syntax.DefineOption("p|project", ref projectPath, "The path to the project or project directory. The default is the root directory of the repo that spans the current directory, or an existing version.json file, if applicable.");
                syntax.DefineParameter("version", ref version, "The version to set.");

                var cloud = syntax.DefineCommand("cloud", ref commandText, "Communicates with the ambient cloud build to set the build number and/or other cloud build variables.");
                syntax.DefineOption("p|project", ref projectPath, "The path to the project or project directory used to calculate the version. The default is the current directory. Ignored if the -v option is specified.");
                syntax.DefineOption("v|version", ref version, "The string to use for the cloud build number. If not specified, the computed version will be used.");
                syntax.DefineOptionList("d|define", ref cloudVariables, "Additional cloud build variables to define. Each should be in the NAME=VALUE syntax.");

                if (install.IsActive)
                {
                    exitCode = OnInstallCommand(versionJsonRoot, version);
                }
                else if (getVersion.IsActive)
                {
                    exitCode = OnGetVersionCommand(projectPath, format);
                }
                else if (setVersion.IsActive)
                {
                    exitCode = OnSetVersionCommand(projectPath, version);
                }
                else if (cloud.IsActive)
                {
                    exitCode = OnCloudCommand(projectPath, version, cloudVariables);
                }
            });

            return (int)exitCode;
        }

        private static ExitCodes OnInstallCommand(string versionJsonRoot, string version)
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

            var repository = GitExtensions.OpenGitRepo(searchPath);
            if (repository == null)
            {
                Console.Error.WriteLine("No git repo found at or above: \"{0}\"", searchPath);
                return ExitCodes.NoGitRepo;
            }

            if (string.IsNullOrEmpty(versionJsonRoot))
            {
                versionJsonRoot = repository.Info.WorkingDirectory;
            }

            var existingOptions = VersionFile.GetVersion(versionJsonRoot);
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
                string versionJsonPath= VersionFile.SetVersion(versionJsonRoot, options);
                LibGit2Sharp.Commands.Stage(repository, versionJsonPath);
            }

            // TODO: Add/modify Directory.Build.props to reference NB.GV package
            // TODO: git add Directory.Build.props.

            return ExitCodes.OK;
        }

        private static string GetSpecifiedOrCurrentDirectoryPath(string versionJsonRoot)
        {
            return Path.GetFullPath(string.IsNullOrEmpty(versionJsonRoot) ? "." : versionJsonRoot);
        }

        private static ExitCodes OnGetVersionCommand(string projectPath, string format)
        {
            if (string.IsNullOrEmpty(format))
            {
                format = DefaultVersionInfoFormat;
            }

            string searchPath = GetSpecifiedOrCurrentDirectoryPath(projectPath);
            var oracle = VersionOracle.Create(searchPath);
            switch (format.ToLowerInvariant())
            {
                case "json":
                    Console.WriteLine(JsonConvert.SerializeObject(oracle, Formatting.Indented));
                    break;
                default:
                    Console.Error.WriteLine("Unsupported format: {0}", format);
                    return ExitCodes.UnsupportedFormat;
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
            var repository = GitExtensions.OpenGitRepo(searchPath);
            var existingOptions = VersionFile.GetVersion(searchPath, out string actualDirectory);
            string versionJsonPath;
            if (existingOptions != null)
            {
                existingOptions.Version = semver;
                versionJsonPath = VersionFile.SetVersion(actualDirectory, existingOptions);
            }
            else if (string.IsNullOrEmpty(projectPath))
            {
                if (repository == null)
                {
                    Console.Error.WriteLine("No version file and no git repo found at or above: \"{0}\"", searchPath);
                    return ExitCodes.NoGitRepo;
                }

                versionJsonPath = VersionFile.SetVersion(repository.Info.WorkingDirectory, defaultOptions);
            }
            else
            {
                versionJsonPath = VersionFile.SetVersion(projectPath, defaultOptions);
            }

            if (repository != null)
            {
                LibGit2Sharp.Commands.Stage(repository, versionJsonPath);
            }

            return ExitCodes.OK;
        }

        private static ExitCodes OnCloudCommand(string projectPath, string version, IReadOnlyList<string> cloudVariables)
        {
            var variables = new Dictionary<string, string>();
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

                variables.Add(split[0], split[1]);
            }

            ICloudBuild activeCloudBuild = CloudBuild.Active;
            if (activeCloudBuild != null)
            {
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
    }
}
