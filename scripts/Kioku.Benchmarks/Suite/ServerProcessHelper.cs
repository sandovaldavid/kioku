using System.Diagnostics;
using ModelContextProtocol.Client;

namespace Kioku.Benchmarks.Suite;

/// <summary>
/// Shared helper for spawning the real Kioku MCP server over stdio and connecting to it with
/// the ModelContextProtocol.Client SDK, mirroring the pattern already used by
/// scripts/Kioku.Ci and scripts/Kioku.HandoffDemo. Launches the built server DLL directly
/// (not `dotnet run`) so measured latency reflects the server's own startup cost, not MSBuild
/// project evaluation.
/// </summary>
public static class ServerProcessHelper
{
    public static async Task EnsureServerBuiltAsync(string serverProjectPath, CancellationToken cancellationToken)
    {
        Console.WriteLine("[info] Building Kioku.Mcp.Server (Release, dotnet build)...");
        using var build = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "build", serverProjectPath, "--configuration", "Release" },
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Unable to start 'dotnet build'.");
        await build.WaitForExitAsync(cancellationToken);
        if (build.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'dotnet build {serverProjectPath}' failed with exit code {build.ExitCode}.");
        }

        Console.WriteLine("[ok] Server build succeeded.");
    }

    public static string ResolveServerDll(string repoRoot, string configuration = "Release")
    {
        var dll = Path.Combine(repoRoot, "src", "Kioku.Mcp.Server", "bin", configuration, "net10.0", "Kioku.Mcp.Server.dll");
        if (!File.Exists(dll))
        {
            throw new InvalidOperationException($"Server DLL not found at '{dll}'. Build it first.");
        }

        return dll;
    }

    public static StdioClientTransport CreateTransport(
        string serverDllPath,
        string vaultPath,
        string clientName,
        string? ollamaUrl = null)
    {
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["KIOKU_VAULT_PATH"] = vaultPath;
        environment["KIOKU_TRANSPORT"] = "stdio";
        if (ollamaUrl is not null)
        {
            environment["KIOKU_OLLAMA_URL"] = ollamaUrl;
        }

        CopyCurrentEnvironment(environment, "DOTNET_ROOT");
        CopyCurrentEnvironment(environment, "DOTNET_ROOT_X64");
        CopyCurrentEnvironment(environment, "NUGET_PACKAGES");
        CopyCurrentEnvironment(environment, "HOME");
        CopyCurrentEnvironment(environment, "USERPROFILE");
        CopyCurrentEnvironment(environment, "TMP");
        CopyCurrentEnvironment(environment, "TEMP");
        CopyCurrentEnvironment(environment, "PATH");

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = clientName,
            Command = "dotnet",
            Arguments = [serverDllPath],
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
            ShutdownTimeout = TimeSpan.FromSeconds(2),
        });
    }

    private static void CopyCurrentEnvironment(Dictionary<string, string?> target, string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }
}
