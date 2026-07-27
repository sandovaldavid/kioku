using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Kioku.HandoffDemo;

// Multi-agent handoff demo: reproduces the scenario from issue #257 end-to-end over the real
// MCP stdio protocol, with no LLM API calls or keys required.
//
// Agent 1 ("claude-code-demo") starts a work session on a fictional project, records an
// implementation plan, an ADR, and a bug report, then ends its session and disconnects. Its
// server subprocess is proven to fully exit (pid + exit code) before Agent 2 ever connects.
//
// Agent 2 ("codex-demo") connects fresh — a new subprocess, a new MCP session — retrieves the
// project context Agent 1 left behind with get_project_context, starts its OWN session
// (recording Agent 1's session_id as parent_session_id for provenance, not resuming it), adds
// a backlog item that references what it just read, and ends its own session.
//
// A third, independent connection ("verifier-demo") then confirms both sessions exist, are
// closed, and carry distinct ids — proving Agent 2 never touched Agent 1's session.
//
// Usage (from the repo root):
//   dotnet run --project scripts/Kioku.HandoffDemo -- [--vault <path>]
//
// Without --vault, the driver copies demo/handoff/fixture-vault into a fresh temporary
// directory so repeated runs stay reproducible and never modify the checked-in fixture.
internal static class Program
{
    private const string ProjectName = "acme-checkout";
    private const string Agent1ClientName = "claude-code-demo";
    private const string Agent2ClientName = "codex-demo";
    private const string VerifierClientName = "verifier-demo";
    private const string BuildConfiguration = "Debug";
    private const string TargetFramework = "net10.0";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = DemoOptions.Parse(args);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await RunAsync(options, timeout.Token);
            Console.WriteLine();
            Console.WriteLine("[ok] Multi-agent handoff demo completed.");
            if (options.IsTemporaryVault)
            {
                Console.WriteLine($"[info] Fixture vault copy left at: {options.VaultPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[error] Handoff demo failed: {ex}");
            return 1;
        }
    }

    private static async Task RunAsync(DemoOptions options, CancellationToken cancellationToken)
    {
        Console.WriteLine("=== Kioku multi-agent handoff demo ===");
        Console.WriteLine($"[info] Vault: {options.VaultPath}");
        Console.WriteLine($"[info] Server project: {options.ServerProjectPath}");

        await EnsureServerBuiltAsync(options, cancellationToken);

        var agent1SessionId = await RunAgent1Async(options, cancellationToken);
        var agent2SessionId = await RunAgent2Async(options, agent1SessionId, cancellationToken);
        await RunVerificationAsync(options, agent1SessionId, agent2SessionId, cancellationToken);
    }

    private static async Task<string> RunAgent1Async(DemoOptions options, CancellationToken cancellationToken)
    {
        const string agentLabel = "Agent 1";
        Console.WriteLine();
        Console.WriteLine($"=== {agentLabel} (\"{Agent1ClientName}\") starts a project session ===");

        var client = await ConnectAsync(options, Agent1ClientName, cancellationToken);
        string sessionId;
        try
        {
            var startText = await CallToolAsync(
                client,
                "start_work_session",
                new Dictionary<string, object?>
                {
                    ["project"] = ProjectName,
                    ["goal"] = "Fix duplicate charges when the payment gateway times out and the client retries checkout.",
                },
                cancellationToken);
            var startFields = ParseJsonPayload("start_work_session", startText);
            sessionId = startFields["session_id"];
            PrintCall(agentLabel, $"start_work_session -> session_id={sessionId}, path={startFields["path"]}");

            var planText = await CallToolAsync(
                client,
                "create_implementation_plan",
                new Dictionary<string, object?>
                {
                    ["project"] = ProjectName,
                    ["title"] = "Add idempotency keys to checkout retries",
                    ["objective"] =
                        "Client retries after a payment-gateway timeout are indistinguishable from new " +
                        "checkout attempts, which has created duplicate charges. Add an idempotency key " +
                        "so a retry of the same attempt is deduplicated server-side instead of charged again.",
                    ["steps"] =
                        "- [ ] Generate an idempotency key client-side per checkout attempt\n" +
                        "- [ ] Store (idempotency_key -> charge result) in Redis with a 24h TTL\n" +
                        "- [ ] Short-circuit the payment gateway call when a key is already recorded\n" +
                        "- [ ] Add an integration test that retries a timed-out checkout and asserts a single charge",
                    ["status"] = "active",
                },
                cancellationToken);
            PrintCall(agentLabel, $"create_implementation_plan -> {FirstLine(planText)}");

            var adrText = await CallToolAsync(
                client,
                "record_adr",
                new Dictionary<string, object?>
                {
                    ["project"] = ProjectName,
                    ["title"] = "Use Redis-backed idempotency keys for checkout retries",
                    ["context"] =
                        "The payment gateway occasionally times out after the charge already succeeded " +
                        "on its side. The client cannot tell that apart from a real failure, so it " +
                        "retries, which previously created a second charge.",
                    ["decision"] =
                        "Require an idempotency_key on every checkout request. Store the first result " +
                        "for that key in Redis with a 24h TTL, and return the cached result instead of " +
                        "calling the gateway again when the same key is replayed.",
                    ["consequences"] =
                        "Adds a Redis dependency to the checkout path and one extra lookup on the fast " +
                        "path; eliminates duplicate charges from client retries within the TTL window.",
                    ["alternatives"] =
                        "Considered relying on the gateway's own idempotency support, but it only " +
                        "covers a 5-minute window, shorter than our client retry backoff.",
                },
                cancellationToken);
            PrintCall(agentLabel, $"record_adr -> {FirstLine(adrText)}");

            var bugText = await CallToolAsync(
                client,
                "record_bug",
                new Dictionary<string, object?>
                {
                    ["project"] = ProjectName,
                    ["title"] = "Duplicate charges on checkout retry after gateway timeout",
                    ["symptom"] =
                        "A small number of customers were charged twice for a single order when their " +
                        "checkout request timed out and the client automatically retried.",
                    ["root_cause"] =
                        "The checkout endpoint had no request-level deduplication. A retry after a " +
                        "client-side timeout was indistinguishable from a new checkout attempt, and the " +
                        "original request had often already completed on the gateway's side by the time " +
                        "the retry arrived.",
                    ["fix"] =
                        "Added the idempotency-key mechanism from this session's ADR: a second request " +
                        "for the same key now returns the cached result from the first instead of " +
                        "calling the gateway again.",
                    ["related_files"] = "services/checkout/handler.py, services/checkout/gateway_client.py",
                },
                cancellationToken);
            PrintCall(agentLabel, $"record_bug -> {FirstLine(bugText)}");

            var endText = await CallToolAsync(
                client,
                "end_work_session",
                new Dictionary<string, object?>
                {
                    ["session_id"] = sessionId,
                    ["summary"] =
                        "Recorded the idempotency-key plan, ADR, and root-cause writeup for the " +
                        "duplicate-charge bug. Implementation itself is out of scope for this session; " +
                        "ready for review.",
                },
                cancellationToken);
            var endFields = ParseJsonPayload("end_work_session", endText);
            PrintCall(
                agentLabel,
                $"end_work_session -> session_id={endFields["session_id"]}, " +
                $"duration_seconds={endFields.GetValueOrDefault("duration_seconds", "0")}, " +
                $"notes_touched={endFields.GetValueOrDefault("notes_touched", "0")}");
        }
        finally
        {
            await DisconnectAndProveExitAsync(client, Agent1ClientName, cancellationToken);
        }

        Console.WriteLine($"[info] {agentLabel}'s process has fully exited; its MCP connection no longer exists.");
        return sessionId;
    }

    private static async Task<string> RunAgent2Async(
        DemoOptions options, string agent1SessionId, CancellationToken cancellationToken)
    {
        const string agentLabel = "Agent 2";
        Console.WriteLine();
        Console.WriteLine($"=== {agentLabel} (\"{Agent2ClientName}\") starts in a fresh session ===");

        var client = await ConnectAsync(options, Agent2ClientName, cancellationToken);
        string sessionId;
        try
        {
            var workContextText = await CallToolAsync(
                client, "get_work_context", new Dictionary<string, object?>(), cancellationToken);
            PrintExcerpt(
                agentLabel,
                "get_work_context (vault-wide; shows Agent 1's session is no longer active)",
                workContextText);

            var contextText = await CallToolAsync(
                client,
                "get_project_context",
                new Dictionary<string, object?> { ["project"] = ProjectName },
                cancellationToken);
            PrintExcerpt(agentLabel, "get_project_context (retrieves Agent 1's plan/ADR/bug)", contextText);

            var startText = await CallToolAsync(
                client,
                "start_work_session",
                new Dictionary<string, object?>
                {
                    ["project"] = ProjectName,
                    ["goal"] =
                        "Review Agent 1's idempotency-key fix for checkout retries and close remaining " +
                        "gaps before it ships.",
                    ["parent_session_id"] = agent1SessionId,
                },
                cancellationToken);
            var startFields = ParseJsonPayload("start_work_session", startText);
            sessionId = startFields["session_id"];
            PrintCall(
                agentLabel,
                $"start_work_session -> session_id={sessionId} (its OWN session; " +
                $"parent_session_id={agent1SessionId} records provenance, it does not resume Agent 1)");

            var backlogText = await CallToolAsync(
                client,
                "add_backlog_item",
                new Dictionary<string, object?>
                {
                    ["project"] = ProjectName,
                    ["title"] = "Add chaos test for concurrent retry storms",
                    ["description"] =
                        "Agent 1's idempotency-key fix (see this project's ADR and bug report) was " +
                        "verified against a single sequential retry. Add a chaos/load test that fires a " +
                        "burst of concurrent retries sharing one idempotency key to confirm exactly one " +
                        "charge is created under contention, not just when retries arrive one at a time.",
                },
                cancellationToken);
            PrintCall(agentLabel, $"add_backlog_item -> {FirstLine(backlogText)}");

            var endText = await CallToolAsync(
                client,
                "end_work_session",
                new Dictionary<string, object?>
                {
                    ["session_id"] = sessionId,
                    ["summary"] =
                        "Reviewed Agent 1's plan, ADR, and bug report via get_project_context; the fix " +
                        "covers the reported case but not concurrent retries. Filed that gap as a " +
                        "backlog item and handed off for prioritization.",
                },
                cancellationToken);
            var endFields = ParseJsonPayload("end_work_session", endText);
            PrintCall(
                agentLabel,
                $"end_work_session -> session_id={endFields["session_id"]}, " +
                $"duration_seconds={endFields.GetValueOrDefault("duration_seconds", "0")}, " +
                $"notes_touched={endFields.GetValueOrDefault("notes_touched", "0")}");
        }
        finally
        {
            await DisconnectAndProveExitAsync(client, Agent2ClientName, cancellationToken);
        }

        return sessionId;
    }

    private static async Task RunVerificationAsync(
        DemoOptions options, string agent1SessionId, string agent2SessionId, CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Verification (\"{VerifierClientName}\", independent third connection) ===");

        var client = await ConnectAsync(options, VerifierClientName, cancellationToken);
        try
        {
            var listText = await CallToolAsync(
                client,
                "list_work_sessions",
                new Dictionary<string, object?> { ["project"] = ProjectName },
                cancellationToken);
            PrintExcerpt("Verifier", "list_work_sessions(project=\"acme-checkout\")", listText);

            var sawAgent1 = listText.Contains(agent1SessionId, StringComparison.Ordinal);
            var sawAgent2 = listText.Contains(agent2SessionId, StringComparison.Ordinal);
            if (!sawAgent1 || !sawAgent2)
            {
                throw new InvalidOperationException(
                    "Expected both session ids in list_work_sessions output. " +
                    $"agent1_seen={sawAgent1}, agent2_seen={sawAgent2}.");
            }

            if (string.Equals(agent1SessionId, agent2SessionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Agent 1 and Agent 2 unexpectedly share the same session_id.");
            }

            var agent1Status = ExtractSessionStatus(listText, agent1SessionId);
            var agent2Status = ExtractSessionStatus(listText, agent2SessionId);
            if (!string.Equals(agent1Status, "done", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(agent2Status, "done", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Expected both sessions closed ('done'). " +
                    $"agent1_status={agent1Status}, agent2_status={agent2Status}.");
            }

            Console.WriteLine(
                "[ok] Verified: two distinct session_id values, both status=done, both listed " +
                "independently under the same project. Agent 2 never reopened or edited Agent 1's " +
                "session note.");
        }
        finally
        {
            await DisconnectAndProveExitAsync(client, VerifierClientName, cancellationToken);
        }
    }

    private static string ExtractSessionStatus(string listText, string sessionId)
    {
        foreach (var line in listText.Split('\n'))
        {
            if (!line.Contains($"`{sessionId}`", StringComparison.Ordinal))
            {
                continue;
            }

            const string marker = "status: ";
            var statusIndex = line.IndexOf(marker, StringComparison.Ordinal);
            if (statusIndex < 0)
            {
                break;
            }

            var rest = line[(statusIndex + marker.Length)..];
            return rest.Split(" — ", 2)[0].Trim();
        }

        throw new InvalidOperationException($"Could not find status for session_id '{sessionId}' in:\n{listText}");
    }

    private static async Task<McpClient> ConnectAsync(
        DemoOptions options, string clientName, CancellationToken cancellationToken)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = $"Kioku handoff demo ({clientName})",
            Command = "dotnet",
            Arguments =
            [
                "run",
                "--project", options.ServerProjectPath,
                "--no-build",
                "--configuration", BuildConfiguration,
            ],
            InheritEnvironmentVariables = false,
            EnvironmentVariables = CreateServerEnvironment(options),
            // ModelContextProtocol.Core v1.4.1's StdioClientTransport disposal path never signals
            // EOF on the child process's stdin before waiting; it only waits up to ShutdownTimeout
            // for the process to exit on its own, then force-kills the process tree. The server's
            // stdio read loop has no way to observe a graceful signal to stop, so it never reaches
            // its own shutdown path. All tool calls have already completed and returned by the time
            // we get here, so a forceful exit loses nothing — it just means the captured exit code
            // below is a kill (SIGKILL/137), not a graceful 0. Shortened from the 5s default purely
            // so the demo does not spend 15s waiting across three connections.
            ShutdownTimeout = TimeSpan.FromSeconds(2),
        });

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = clientName, Version = "1.0.0" },
        };

        var client = await McpClient.CreateAsync(transport, clientOptions, cancellationToken: cancellationToken);
        Console.WriteLine($"[info] Connected as MCP client \"{clientName}\" (its own subprocess, its own stdio pipe).");
        return client;
    }

    private static async Task DisconnectAndProveExitAsync(
        McpClient client, string clientName, CancellationToken cancellationToken)
    {
        await client.DisposeAsync();
        try
        {
            var details = await client.Completion.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            if (details is StdioClientCompletionDetails stdio)
            {
                Console.WriteLine(
                    $"[info] \"{clientName}\" subprocess fully exited: pid={stdio.ProcessId}, " +
                    $"exit_code={stdio.ExitCode}.");
                return;
            }
        }
        catch (TimeoutException)
        {
            // Fall through to the generic message below.
        }

        Console.WriteLine($"[info] \"{clientName}\" disconnected; subprocess teardown confirmed by DisposeAsync.");
    }

    private static async Task EnsureServerBuiltAsync(DemoOptions options, CancellationToken cancellationToken)
    {
        var serverDll = Path.Combine(
            options.ServerProjectPath, "bin", BuildConfiguration, TargetFramework, "Kioku.Mcp.Server.dll");
        if (File.Exists(serverDll))
        {
            Console.WriteLine($"[info] Server already built: {serverDll}");
            return;
        }

        // dotnet run without --no-build writes MSBuild's restore/build progress to stdout, which
        // would corrupt the JSON-RPC framing on the stdio pipe. Building explicitly first lets
        // every subsequent "dotnet run --no-build" spawn a clean stdio channel.
        Console.WriteLine("[info] Building Kioku.Mcp.Server once before spawning any agent (dotnet build)...");
        using var build = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "build", options.ServerProjectPath, "--configuration", BuildConfiguration },
            UseShellExecute = false,
        }) ?? throw new InvalidOperationException("Unable to start 'dotnet build'.");
        await build.WaitForExitAsync(cancellationToken);
        if (build.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'dotnet build {options.ServerProjectPath}' failed with exit code {build.ExitCode}.");
        }

        Console.WriteLine("[ok] Server build succeeded.");
    }

    private static Dictionary<string, string?> CreateServerEnvironment(DemoOptions options)
    {
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["KIOKU_VAULT_PATH"] = options.VaultPath;
        // Point Ollama at an unreachable loopback port: this demo's tool sequence never calls
        // semantic search, and EmbeddingService degrades gracefully when Ollama is unreachable,
        // so this keeps the demo fast, deterministic, and offline (mirrors scripts/Kioku.Ci).
        environment["KIOKU_OLLAMA_URL"] = "http://127.0.0.1:9";
        CopyCurrentEnvironment(environment, "DOTNET_ROOT");
        CopyCurrentEnvironment(environment, "DOTNET_ROOT_X64");
        CopyCurrentEnvironment(environment, "NUGET_PACKAGES");
        CopyCurrentEnvironment(environment, "HOME");
        CopyCurrentEnvironment(environment, "USERPROFILE");
        CopyCurrentEnvironment(environment, "TMP");
        CopyCurrentEnvironment(environment, "TEMP");
        return environment;
    }

    private static void CopyCurrentEnvironment(IDictionary<string, string?> target, string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            target[key] = value;
        }
    }

    private static async Task<string> CallToolAsync(
        McpClient client,
        string toolName,
        Dictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
        var text = ExtractPrimaryText(result);
        if (result.IsError is true)
        {
            throw new InvalidOperationException($"{toolName} returned an MCP tool error: {text}");
        }

        return text;
    }

    private static string ExtractPrimaryText(CallToolResult result) =>
        string.Join(
            Environment.NewLine,
            result.Content.OfType<TextContentBlock>()
                .Select(block => block.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));

    private static Dictionary<string, string> ParseJsonPayload(string toolName, string text)
    {
        var braceIndex = text.IndexOf('{', StringComparison.Ordinal);
        if (braceIndex < 0)
        {
            throw new InvalidOperationException($"{toolName} did not return a JSON payload. Response:\n{text}");
        }

        using var document = JsonDocument.Parse(text[braceIndex..]);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            fields[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Null => string.Empty,
                _ => property.Value.GetRawText(),
            };
        }

        return fields;
    }

    private static string FirstLine(string text) => text.Split('\n', 2)[0].TrimEnd();

    private static void PrintCall(string agentLabel, string line) => Console.WriteLine($"[{agentLabel}] {line}");

    private static void PrintExcerpt(string agentLabel, string label, string text, int maxLines = 14)
    {
        Console.WriteLine($"[{agentLabel}] {label}:");
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var line in lines.Take(maxLines))
        {
            Console.WriteLine($"    | {line.TrimEnd()}");
        }

        if (lines.Length > maxLines)
        {
            Console.WriteLine($"    | ... ({lines.Length - maxLines} more line(s) omitted; see the full run log)");
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        foreach (var filePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, filePath);
            var targetPath = Path.Combine(destinationDir, relative);
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            File.Copy(filePath, targetPath, overwrite: true);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        DirectoryInfo? cursor = directory;
        while (cursor is not null && !File.Exists(Path.Combine(cursor.FullName, "Kioku.slnx")))
        {
            cursor = cursor.Parent;
        }

        return cursor?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the repo root (no 'Kioku.slnx' found above the executable directory).");
    }

    private sealed record DemoOptions(
        string RepoRoot,
        string VaultPath,
        string ServerProjectPath,
        bool IsTemporaryVault)
    {
        internal static DemoOptions Parse(string[] args)
        {
            string? vault = null;
            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                switch (argument)
                {
                    case "--vault":
                        vault = RequireValue(args, ref index, argument);
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{argument}'.");
                }
            }

            var repoRoot = FindRepoRoot();
            var serverProjectPath = Path.Combine(repoRoot, "src", "Kioku.Mcp.Server");
            if (!Directory.Exists(serverProjectPath))
            {
                throw new InvalidOperationException($"Server project not found at '{serverProjectPath}'.");
            }

            if (vault is not null)
            {
                var vaultPath = Path.GetFullPath(vault);
                Directory.CreateDirectory(vaultPath);
                return new DemoOptions(repoRoot, vaultPath, serverProjectPath, IsTemporaryVault: false);
            }

            var fixtureVaultPath = Path.Combine(repoRoot, "demo", "handoff", "fixture-vault");
            if (!Directory.Exists(fixtureVaultPath))
            {
                throw new InvalidOperationException($"Fixture vault not found at '{fixtureVaultPath}'.");
            }

            var tempVaultPath = Path.Combine(Path.GetTempPath(), $"kioku-handoff-demo-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempVaultPath);
            CopyDirectory(fixtureVaultPath, tempVaultPath);
            return new DemoOptions(repoRoot, tempVaultPath, serverProjectPath, IsTemporaryVault: true);
        }

        private static string RequireValue(string[] args, ref int index, string argument)
        {
            index++;
            if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            {
                throw new ArgumentException($"{argument} requires a value.");
            }

            return args[index];
        }
    }
}
