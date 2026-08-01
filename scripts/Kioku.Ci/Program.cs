using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Kioku.Ci;

internal static class Program
{
    private const string SmokeMarker = "Kioku MCP end-to-end smoke marker";
    private static readonly TimeSpan IndexPropagationTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    public static async Task<int> Main(string[] args)
    {
        SmokeOptions? options = null;
        try
        {
            options = SmokeOptions.Parse(args);
            Directory.CreateDirectory(options.VaultPath);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
            if (options.Transport.Equals("stdio", StringComparison.OrdinalIgnoreCase))
            {
                await RunStdioAsync(options, timeout.Token);
            }
            else
            {
                await RunHttpAsync(options, timeout.Token);
            }

            Console.WriteLine($"[ok] {options.Transport} MCP smoke test completed.");
            return 0;
        }
        catch (Exception ex)
        {
            var details = options is null ? ex.ToString() : Redact(ex.ToString(), options);
            Console.Error.WriteLine($"[error] MCP smoke test failed: {details}");
            return 1;
        }
    }

    private static async Task RunStdioAsync(SmokeOptions options, CancellationToken cancellationToken)
    {
        var command = options.Command;
        IList<string> arguments = options.CommandArguments;
        var environment = CreateServerEnvironment(options, "stdio");
        if (OperatingSystem.IsWindows() && Path.IsPathFullyQualified(command))
        {
            var normalizedCommand = command.Replace('/', '\\');
            var toolDirectory = Path.GetDirectoryName(normalizedCommand)
                ?? throw new InvalidOperationException($"Unable to resolve the tool directory for '{command}'.");
            var currentPath = Environment.GetEnvironmentVariable("PATH");
            environment["PATH"] = string.IsNullOrWhiteSpace(currentPath)
                ? toolDirectory
                : $"{toolDirectory}{Path.PathSeparator}{currentPath}";
            command = Path.GetFileNameWithoutExtension(normalizedCommand);
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Kioku CI stdio",
            Command = command,
            Arguments = arguments,
            InheritEnvironmentVariables = false,
            EnvironmentVariables = environment,
        });
        await VerifyProtocolAsync(transport, options, cancellationToken);
    }

    private static async Task RunHttpAsync(SmokeOptions options, CancellationToken cancellationToken)
    {
        if (options.Endpoint is null)
        {
            throw new InvalidOperationException("--endpoint is required for the HTTP smoke test.");
        }

        using var process = StartHttpServer(options);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        Exception? failure = null;
        try
        {
            await WaitForReadinessAsync(process, options, cancellationToken);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                headers["Authorization"] = $"Bearer {options.ApiKey}";
            }

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = "Kioku CI Streamable HTTP",
                Endpoint = options.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = headers,
            });
            await VerifyProtocolAsync(transport, options, cancellationToken);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            await StopProcessAsync(process);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (failure is not null)
        {
            throw new InvalidOperationException(
                $"{Redact(failure.Message, options)}{Environment.NewLine}--- server stdout ---{Environment.NewLine}" +
                $"{Redact(stdout, options)}{Environment.NewLine}--- server stderr ---{Environment.NewLine}" +
                Redact(stderr, options),
                failure);
        }
    }

    private static async Task VerifyProtocolAsync(
        IClientTransport transport,
        SmokeOptions options,
        CancellationToken cancellationToken)
    {
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        await client.PingAsync(cancellationToken: cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        Console.Error.WriteLine($"[DIAG] {toolNames.Count} tools: {string.Join(", ", toolNames.OrderBy(n => n, StringComparer.Ordinal))}");
        try
        {
            var capsResult = await client.CallToolAsync(
                "get_server_capabilities",
                new Dictionary<string, object?>(),
                cancellationToken: cancellationToken);
            Console.Error.WriteLine($"[DIAG] get_server_capabilities: {ExtractResultText(capsResult)}");
        }
        catch (Exception diagEx)
        {
            Console.Error.WriteLine($"[DIAG] get_server_capabilities call failed: {diagEx}");
        }

        RequireTool(toolNames, "list_work_sessions");
        if (options.Coordination)
        {
            RequireTool(toolNames, "create_coordination_work_item");
        }
        else
        {
            EnsureToolAbsent(toolNames, "create_coordination_work_item");
        }
        RequireTool(toolNames, "create_note");
        RequireTool(toolNames, "read_note");
        RequireTool(toolNames, "delete_note");
        RequireTool(toolNames, "get_server_capabilities");

        await VerifyCapabilitiesAsync(client, options, cancellationToken);

        var sessionsResult = await client.CallToolAsync(
            "list_work_sessions",
            new Dictionary<string, object?>
            {
                ["sessions_folder"] = "Sessions",
            },
            cancellationToken: cancellationToken);
        EnsureSuccess("list_work_sessions", sessionsResult);
        var sessionsText = ExtractResultText(sessionsResult);
        if (!sessionsText.Contains("Legacy Session", StringComparison.Ordinal) ||
            !sessionsText.Contains("019c0000-0000-7000-8000-000000000001", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "list_work_sessions did not return the checked-in pre-profile session fixture.");
        }

        var legacyReadResult = await client.CallToolAsync(
            "read_note",
            new Dictionary<string, object?>
            {
                ["note"] = "Sessions/Legacy Session",
                ["format"] = "text",
            },
            cancellationToken: cancellationToken);
        EnsureSuccess("read_note legacy fixture", legacyReadResult);
        EnsureStructuredEnvelope("read_note legacy fixture", legacyReadResult);
        if (!ExtractResultText(legacyReadResult).Contains(
                "The legacy session must remain readable after the server starts.",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("read_note did not return the pre-profile session content.");
        }

        if (options.Coordination)
        {
            await VerifyCoordinationAsync(client, cancellationToken);
        }

        var noteName = $"ci-smoke-{Guid.NewGuid():N}";
        var expectedPath = Path.Combine(options.VaultPath, $"{noteName}.md");
        var createResult = await client.CallToolAsync(
            "create_note",
            new Dictionary<string, object?>
            {
                ["name"] = noteName,
                ["content"] = $"# CI Smoke{Environment.NewLine}{Environment.NewLine}{SmokeMarker}",
                ["kind"] = "note",
            },
            cancellationToken: cancellationToken);
        EnsureSuccess("create_note", createResult);
        EnsureStructuredEnvelope("create_note", createResult);
        if (!File.Exists(expectedPath))
        {
            throw new InvalidOperationException($"create_note did not persist '{expectedPath}'.");
        }

        await WaitForReadContentAsync(client, noteName, cancellationToken);
        var deleteResult = await client.CallToolAsync(
            "delete_note",
            new Dictionary<string, object?>
            {
                ["note"] = noteName,
                ["permanent"] = false,
                ["dry_run"] = false,
            },
            cancellationToken: cancellationToken);
        EnsureSuccess("delete_note", deleteResult);
    }

    private static async Task VerifyCapabilitiesAsync(
        McpClient client,
        SmokeOptions options,
        CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(
            "get_server_capabilities",
            new Dictionary<string, object?>(),
            cancellationToken: cancellationToken);
        EnsureSuccess("get_server_capabilities", result);
        using var document = ParseJsonResult(result);
        var root = document.RootElement;
        if (!string.Equals(
                root.GetProperty("profile_id").GetString(),
                "kioku.durable-coordination",
                StringComparison.Ordinal) ||
            root.GetProperty("profile_version").GetInt32() != 1 ||
            root.GetProperty("schema_version").GetInt32() != 1)
        {
            throw new InvalidOperationException("The capability profile version contract is invalid.");
        }

        var enabled = root.GetProperty("capability_group").GetProperty("enabled").GetBoolean();
        if (enabled != options.Coordination)
        {
            throw new InvalidOperationException(
                $"The coordination capability state was {enabled}, expected {options.Coordination}.");
        }
    }

    private static async Task VerifyCoordinationAsync(
        McpClient client,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var runId = $"ci-run-{suffix}";
        var workItemId = $"ci-work-{suffix}";
        var createResult = await client.CallToolAsync(
            "create_coordination_work_item",
            new Dictionary<string, object?>
            {
                ["project"] = "package-smoke",
                ["run_id"] = runId,
                ["work_item_id"] = workItemId,
                ["attempt_id"] = $"ci-attempt-{suffix}",
                ["session_id"] = $"ci-session-{suffix}",
                ["agent"] = "package-smoke",
                ["resource_scope"] = "logical:package-smoke",
                ["summary"] = "Synthetic package coordination smoke fixture.",
                ["transition_id"] = $"ci-create-{suffix}",
            },
            cancellationToken: cancellationToken);
        EnsureSuccess("create_coordination_work_item", createResult);
        using var createJson = ParseJsonResult(createResult);
        if (!string.Equals(
                createJson.RootElement.GetProperty("projection").GetProperty("state").GetString(),
                "pending",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The coordination package smoke item was not pending.");
        }

        var historyResult = await client.CallToolAsync(
            "list_coordination_history",
            new Dictionary<string, object?>
            {
                ["run_id"] = runId,
                ["work_item_id"] = workItemId,
            },
            cancellationToken: cancellationToken);
        EnsureSuccess("list_coordination_history", historyResult);
        using var historyJson = ParseJsonResult(historyResult);
        if (historyJson.RootElement.GetProperty("items").GetArrayLength() != 1)
        {
            throw new InvalidOperationException("The coordination package smoke history was not replayable.");
        }
    }

    private static async Task WaitForReadContentAsync(McpClient client, string noteName, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(IndexPropagationTimeout);
        string lastResponse = "<empty>";
        while (DateTimeOffset.UtcNow < deadline)
        {
            var readResult = await client.CallToolAsync(
                "read_note",
                new Dictionary<string, object?>
                {
                    ["note"] = noteName,
                    ["format"] = "text",
                },
                cancellationToken: cancellationToken);
            EnsureSuccess("read_note", readResult);
            EnsureStructuredEnvelope("read_note", readResult);
            lastResponse = ExtractResultText(readResult);
            if (lastResponse.Contains(SmokeMarker, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(RetryDelay, cancellationToken);
        }

        throw new InvalidOperationException(
            $"read_note did not return the created content after index reconciliation. Last response: {lastResponse}");
    }

    private static string ExtractResultText(CallToolResult result)
    {
        var parts = result.Content
            .OfType<TextContentBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        if (result.StructuredContent is { } structuredContent)
        {
            parts.Add(structuredContent.GetRawText());
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static JsonDocument ParseJsonResult(CallToolResult result)
    {
        var text = result.Content
            .OfType<TextContentBlock>()
            .Select(block => block.Text)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (text is not null)
        {
            return JsonDocument.Parse(text);
        }

        if (result.StructuredContent is { ValueKind: JsonValueKind.Object } structured)
        {
            return JsonDocument.Parse(structured.GetRawText());
        }

        return JsonDocument.Parse(ExtractResultText(result));
    }

    private static string Redact(string value, SmokeOptions options)
    {
        var redacted = value.Replace(options.VaultPath, "<vault>", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            redacted = redacted.Replace(options.ApiKey, "<api-key>", StringComparison.Ordinal);
        }

        return redacted;
    }

    private static Process StartHttpServer(SmokeOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.Command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in options.CommandArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var (key, value) in CreateServerEnvironment(options, "http"))
        {
            if (value is not null)
            {
                startInfo.Environment[key] = value;
            }
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start '{options.Command}'.");
    }

    private static Dictionary<string, string?> CreateServerEnvironment(SmokeOptions options, string transport)
    {
        var environment = StdioClientTransportOptions.GetDefaultEnvironmentVariables();
        environment["KIOKU_VAULT_PATH"] = options.VaultPath;
        environment["KIOKU_TRANSPORT"] = transport;
        environment["KIOKU_OLLAMA_URL"] = "http://127.0.0.1:9";
        environment["KIOKU_INDEX_CONCURRENCY"] = "2";
        environment["KIOKU_EMBEDDING_CONCURRENCY"] = "1";
        CopyCurrentEnvironment(environment, "DOTNET_ROOT");
        CopyCurrentEnvironment(environment, "DOTNET_ROOT_X64");
        CopyCurrentEnvironment(environment, "NUGET_PACKAGES");
        CopyCurrentEnvironment(environment, "HOME");
        CopyCurrentEnvironment(environment, "USERPROFILE");
        CopyCurrentEnvironment(environment, "TMP");
        CopyCurrentEnvironment(environment, "TEMP");

        if (transport.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = options.Endpoint
                ?? throw new InvalidOperationException("HTTP endpoint is required.");
            environment["KIOKU_HTTP_HOST"] = endpoint.Host;
            environment["KIOKU_HTTP_PORT"] = endpoint.Port.ToString(CultureInfo.InvariantCulture);
            environment["KIOKU_HTTP_ALLOWED_ORIGINS"] = "http://localhost";
            environment["KIOKU_API_KEY"] = options.ApiKey;
        }

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

    private static async Task WaitForReadinessAsync(Process process, SmokeOptions options, CancellationToken cancellationToken)
    {
        var endpoint = options.Endpoint
            ?? throw new InvalidOperationException("HTTP endpoint is required.");
        var readinessUri = new Uri(endpoint, "/health/ready");
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        while (!cancellationToken.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"HTTP server exited before readiness with code {process.ExitCode}.");
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, readinessUri);
                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                }

                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The server may still be binding the port.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Retry an individual readiness timeout until the overall smoke timeout expires.
            }

            await Task.Delay(RetryDelay, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process.WaitForExitAsync();
    }

    private static void RequireTool(HashSet<string> tools, string name)
    {
        if (!tools.Contains(name))
        {
            throw new InvalidOperationException($"tools/list did not include '{name}'.");
        }
    }

    private static void EnsureToolAbsent(HashSet<string> tools, string name)
    {
        if (tools.Contains(name))
        {
            throw new InvalidOperationException($"tools/list unexpectedly included gated tool '{name}'.");
        }
    }

    private static void EnsureSuccess(string tool, CallToolResult result)
    {
        if (result.IsError is true)
        {
            throw new InvalidOperationException($"{tool} returned an MCP tool error: {ExtractResultText(result)}");
        }
    }

    private static void EnsureStructuredEnvelope(string tool, CallToolResult result)
    {
        if (result.StructuredContent is not { ValueKind: JsonValueKind.Object } structured ||
            !structured.TryGetProperty("success", out var success) ||
            success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException(
                $"{tool} did not return the expected structured MCP envelope.");
        }
    }

    private sealed record SmokeOptions(
        string Transport,
        string Command,
        string[] CommandArguments,
        string VaultPath,
        Uri? Endpoint,
        string? ApiKey,
        int TimeoutSeconds,
        bool Coordination)
    {
        internal static SmokeOptions Parse(string[] args)
        {
            string? transport = null;
            string? command = null;
            string? vaultPath = null;
            Uri? endpoint = null;
            string? apiKey = null;
            var timeoutSeconds = 60;
            var coordination = false;
            var commandArguments = new List<string>();
            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                switch (argument)
                {
                    case "--transport":
                        transport = RequireValue(args, ref index, argument);
                        break;
                    case "--command":
                        command = RequireValue(args, ref index, argument);
                        break;
                    case "--argument":
                        commandArguments.Add(RequireValue(args, ref index, argument));
                        break;
                    case "--vault":
                        vaultPath = RequireValue(args, ref index, argument);
                        break;
                    case "--endpoint":
                        endpoint = new Uri(RequireValue(args, ref index, argument), UriKind.Absolute);
                        break;
                    case "--api-key":
                        apiKey = RequireValue(args, ref index, argument);
                        break;
                    case "--timeout-seconds":
                        timeoutSeconds = int.Parse(RequireValue(args, ref index, argument), CultureInfo.InvariantCulture);
                        break;
                    case "--coordination":
                        coordination = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{argument}'.");
                }
            }

            if (!string.Equals(transport, "stdio", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("--transport must be 'stdio' or 'http'.");
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                throw new ArgumentException("--command is required.");
            }

            if (string.IsNullOrWhiteSpace(vaultPath))
            {
                throw new ArgumentException("--vault is required.");
            }

            if (string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase) && endpoint is null)
            {
                throw new ArgumentException("--endpoint is required for HTTP.");
            }

            if (timeoutSeconds is < 5 or > 600)
            {
                throw new ArgumentOutOfRangeException(nameof(args), timeoutSeconds, "Timeout must be between 5 and 600 seconds.");
            }

            return new SmokeOptions(
                transport!,
                command,
                commandArguments.ToArray(),
                Path.GetFullPath(vaultPath),
                endpoint,
                apiKey,
                timeoutSeconds,
                coordination);
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
