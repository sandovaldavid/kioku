using System.Diagnostics;
using System.Reflection;
using Kioku.Mcp.Server.Tools;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public async Task Version_switch_exits_without_starting_server(string versionArgument)
    {
        var serverAssembly = typeof(UtilityTools).Assembly;
        var expectedVersion = serverAssembly
                                  .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                                  .InformationalVersion
                              ?? serverAssembly.GetName().Version?.ToString()
                              ?? "unknown";
        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"kioku-version-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(serverAssembly.Location);
            startInfo.ArgumentList.Add(versionArgument);
            startInfo.Environment.Remove("KIOKU_VAULT_PATH");
            startInfo.Environment.Remove("Kioku__VaultPath");

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start the Kioku version process.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.WaitForExitAsync(timeout.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            Assert.Equal(0, process.ExitCode);
            Assert.Equal(expectedVersion, stdout.Trim());
            Assert.False(stderr.Contains("Kioku MCP Server starting", StringComparison.Ordinal));
            Assert.False(stderr.Contains("Vault:", StringComparison.Ordinal));
            Assert.False(stderr.Contains("Starting vault reconciliation", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }
}
