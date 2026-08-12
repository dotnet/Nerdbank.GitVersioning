// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET10_0
#nullable enable

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Nerdbank.GitVersioning;
using Newtonsoft.Json.Linq;
using Xunit;

public class NbgvInstallTests : RepoTestBase
{
    public NbgvInstallTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    [Fact]
    public async Task UnauthorizedNuGetSourceDoesNotModifyRepository()
    {
        this.InitializeSourceControl(withInitialCommit: false);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancellationSource = new CancellationTokenSource();
        Task serverTask = ServeUnauthorizedResponsesAsync(listener, cancellationSource.Token);

        string source = $"http://127.0.0.1:{port}/v3/index.json";
        var nugetConfig = new XDocument(
            new XElement(
                "configuration",
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    new XElement("add", new XAttribute("key", "unauthorized"), new XAttribute("value", source), new XAttribute("allowInsecureConnections", "true")))));
        nugetConfig.Save(Path.Combine(this.RepoPath, "NuGet.Config"));

        string nbgvToolPath = typeof(NbgvInstallTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "NbgvToolPath")
            .Value!;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(nbgvToolPath);
        startInfo.ArgumentList.Add("install");
        startInfo.ArgumentList.Add("--path");
        startInfo.ArgumentList.Add(this.RepoPath);

        using Process process = Process.Start(startInfo)!;
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await Task.WhenAll(standardOutputTask, standardErrorTask, process.WaitForExitAsync(TestContext.Current.CancellationToken));
        string standardError = await standardErrorTask;

        await cancellationSource.CancelAsync();
        listener.Stop();
        await serverTask;

