using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Http;
using Kioku.Mcp.Server.Protocol;
using Kioku.Mcp.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class McpSurfaceMetadataContractTests
{
    private static readonly string[] RequiredEnvelopeProperties = ["success", "data", "error", "pagination", "warnings"];

    [Fact]
    public async Task Default_profile_exposes_44_tools_with_truthful_schemas_and_annotations()
    {
        var tempVault = Path.Combine(Path.GetTempPath(), $"kioku-contract-default-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempVault);
        try
        {
            await using var server = await StartTestServerAsync(tempVault, enableAllCapabilities: false);
            using var client = CreateHttpClient();
            await InitializeMcpSessionAsync(server.BaseUrl, client);

            var tools = await FetchToolsListAsync(server.BaseUrl, client);
            Assert.Equal(44, tools.Count);

            foreach (var tool in tools)
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.Name));
                Assert.NotNull(tool.Annotations);
                Assert.NotNull(tool.OutputSchema);
                AssertValidOutputSchema(tool.OutputSchema.Value);

                var expectedAnnotations = KiokuToolAnnotations.Create(tool.Name);
                Assert.Equal(expectedAnnotations.ReadOnlyHint, tool.Annotations.ReadOnlyHint);
                Assert.Equal(expectedAnnotations.DestructiveHint, tool.Annotations.DestructiveHint);
                Assert.Equal(expectedAnnotations.IdempotentHint, tool.Annotations.IdempotentHint);
                Assert.Equal(expectedAnnotations.OpenWorldHint, tool.Annotations.OpenWorldHint);
            }
        }
        finally
        {
            if (Directory.Exists(tempVault))
            {
                Directory.Delete(tempVault, recursive: true);
            }
        }
    }

    [Fact]
    public async Task All_capabilities_profile_exposes_77_tools_with_truthful_schemas_and_annotations()
    {
        var tempVault = Path.Combine(Path.GetTempPath(), $"kioku-contract-all-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempVault);
        try
        {
            await using var server = await StartTestServerAsync(tempVault, enableAllCapabilities: true);
            using var client = CreateHttpClient();
            await InitializeMcpSessionAsync(server.BaseUrl, client);

            var tools = await FetchToolsListAsync(server.BaseUrl, client);
            Assert.Equal(77, tools.Count);

            foreach (var tool in tools)
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.Name));
                Assert.NotNull(tool.Annotations);
                Assert.NotNull(tool.OutputSchema);
                AssertValidOutputSchema(tool.OutputSchema.Value);

                var expectedAnnotations = KiokuToolAnnotations.Create(tool.Name);
                Assert.Equal(expectedAnnotations.ReadOnlyHint, tool.Annotations.ReadOnlyHint);
                Assert.Equal(expectedAnnotations.DestructiveHint, tool.Annotations.DestructiveHint);
                Assert.Equal(expectedAnnotations.IdempotentHint, tool.Annotations.IdempotentHint);
                Assert.Equal(expectedAnnotations.OpenWorldHint, tool.Annotations.OpenWorldHint);
            }
        }
        finally
        {
            if (Directory.Exists(tempVault))
            {
                Directory.Delete(tempVault, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Structured_result_envelope_validates_against_declared_output_schema_for_representative_tools()
    {
        var tempVault = Path.Combine(Path.GetTempPath(), $"kioku-contract-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempVault);
        try
        {
            await using var server = await StartTestServerAsync(tempVault, enableAllCapabilities: true);
            using var client = CreateHttpClient();
            await InitializeMcpSessionAsync(server.BaseUrl, client);

            // 1. Pure read: get_server_status
            var statusResult = await CallToolJsonRpcAsync(server.BaseUrl, client, "get_server_status", new { });
            AssertValidStructuredEnvelope(statusResult);

            // 2. Pure read: list_projects
            var projectsResult = await CallToolJsonRpcAsync(server.BaseUrl, client, "list_projects", new { });
            AssertValidStructuredEnvelope(projectsResult);

            // 3. Structured search: list_notes
            var listNotesResult = await CallToolJsonRpcAsync(server.BaseUrl, client, "list_notes", new { limit = 10 });
            AssertValidStructuredEnvelope(listNotesResult);

            // 4. Session / task: list_tasks
            var tasksResult = await CallToolJsonRpcAsync(server.BaseUrl, client, "list_tasks", new { });
            AssertValidStructuredEnvelope(tasksResult);

            // 5. Mixed preview / apply: process_inbox (apply=false preview)
            var inboxResult = await CallToolJsonRpcAsync(server.BaseUrl, client, "process_inbox", new { apply = false });
            AssertValidStructuredEnvelope(inboxResult);

            // 6. Error envelope: read_note on missing note
            var notFoundResult = await CallToolJsonRpcAsync(server.BaseUrl, client, "read_note", new { note = "NonExistentNote999" });
            AssertValidStructuredEnvelope(notFoundResult);
            AssertErrorEnvelope(notFoundResult, "NOT_FOUND");
        }
        finally
        {
            if (Directory.Exists(tempVault))
            {
                Directory.Delete(tempVault, recursive: true);
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client;
    }

    private static void AssertValidOutputSchema(JsonElement schema)
    {
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.True(schema.TryGetProperty("type", out var typeProp));
        Assert.Equal("object", typeProp.GetString());

        Assert.True(schema.TryGetProperty("properties", out var props));
        foreach (var reqProp in RequiredEnvelopeProperties)
        {
            Assert.True(props.TryGetProperty(reqProp, out _), $"OutputSchema missing property '{reqProp}'");
        }
    }

    private static void AssertValidStructuredEnvelope(JsonElement resultElement)
    {
        Assert.True(resultElement.TryGetProperty("structuredContent", out var envelope), "Response missing structuredContent");
        Assert.Equal(JsonValueKind.Object, envelope.ValueKind);

        Assert.True(envelope.TryGetProperty("success", out var successProp));
        Assert.True(successProp.ValueKind is JsonValueKind.True or JsonValueKind.False);

        Assert.True(envelope.TryGetProperty("data", out _));
        Assert.True(envelope.TryGetProperty("error", out var errorProp));
        Assert.True(errorProp.ValueKind is JsonValueKind.Object or JsonValueKind.Null);

        Assert.True(envelope.TryGetProperty("pagination", out var pageProp));
        Assert.True(pageProp.ValueKind is JsonValueKind.Object or JsonValueKind.Null);

        Assert.True(envelope.TryGetProperty("warnings", out var warningsProp));
        Assert.Equal(JsonValueKind.Array, warningsProp.ValueKind);
    }

    private static void AssertErrorEnvelope(JsonElement resultElement, string expectedCode)
    {
        var envelope = resultElement.GetProperty("structuredContent");
        Assert.False(envelope.GetProperty("success").GetBoolean());
        var error = envelope.GetProperty("error");
        Assert.Equal(JsonValueKind.Object, error.ValueKind);
        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
    }

    private static async Task<TestHttpServer> StartTestServerAsync(string vaultPath, bool enableAllCapabilities)
    {
        if (enableAllCapabilities)
        {
            var configDir = Path.Combine(vaultPath, ".kioku");
            Directory.CreateDirectory(configDir);
            await File.WriteAllTextAsync(
                Path.Combine(configDir, "config.yml"),
                "capabilities:\n  require_explicit: true\n  enabled: [tasks, organization, sessions, workflows, graph, engineering, research, generation, css, assets, bridge, plugin, coordination]\n");
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration["Kioku:VaultPath"] = vaultPath;

        var config = KiokuOptionsConfiguration.GetValidated(builder.Configuration).ToConfiguration();
        builder.Services.AddSingleton(config);
        builder.Services.AddKiokuRuntime(builder.Configuration);

        HttpTransportSecurity.ConfigureBuilder(builder, config);

        var capabilities = VaultCapabilityProfile.Load(vaultPath);
        var mcpBuilder = builder.Services.AddMcpServer().WithHttpTransport();

        mcpBuilder
            .WithTools<Tools.NoteQueryTools>()
            .WithTools<Tools.NoteCommandTools>()
            .WithTools<Tools.FocusedCreationTools>()
            .WithTools<Tools.UtilityTools>();

        if (capabilities.IsEnabled("tasks"))
        {
            mcpBuilder.WithTools<Tools.TaskManagementTools>();
        }

        if (capabilities.IsEnabled("organization"))
        {
            mcpBuilder.WithTools<Tools.VaultOrganizationTools>();
        }

        if (capabilities.IsEnabled("sessions"))
        {
            mcpBuilder.WithTools<Tools.SessionContextTools>();
        }

        if (capabilities.IsEnabled("workflows"))
        {
            mcpBuilder.WithTools<Tools.WorkflowTools>();
        }

        if (capabilities.IsEnabled("css"))
        {
            mcpBuilder.WithTools<Tools.CssThemingTools>();
        }

        if (capabilities.IsEnabled("graph"))
        {
            mcpBuilder.WithTools<Tools.KnowledgeGraphTools>();
            mcpBuilder.WithTools<Tools.GraphAnalysisTools>();
        }

        if (capabilities.IsEnabled("research"))
        {
            mcpBuilder.WithTools<Tools.ResearchTools>();
        }

        if (capabilities.IsEnabled("bridge"))
        {
            mcpBuilder.WithTools<Tools.ObsidianBridgeTools>();
        }

        if (capabilities.IsEnabled("plugin"))
        {
            mcpBuilder.WithTools<Tools.PluginIntegrationTools>();
        }

        if (capabilities.IsEnabled("assets"))
        {
            mcpBuilder.WithTools<Tools.AssetTools>();
        }

        if (capabilities.IsEnabled("generation"))
        {
            mcpBuilder.WithTools<Tools.GenerationTools>();
        }

        if (capabilities.IsEnabled("engineering"))
        {
            mcpBuilder.WithTools<Tools.EngineeringWorkflowTools>();
        }

        if (capabilities.IsEnabled("coordination"))
        {
            mcpBuilder.WithTools<Tools.CoordinationTools>();
        }

        mcpBuilder.WithKiokuTypedResults();

        var app = builder.Build();
        HttpTransportSecurity.Use(app, config);
        app.MapMcp("/mcp");

        var readiness = app.Services.GetRequiredService<HttpReadinessState>();
        readiness.MarkIndexReady();

        var index = app.Services.GetRequiredService<VaultIndexService>();
        await index.RebuildIndexAsync();

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        return new TestHttpServer(app, addresses.First());
    }

    private static async Task InitializeMcpSessionAsync(string baseUrl, HttpClient client)
    {
        var initPayload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = "contract-test-client", version = "1.0" },
            }
        };

        var initContent = new StringContent(JsonSerializer.Serialize(initPayload), Encoding.UTF8, "application/json");
        var initResponse = await client.PostAsync($"{baseUrl}/mcp", initContent);
        initResponse.EnsureSuccessStatusCode();

        if (initResponse.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues))
        {
            var sessionId = sessionValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                client.DefaultRequestHeaders.Add("Mcp-Session-Id", sessionId);
            }
        }

        var initializedNotification = new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized",
            @params = new { }
        };

        var notifyContent = new StringContent(JsonSerializer.Serialize(initializedNotification), Encoding.UTF8, "application/json");
        await client.PostAsync($"{baseUrl}/mcp", notifyContent);
    }

    private static async Task<List<DiscoveredTool>> FetchToolsListAsync(string baseUrl, HttpClient client)
    {
        var requestPayload = new
        {
            jsonrpc = "2.0",
            id = 100,
            method = "tools/list",
            @params = new { }
        };

        var content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{baseUrl}/mcp", content);
        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync();
            Assert.Fail($"HTTP {response.StatusCode}: {errBody}");
        }

        var responseText = await response.Content.ReadAsStringAsync();
        var jsonText = ExtractJsonFromResponse(responseText);

        using var doc = JsonDocument.Parse(jsonText);
        var toolsArray = doc.RootElement.GetProperty("result").GetProperty("tools");

        var result = new List<DiscoveredTool>();
        foreach (var toolElem in toolsArray.EnumerateArray())
        {
            var name = toolElem.GetProperty("name").GetString()!;
            ToolAnnotations? annotations = null;
            if (toolElem.TryGetProperty("annotations", out var annElem) && annElem.ValueKind == JsonValueKind.Object)
            {
                annotations = new ToolAnnotations
                {
                    ReadOnlyHint = annElem.TryGetProperty("readOnlyHint", out var ro) && ro.GetBoolean(),
                    DestructiveHint = annElem.TryGetProperty("destructiveHint", out var d) && d.GetBoolean(),
                    IdempotentHint = annElem.TryGetProperty("idempotentHint", out var id) && id.GetBoolean(),
                    OpenWorldHint = annElem.TryGetProperty("openWorldHint", out var ow) && ow.GetBoolean(),
                };
            }

            JsonElement? outputSchema = null;
            if (toolElem.TryGetProperty("outputSchema", out var schemaElem) && schemaElem.ValueKind == JsonValueKind.Object)
            {
                outputSchema = schemaElem.Clone();
            }

            result.Add(new DiscoveredTool(name, annotations, outputSchema));
        }

        return result;
    }

    private static async Task<JsonElement> CallToolJsonRpcAsync(string baseUrl, HttpClient client, string toolName, object arguments)
    {
        var requestPayload = new
        {
            jsonrpc = "2.0",
            id = 200,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments,
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"{baseUrl}/mcp", content);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync();
        var jsonText = ExtractJsonFromResponse(responseText);

        using var doc = JsonDocument.Parse(jsonText);
        if (doc.RootElement.TryGetProperty("error", out var rpcError))
        {
            Assert.Fail($"JSON-RPC call to '{toolName}' returned error: {rpcError}");
        }

        return doc.RootElement.GetProperty("result").Clone();
    }

    private static string ExtractJsonFromResponse(string responseText)
    {
        var trimmed = responseText.Trim();
        if (trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        foreach (var line in trimmed.Split('\n'))
        {
            var lineTrimmed = line.Trim();
            if (lineTrimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return lineTrimmed["data:".Length..].Trim();
            }
        }

        return trimmed;
    }

    private sealed record TestHttpServer(WebApplication App, string BaseUrl) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    private sealed record DiscoveredTool(string Name, ToolAnnotations? Annotations, JsonElement? OutputSchema);
}
