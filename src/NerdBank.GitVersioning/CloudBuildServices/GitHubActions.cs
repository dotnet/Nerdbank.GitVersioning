// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using Validation;

namespace Nerdbank.GitVersioning.CloudBuildServices;

internal class GitHubActions : ICloudBuild
{
    /// <summary>
    /// The encoding GitHub Actions uses to read the file identified by the <c>GITHUB_ENV</c> environment variable.
    /// </summary>
    private static readonly Encoding EnvironmentFileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc/>
    public bool IsApplicable => Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

    /// <inheritdoc/>
    public bool IsPullRequest => Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") == "PullRequestEvent";

    /// <inheritdoc/>
    public string BuildingBranch => (BuildingRef?.StartsWith("refs/heads/") ?? false) ? BuildingRef : null;

    /// <inheritdoc/>
    public string BuildingTag => (BuildingRef?.StartsWith("refs/tags/") ?? false) ? BuildingRef : null;

    /// <inheritdoc/>
    public string GitCommitId => IgnoreGitHubRef ? null : Environment.GetEnvironmentVariable("GITHUB_SHA");

    private static string BuildingRef => IgnoreGitHubRef ? null : Environment.GetEnvironmentVariable("GITHUB_REF");

    /// <summary>
    /// Gets a value indicating whether to ignore GitHub Actions environment variables that indicate where HEAD is.
    /// </summary>
    /// <remarks>
    /// This is useful in a GitHub workflow where HEAD was moved by some prior Action, such that the environment variables are stale.
    /// GitHub Actions does not allow these env vars to be changed mid-workflow, so in such cases NB.GV should just use HEAD.
    /// </remarks>
    private static bool IgnoreGitHubRef => string.Equals(Environment.GetEnvironmentVariable("IGNORE_GITHUB_REF"), "true", StringComparison.OrdinalIgnoreCase);

    private static string EnvironmentFile => Environment.GetEnvironmentVariable("GITHUB_ENV");

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber, TextWriter stdout, TextWriter stderr)
    {
        return new Dictionary<string, string>();
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name, string value, TextWriter stdout, TextWriter stderr)
    {
        AppendVariable(EnvironmentFile, name, value);
        return GetDictionaryFor(name, value);
    }

    /// <summary>
    /// Appends a variable assignment to a GitHub Actions environment file.
    /// </summary>
    /// <param name="environmentFilePath">The path to the file identified by the <c>GITHUB_ENV</c> environment variable.</param>
    /// <param name="name">The name of the variable to set. Must not be empty nor contain a newline.</param>
    /// <param name="value">The value to assign to the variable. May be <see langword="null" />, empty, or contain newlines.</param>
    /// <remarks>
    /// <para>
    /// Several MSBuild processes may run concurrently within one GitHub Actions step (e.g. when building
    /// many projects or target frameworks in parallel) and every one of them may append to the same file.
    /// This method therefore takes an exclusive lock on the file for the duration of the append.
    /// </para>
    /// <para>
    /// The lock is essential on non-Windows platforms because .NET resolves <see cref="FileMode.Append" />
    /// to a seek-to-end at the time the file is opened rather than to the <c>O_APPEND</c> open flag.
    /// Concurrent appenders would otherwise each write at the same stale offset, silently overwriting one
    /// another and leaving torn lines behind that GitHub Actions rejects with the error
    /// <c>Unable to process file command 'env' successfully</c>.
    /// </para>
    /// </remarks>
    internal static void AppendVariable(string environmentFilePath, string name, string value)
    {
        Requires.NotNullOrEmpty(environmentFilePath, nameof(environmentFilePath));
        Requires.NotNullOrEmpty(name, nameof(name));
        Requires.Argument(name.IndexOf('\n') < 0 && name.IndexOf('\r') < 0, nameof(name), "Variable names must not contain newlines.");

        byte[] bytes = EnvironmentFileEncoding.GetBytes(FormatVariable(name, value ?? string.Empty));

        Utilities.FileOperationWithRetry(() =>
        {
            // FileShare.None asks for an exclusive lock, which .NET honors on non-Windows platforms
            // by way of an advisory flock on the file descriptor.
            using FileStream stream = new(environmentFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            // Guard against a prior writer that did not terminate its last line,
            // which would otherwise merge that line with ours and corrupt both.
            if (stream.Length > 0)
            {
                stream.Seek(-1, SeekOrigin.End);
                if (stream.ReadByte() != '\n')
                {
                    stream.WriteByte((byte)'\n');
                }
            }

            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        });
    }

    /// <summary>
    /// Formats a variable assignment using the syntax that GitHub Actions expects in the <c>GITHUB_ENV</c> file.
    /// </summary>
    /// <param name="name">The name of the variable.</param>
    /// <param name="value">The value of the variable.</param>
    /// <returns>A newline-terminated string to append to the environment file.</returns>
    /// <remarks>
    /// A value that spans multiple lines cannot use the <c>name=value</c> syntax, so a heredoc is used for those.
    /// </remarks>
    internal static string FormatVariable(string name, string value)
    {
        if (value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0)
        {
            return $"{name}={value}\n";
        }

        // A delimiter that appears within the value would let the value terminate its own heredoc,
        // so keep generating candidates until one is unique to this assignment.
        string delimiter;
        do
        {
            delimiter = $"NBGV_EOF_{Guid.NewGuid():N}";
        }
        while (value.IndexOf(delimiter, StringComparison.Ordinal) >= 0);

        // Normalize the value's line endings so that the delimiter is guaranteed to start its own line.
        string normalizedValue = value.Replace("\r\n", "\n").Replace('\r', '\n');
        return $"{name}<<{delimiter}\n{normalizedValue}\n{delimiter}\n";
    }

    private static IReadOnlyDictionary<string, string> GetDictionaryFor(string variableName, string value)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { GetEnvironmentVariableNameForVariable(variableName), value },
        };
    }

    private static string GetEnvironmentVariableNameForVariable(string name) => name.ToUpperInvariant().Replace('.', '_');
}
