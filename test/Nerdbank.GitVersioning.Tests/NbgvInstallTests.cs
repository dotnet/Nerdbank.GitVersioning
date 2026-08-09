// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if NET10_0
#nullable enable

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Nerdbank.GitVersioning;
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

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains($"Failed to query NuGet package sources: Unable to load the service index for source {source}.", standardError);
        Assert.Contains("Use the '--source' option", standardError);
        Assert.DoesNotContain("Unhandled exception", standardError, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(this.RepoPath, VersionFile.JsonFileName)));
        Assert.False(File.Exists(Path.Combine(this.RepoPath, "Directory.Build.props")));
    }

    protected override GitContext CreateGitContext(string path, string? committish = null)
        => GitContext.Create(path, committish, engine: GitContext.Engine.ReadWrite);

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
}
#endif
