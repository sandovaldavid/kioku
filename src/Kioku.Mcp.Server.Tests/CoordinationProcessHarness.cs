using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

internal sealed record CoordinationFaultPlan(
    string Point,
    string Action,
    string? SignalPath = null,
    string? ReleasePath = null);

internal sealed record ProcessExitObservation(
    int ProcessId,
    bool Exited,
    int? ExitCode,
    string Cause);

/// <summary>
/// Launches the built server as an independent process and connects through the real MCP client
/// transport. The fixture intentionally shares only its vault path with sibling processes.
/// </summary>
internal sealed class CoordinationProcessServer : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(25);

    private readonly Process process;
    private readonly Task<string> standardError;
    private McpClient? client;
    private bool killed;

    private CoordinationProcessServer(
        Process process,
        Task<string> standardError)
    {
        this.process = process;
        this.standardError = standardError;
    }

    internal int ProcessId => process.Id;

    internal ProcessExitObservation Observation
    {
        get
        {
            var exited = process.HasExited;
            return new(
                process.Id,
                exited,
                exited ? process.ExitCode : null,
                killed ? "terminated-by-test" : exited
                    ? process.ExitCode == 0 ? "normal-exit" : "abnormal-exit"
                    : "running");
        }
    }

    internal string? StandardError { get; private set; }

    internal static async Task<CoordinationProcessServer> StartStdioAsync(
        string vaultPath,
        string clientName,
        CoordinationFaultPlan? faultPlan = null,
        CancellationToken cancellationToken = default)
    {
        var process = StartProcess(vaultPath, "stdio", faultPlan);
        var server = new CoordinationProcessServer(
            process,
            process.StandardError.ReadToEndAsync(cancellationToken));
        try
        {
            var transport = new StreamClientTransport(
                process.StandardInput.BaseStream,
                process.StandardOutput.BaseStream,
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
            server.client = await McpClient.CreateAsync(
                    transport,
                    new McpClientOptions
                    {
                        ClientInfo = new Implementation
                        {
                            Name = clientName,
                            Version = "1.0",
                        },
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<CoordinationProcessServer> StartHttpAsync(
        string vaultPath,
        string clientName,
        CoordinationFaultPlan? faultPlan = null,
        CancellationToken cancellationToken = default)
    {
        var port = ReservePort();
        var process = StartProcess(vaultPath, "http", faultPlan, port);
        var server = new CoordinationProcessServer(
            process,
            process.StandardError.ReadToEndAsync(cancellationToken));
        try
        {
            var endpoint = await WaitForReadinessAsync(process, port, cancellationToken).ConfigureAwait(false);
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(endpoint, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            });
            server.client = await McpClient.CreateAsync(
                    transport,
                    new McpClientOptions
                    {
                        ClientInfo = new Implementation
                        {
                            Name = clientName,
                            Version = "1.0",
                        },
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<string> CallToolAsync(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var current = client ?? throw new InvalidOperationException("The MCP client is not connected.");
        var result = await current.CallToolAsync(name, arguments, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var text = string.Join(
            Environment.NewLine,
            result.Content
                .OfType<TextContentBlock>()
                .Select(block => block.Text)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        return text;
    }

    internal async Task<JsonDocument> CallJsonToolAsync(
        string name,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        var text = await CallToolAsync(name, arguments, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(text);
    }

    internal async Task WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<ProcessExitObservation> ShutdownGracefullyAsync()
    {
        if (!process.HasExited)
        {
            var current = Interlocked.Exchange(ref client, null);
            if (current is not null)
            {
                await current.DisposeAsync().AsTask().WaitAsync(ShutdownTimeout).ConfigureAwait(false);
            }

            if (!process.HasExited && process.StartInfo.RedirectStandardInput)
            {
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync().WaitAsync(ShutdownTimeout).ConfigureAwait(false);
        }

        return Observation;
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            killed = true;
            process.Kill(entireProcessTree: true);
        }

        try
        {
            await process.WaitForExitAsync().WaitAsync(ShutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(false);
        }

        var current = Interlocked.Exchange(ref client, null);
        if (current is not null)
        {
            try
            {
                await current.DisposeAsync().AsTask().WaitAsync(ShutdownTimeout).ConfigureAwait(false);
            }
            catch (Exception) when (process.HasExited)
            {
                // The process may have been intentionally crashed by the test.
            }
        }

        StandardError = await standardError.ConfigureAwait(false);
        process.Dispose();
    }

    private static Process StartProcess(
        string vaultPath,
        string transport,
        CoordinationFaultPlan? faultPlan,
        int? httpPort = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveDotnetHost(),
            UseShellExecute = false,
            RedirectStandardInput = transport.Equals("stdio", StringComparison.Ordinal),
            RedirectStandardOutput = transport.Equals("stdio", StringComparison.Ordinal),
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = FindRepositoryRoot(),
        };
        startInfo.ArgumentList.Add(ResolveServerAssembly());
        startInfo.Environment.Clear();
        foreach (var (key, value) in StdioClientTransportOptions.GetDefaultEnvironmentVariables())
        {
            if (value is not null)
            {
                startInfo.Environment[key] = value;
            }
        }

        startInfo.Environment["KIOKU_VAULT_PATH"] = vaultPath;
        startInfo.Environment["KIOKU_TRANSPORT"] = transport;
        startInfo.Environment["KIOKU_OLLAMA_URL"] = "http://127.0.0.1:9";
        startInfo.Environment["KIOKU_INDEX_CONCURRENCY"] = "1";
        startInfo.Environment["KIOKU_EMBEDDING_CONCURRENCY"] = "1";
        if (transport.Equals("http", StringComparison.Ordinal))
        {
            startInfo.Environment["KIOKU_HTTP_HOST"] = "127.0.0.1";
            startInfo.Environment["KIOKU_HTTP_PORT"] = httpPort!.Value.ToString(CultureInfo.InvariantCulture);
            startInfo.Environment["KIOKU_HTTP_ALLOWED_ORIGINS"] = "http://localhost";
        }

        if (faultPlan is not null)
        {
            startInfo.Environment["KIOKU_TEST_COORDINATION_FAULT_POINT"] = faultPlan.Point;
            startInfo.Environment["KIOKU_TEST_COORDINATION_FAULT_ACTION"] = faultPlan.Action;
            SetOptionalEnvironment(startInfo, "KIOKU_TEST_COORDINATION_FAULT_SIGNAL_PATH", faultPlan.SignalPath);
            SetOptionalEnvironment(startInfo, "KIOKU_TEST_COORDINATION_FAULT_RELEASE_PATH", faultPlan.ReleasePath);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("The coordination test server could not be started.");
    }

    private static async Task<Uri> WaitForReadinessAsync(
        Process process,
        int port,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri($"http://127.0.0.1:{port}");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTimeOffset.UtcNow.Add(StartupTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException("The HTTP coordination test server exited during startup.");
            }

            try
            {
                using var response = await http.GetAsync(
                        new Uri(endpoint, "/health/ready"),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return endpoint;
                }
            }
            catch (HttpRequestException)
            {
                // Kestrel may still be binding the ephemeral port.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Retry an individual readiness timeout until the overall deadline.
            }

            await Task.Delay(PollDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("The HTTP coordination test server did not become ready.");
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string ResolveDotnetHost() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
        ?? Environment.ProcessPath
        ?? "dotnet";

    private static string ResolveServerAssembly()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Kioku.Mcp.Server",
            "bin",
            ResolveBuildConfiguration(),
            "net10.0",
            "Kioku.Mcp.Server.dll");
        return File.Exists(path)
            ? path
            : throw new InvalidOperationException("The built Kioku server assembly is unavailable.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kioku.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The Kioku repository root could not be located.");
    }

    private static string ResolveBuildConfiguration()
    {
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        return outputDirectory.Parent?.Name ?? "Release";
    }

    private static void SetOptionalEnvironment(
        ProcessStartInfo startInfo,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            startInfo.Environment[name] = value;
        }
    }
}

internal sealed class CoordinationProcessVault : IAsyncLifetime
{
    internal string VaultPath { get; private set; } = string.Empty;

    public Task InitializeAsync()
    {
        VaultPath = Path.Combine(
            Path.GetTempPath(),
            $"kioku-coordination-process-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(VaultPath, ".kioku"));
        File.WriteAllText(
            Path.Combine(VaultPath, ".kioku", "config.yml"),
            "capabilities:\n  require_explicit: true\n  enabled:\n    - coordination\n    - sessions\n");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            Directory.Delete(VaultPath, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }

        return Task.CompletedTask;
    }

    internal string NotePath(string note) => Path.Combine(VaultPath, note.Replace('/', Path.DirectorySeparatorChar));
}

internal static class CoordinationProcessAssertions
{
    internal static void AssertError(string response, string code)
    {
        Assert.StartsWith($"[error:{code}]", response, StringComparison.Ordinal);
    }

    internal static async Task WaitForFileAsync(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("The signal path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        if (File.Exists(path))
        {
            return;
        }

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler onChanged = (_, _) => completed.TrySetResult();
        RenamedEventHandler onRenamed = (_, _) => completed.TrySetResult();
        watcher.Created += onChanged;
        watcher.Changed += onChanged;
        watcher.Renamed += onRenamed;

        if (File.Exists(path))
        {
            return;
        }

        await completed.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }
}