        this.Logger.WriteLine("nbgv standard error:{0}{1}", Environment.NewLine, standardError);
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains($"Failed to query NuGet package sources: Unable to load the service index for source {source}.", standardError);
        Assert.Contains("Use the '--source' option", standardError);
        Assert.DoesNotContain("Unhandled exception", standardError, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(this.RepoPath, VersionFile.JsonFileName)));
        Assert.False(File.Exists(Path.Combine(this.RepoPath, "Directory.Build.props")));
    }

    [Fact]
    public async Task UsesDefaultBranchInVersionJson()
    {
        this.InitializeSourceControl();
        this.AddCommits();
        this.LibGit2Repository!.Refs.Rename("refs/heads/master", "refs/heads/release/v1.0");
        this.LibGit2Repository.Refs.UpdateTarget("HEAD", "refs/heads/release/v1.0");

        string packageSource = this.CreateDirectoryForNewRepo();
        CreatePackage(packageSource, "Nerdbank.GitVersioning", "1.2.3");
        var nugetConfig = new XDocument(
            new XElement(
                "configuration",
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    new XElement("add", new XAttribute("key", "local"), new XAttribute("value", packageSource)))));
        nugetConfig.Save(Path.Combine(this.RepoPath, "NuGet.Config"));

        string nbgvToolPath = typeof(NbgvInstallTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "NbgvToolPath")
            .Value!;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(nbgvToolPath);
        startInfo.ArgumentList.Add("install");
        startInfo.ArgumentList.Add("--path");
        startInfo.ArgumentList.Add(this.RepoPath);

        using Process process = Process.Start(startInfo)!;
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await Task.WhenAll(standardOutputTask, standardErrorTask, process.WaitForExitAsync(TestContext.Current.CancellationToken));
        this.Logger.WriteLine("nbgv standard output:{0}{1}", Environment.NewLine, await standardOutputTask);
        this.Logger.WriteLine("nbgv standard error:{0}{1}", Environment.NewLine, await standardErrorTask);

        Assert.Equal(0, process.ExitCode);
        JObject versionOptions = JObject.Parse(File.ReadAllText(Path.Combine(this.RepoPath, VersionFile.JsonFileName)));
        Assert.Equal("^refs/heads/release/v1\\.0$", (string?)versionOptions["publicReleaseRefSpec"]?[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetVersionWarnsWhenVersionJsonIsDirty(bool staged)
    {
        this.WriteVersionFile();
        this.InitializeSourceControl();
        string versionJsonPath = Path.Combine(this.RepoPath, VersionFile.JsonFileName);
        File.WriteAllText(versionJsonPath, """{"version":"2.0"}""");
        if (staged)
        {
            LibGit2Sharp.Commands.Stage(this.LibGit2Repository, VersionFile.JsonFileName);
        }

        (int exitCode, string standardError) = await this.RunNbgvGetVersionAsync(this.RepoPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("Dirty version.json files must be committed before their changes will be applied.", standardError);
    }

    [Fact]
    public async Task GetVersionWarnsWhenInheritedVersionJsonIsDirty()
    {
        this.WriteVersionFile();
        this.WriteVersionFile(new VersionOptions { Inherit = true }, "src");
        this.InitializeSourceControl();
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.JsonFileName), """{"version":"2.0"}""");

        (int exitCode, string standardError) = await this.RunNbgvGetVersionAsync(Path.Combine(this.RepoPath, "src"));

        Assert.Equal(0, exitCode);
        Assert.Contains("Dirty version.json files must be committed before their changes will be applied.", standardError);
    }

    [Fact]
    public async Task GetVersionDoesNotWarnForUnreadVersionJson()
    {
        this.WriteVersionFile();
        this.WriteVersionFile("2.0", relativeDirectory: "src");
        this.InitializeSourceControl();
        File.WriteAllText(Path.Combine(this.RepoPath, VersionFile.JsonFileName), """{"version":"3.0"}""");

        (int exitCode, string standardError) = await this.RunNbgvGetVersionAsync(Path.Combine(this.RepoPath, "src"));

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Dirty version.json files", standardError);
    }

    [Fact]
    public async Task GetVersionSupportsHeadAlias()
    {
        this.WriteVersionFile();
        this.InitializeSourceControl();

        (int exitCode, _) = await this.RunNbgvGetVersionAsync(this.RepoPath, "@");

        Assert.Equal(0, exitCode);
    }

    protected override GitContext CreateGitContext(string path, string? committish = null)
        => GitContext.Create(path, committish, engine: GitContext.Engine.ReadWrite);

    private static void CreatePackage(string packageSource, string packageId, string packageVersion)
    {
        string packagePath = Path.Combine(packageSource, $"{packageId}.{packageVersion}.nupkg");
        using ZipArchive package = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        using StreamWriter writer = new(package.CreateEntry($"{packageId}.nuspec").Open());
        writer.Write(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>{packageId}</id>
                <version>{packageVersion}</version>
                <authors>Test</authors>
                <description>Test package</description>
              </metadata>
            </package>
            """);
    }

    private static async Task ServeUnauthorizedResponsesAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                await using NetworkStream stream = client.GetStream();
                byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 401 Unauthorized\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<(int ExitCode, string StandardError)> RunNbgvGetVersionAsync(string project, string? commitish = null)
    {
        string nbgvToolPath = typeof(NbgvInstallTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "NbgvToolPath")
            .Value!;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(nbgvToolPath);
        startInfo.ArgumentList.Add("get-version");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(project);
        if (commitish is not null)
        {
            startInfo.ArgumentList.Add(commitish);
        }

        startInfo.Environment["NBGV_GitEngine"] = "Managed";

        using Process process = Process.Start(startInfo)!;
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await Task.WhenAll(standardOutputTask, standardErrorTask, process.WaitForExitAsync(TestContext.Current.CancellationToken));
        string standardError = await standardErrorTask;
        this.Logger.WriteLine("nbgv standard output:{0}{1}", Environment.NewLine, await standardOutputTask);
        this.Logger.WriteLine("nbgv standard error:{0}{1}", Environment.NewLine, standardError);
        return (process.ExitCode, standardError);
    }
}
#endif
