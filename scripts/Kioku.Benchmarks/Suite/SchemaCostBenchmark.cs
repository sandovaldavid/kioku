using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Kioku.Benchmarks.Suite;

/// <summary>
/// Measures the tool-schema JSON size served to an MCP client for the "default" and
/// "all-capabilities" profiles, and estimates a token cost from it. Spawns the real server
/// (mirroring scripts/generate-public-docs.mjs's inspectProfile/JsonLineMcpClient, but in C#
/// via the ModelContextProtocol.Client SDK instead of a second hand-rolled JSON-RPC client) and
/// captures the raw tools/list response for each profile.
///
/// Token estimate is a rough character-count proxy (ceil(utf8Bytes / 4)), NOT a real GPT-style
/// tokenizer count — no tokenizer library is a dependency of this repo. Treat it as an
/// order-of-magnitude approximation for comparing profiles, not an exact cost.
/// </summary>
public static class SchemaCostBenchmark
{
    public sealed record ProfileResult(string Profile, int ToolCount, long SchemaJsonBytes, long EstimatedTokens);

    public sealed record Result(ProfileResult Default, ProfileResult AllCapabilities, long TokenDelta);

    public static async Task<Result> RunAsync(
        string serverDllPath,
        string tempRoot,
        IReadOnlyList<string> allCapabilities,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("[loading] Schema cost: default profile...");
        var defaultResult = await InspectProfileAsync(serverDllPath, tempRoot, "default", null, cancellationToken);

        Console.WriteLine("[loading] Schema cost: all-capabilities profile...");
        var allResult = await InspectProfileAsync(
            serverDllPath, tempRoot, "all-capabilities", allCapabilities, cancellationToken);

        Console.WriteLine(
            $"[ok] default={defaultResult.ToolCount} tools/{defaultResult.EstimatedTokens} est. tokens, " +
            $"all-capabilities={allResult.ToolCount} tools/{allResult.EstimatedTokens} est. tokens.");

        return new Result(defaultResult, allResult, allResult.EstimatedTokens - defaultResult.EstimatedTokens);
    }

    private static async Task<ProfileResult> InspectProfileAsync(
        string serverDllPath,
        string tempRoot,
        string profileName,
        IReadOnlyList<string>? capabilities,
        CancellationToken cancellationToken)
    {
        var vaultPath = Path.Combine(tempRoot, $"kioku-bench-schema-{profileName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultPath);
        await File.WriteAllTextAsync(
            Path.Combine(vaultPath, "seed.md"), "# Documentation probe\n", cancellationToken);

        if (capabilities is not null)
        {
            var kiokuDir = Path.Combine(vaultPath, ".kioku");
            Directory.CreateDirectory(kiokuDir);
            var enabled = string.Join(", ", capabilities);
            await File.WriteAllTextAsync(
                Path.Combine(kiokuDir, "config.yml"),
                $"capabilities:\n  require_explicit: true\n  enabled: [{enabled}]\n",
                cancellationToken);
        }

        // Ollama is intentionally unreachable: schema discovery only needs tools/list, and
        // pointing at a real Ollama would only add latency/noise unrelated to schema size.
        var transport = ServerProcessHelper.CreateTransport(
            serverDllPath, vaultPath, $"kioku-benchmarks-schema-{profileName}", "http://127.0.0.1:9");

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        var json = SerializeToolSchemas(tools.Select(t => t.ProtocolTool));
        var bytes = Encoding.UTF8.GetByteCount(json);
        var estimatedTokens = (long)Math.Ceiling(bytes / 4.0);

        try
        {
            Directory.Delete(vaultPath, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }

        return new ProfileResult(profileName, tools.Count, bytes, estimatedTokens);
    }

    /// <summary>
    /// Hand-serializes the fields that make up a tool's public schema (name, description,
    /// input/output JSON Schema, behavioral annotations) via Utf8JsonWriter rather than
    /// reflection-based JsonSerializer, so this has no dependency on the SDK's own
    /// (possibly source-generated) contract types.
    /// </summary>
    private static string SerializeToolSchemas(IEnumerable<Tool> tools)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var tool in tools)
            {
                writer.WriteStartObject();
                writer.WriteString("name", tool.Name);
                writer.WriteString("description", tool.Description);

                writer.WritePropertyName("inputSchema");
                tool.InputSchema.WriteTo(writer);

                if (tool.OutputSchema is { } outputSchema)
                {
                    writer.WritePropertyName("outputSchema");
                    outputSchema.WriteTo(writer);
                }

                if (tool.Annotations is { } annotations)
                {
                    writer.WriteStartObject("annotations");
                    writer.WriteBoolean("readOnlyHint", annotations.ReadOnlyHint ?? false);
                    writer.WriteBoolean("destructiveHint", annotations.DestructiveHint ?? false);
                    writer.WriteBoolean("idempotentHint", annotations.IdempotentHint ?? false);
                    writer.WriteBoolean("openWorldHint", annotations.OpenWorldHint ?? false);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
