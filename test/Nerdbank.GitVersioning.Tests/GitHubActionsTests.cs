// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nerdbank.GitVersioning.CloudBuildServices;
using Xunit;

public class GitHubActionsTests : IDisposable
{
    private readonly string environmentFile;

    public GitHubActionsTests()
    {
        this.environmentFile = Path.Combine(Path.GetTempPath(), $"nbgv-github-env-{Guid.NewGuid():N}.txt");
    }

    public void Dispose()
    {
        if (File.Exists(this.environmentFile))
        {
            File.Delete(this.environmentFile);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void FormatVariable_SingleLineValue()
    {
        Assert.Equal("Name=Value\n", GitHubActions.FormatVariable("Name", "Value"));
    }

    [Fact]
    public void FormatVariable_EmptyValue()
    {
        Assert.Equal("Name=\n", GitHubActions.FormatVariable("Name", string.Empty));
    }

    [Fact]
    public void FormatVariable_ValueContainingEqualsSign()
    {
        // Only the first '=' is a separator, so this needs no special treatment.
        Assert.Equal("Name=a=b\n", GitHubActions.FormatVariable("Name", "a=b"));
    }

    [Theory]
    [InlineData("first\nsecond")]
    [InlineData("first\r\nsecond")]
    [InlineData("first\rsecond")]
    public void FormatVariable_MultiLineValueUsesHeredoc(string value)
    {
        string actual = GitHubActions.FormatVariable("Name", value);
        string[] lines = actual.Split('\n');

        // Trailing newline produces a final empty element.
        Assert.Equal(string.Empty, lines[lines.Length - 1]);
        Assert.StartsWith("Name<<", lines[0], StringComparison.Ordinal);

        string delimiter = lines[0].Substring("Name<<".Length);
        Assert.NotEmpty(delimiter);
        Assert.Equal(delimiter, lines[lines.Length - 2]);
        Assert.Equal(new[] { "first", "second" }, lines.Skip(1).Take(lines.Length - 3));

        // Line endings within the value must be normalized so the delimiter starts its own line.
        Assert.DoesNotContain('\r', actual);
    }

    [Fact]
    public void FormatVariable_HeredocDelimiterDoesNotAppearInValue()
    {
        string actual = GitHubActions.FormatVariable("Name", "a\nb");
        string delimiter = actual.Split('\n')[0].Substring("Name<<".Length);
        Assert.DoesNotContain(delimiter, "a\nb");
    }

    [Fact]
    public void FormatVariable_HeredocDelimiterIsUniquePerCall()
    {
        // A predictable delimiter could be smuggled in by a value, so it must vary.
        Assert.NotEqual(
            GitHubActions.FormatVariable("Name", "a\nb"),
            GitHubActions.FormatVariable("Name", "a\nb"));
    }

    [Fact]
    public void AppendVariable_CreatesFile()
    {
        Assert.False(File.Exists(this.environmentFile));
        GitHubActions.AppendVariable(this.environmentFile, "Name", "Value");
        Assert.Equal("Name=Value\n", this.ReadEnvironmentFile());
    }

    [Fact]
    public void AppendVariable_AppendsToExistingContent()
    {
        File.WriteAllText(this.environmentFile, "Existing=1\n");
        GitHubActions.AppendVariable(this.environmentFile, "Name", "Value");
        Assert.Equal("Existing=1\nName=Value\n", this.ReadEnvironmentFile());
    }

    [Fact]
    public void AppendVariable_TerminatesAnUnterminatedLastLine()
    {
        File.WriteAllText(this.environmentFile, "Existing=1");
        GitHubActions.AppendVariable(this.environmentFile, "Name", "Value");
        Assert.Equal("Existing=1\nName=Value\n", this.ReadEnvironmentFile());
    }

    [Fact]
    public void AppendVariable_NullValueIsTreatedAsEmpty()
    {
        GitHubActions.AppendVariable(this.environmentFile, "Name", null);
        Assert.Equal("Name=\n", this.ReadEnvironmentFile());
    }

    [Fact]
    public void AppendVariable_WritesUtf8WithoutPreamble()
    {
        GitHubActions.AppendVariable(this.environmentFile, "Name", "\u00e9");
        Assert.Equal(new byte[] { (byte)'N', (byte)'a', (byte)'m', (byte)'e', (byte)'=', 0xC3, 0xA9, (byte)'\n' }, File.ReadAllBytes(this.environmentFile));
    }

    [Fact]
    public void AppendVariable_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentNullException>(() => GitHubActions.AppendVariable(null, "Name", "Value"));
        Assert.Throws<ArgumentException>(() => GitHubActions.AppendVariable(string.Empty, "Name", "Value"));
        Assert.Throws<ArgumentNullException>(() => GitHubActions.AppendVariable(this.environmentFile, null, "Value"));
        Assert.Throws<ArgumentException>(() => GitHubActions.AppendVariable(this.environmentFile, string.Empty, "Value"));
        Assert.Throws<ArgumentException>(() => GitHubActions.AppendVariable(this.environmentFile, "Na\nme", "Value"));
    }

    /// <summary>
    /// Verifies that concurrent writers neither lose nor interleave their assignments.
    /// </summary>
    /// <remarks>
    /// This is the regression test for the corruption that GitHub Actions reports as the error
    /// <c>Unable to process file command 'env' successfully</c>. Many MSBuild processes (and threads within
    /// them) can build projects in parallel within a single step, and every one of them appends the
    /// version variables to the one file named by <c>GITHUB_ENV</c>.
    /// </remarks>
    [Fact]
    public async Task AppendVariable_IsSafeForConcurrentWriters()
    {
        const int writers = 16;
        const int writesPerWriter = 100;

        var tasks = new Task[writers];
        for (int w = 0; w < writers; w++)
        {
            int writer = w;
            tasks[w] = Task.Run(
                () =>
                {
                    for (int i = 0; i < writesPerWriter; i++)
                    {
                        // A value long enough that a torn write is very likely to be detected.
                        GitHubActions.AppendVariable(this.environmentFile, $"NBGV_{writer}_{i}", new string((char)('a' + writer), 100) + i);
                    }
                },
                TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks);

        var expected = new HashSet<string>(StringComparer.Ordinal);
        for (int w = 0; w < writers; w++)
        {
            for (int i = 0; i < writesPerWriter; i++)
            {
                expected.Add($"NBGV_{w}_{i}={new string((char)('a' + w), 100)}{i}");
            }
        }

        string[] actual = this.ReadEnvironmentFile().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected.Count, actual.Length);
        Assert.Equal(expected, new HashSet<string>(actual, StringComparer.Ordinal));
    }

    private string ReadEnvironmentFile() => Encoding.UTF8.GetString(File.ReadAllBytes(this.environmentFile));
}
