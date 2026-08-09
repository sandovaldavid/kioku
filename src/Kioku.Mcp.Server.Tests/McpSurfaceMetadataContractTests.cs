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
    public async Task Default_profile_exposes_45_tools_with_truthful_schemas_and_annotations()
    {
        var tempVault = Path.Combine(Path.GetTempPath(), $"kioku-contract-default-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempVault);
        try
        {
            await using var server = await StartTestServerAsync(tempVault, enableAllCapabilities: false);
            using var client = CreateHttpClient();
            await InitializeMcpSessionAsync(server.BaseUrl, client);

            var tools = await FetchToolsListAsync(server.BaseUrl, client);
            Assert.Equal(45, tools.Count);

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
    public async Task All_capabilities_profile_exposes_78_tools_with_truthful_schemas_and_annotations()
    {
        var tempVault = Path.Combine(Path.GetTempPath(), $"kioku-contract-all-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempVault);
        try
        {
            await using var server = await StartTestServerAsync(tempVault, enableAllCapabilities: true);
            using var client = CreateHttpClient();
            await InitializeMcpSessionAsync(server.BaseUrl, client);

            var tools = await FetchToolsListAsync(server.BaseUrl, client);
            Assert.Equal(78, tools.Count);

            var discoveredNames = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
            var reviewedNames = KiokuToolAnnotationsTests.ReviewedToolMatrix.Keys.ToHashSet(StringComparer.Ordinal);

            Assert.Equal(78, KiokuToolAnnotationsTests.ReviewedToolMatrix.Count);
            Assert.Equal(reviewedNames.OrderBy(x => x), discoveredNames.OrderBy(x => x));

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stdio_and_HTTP_transports_expose_identical_tool_names_annotations_and_output_schemas(bool enableAllCapabilities)
    {
        var tempVault = Path.Combine(Path.GetTempPath(), $"kioku-contract-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempVault);
        try
        {
            if (enableAllCapabilities)
            {
                var configDir = Path.Combine(tempVault, ".kioku");
                Directory.CreateDirectory(configDir);
                await File.WriteAllTextAsync(
                    Path.Combine(configDir, "config.yml"),
                    "capabilities:\n  require_explicit: true\n  enabled: [tasks, organization, sessions, workflows, graph, engineering, research, generation, css, assets, bridge, plugin, coordination]\n");
            }

            // 1. Discover surface over HTTP transport
            await using var httpServer = await StartTestServerAsync(tempVault, enableAllCapabilities);
            using var httpClient = CreateHttpClient();
            await InitializeMcpSessionAsync(httpServer.BaseUrl, httpClient);
            var httpTools = await FetchToolsListAsync(httpServer.BaseUrl, httpClient);

            // 2. Discover surface over stdio transport process
            await using var stdioServer = await CoordinationProcessServer.StartStdioAsync(tempVault, "parity-test-client");
            var stdioTools = await stdioServer.Client.ListToolsAsync();

            // 3. Compare tool counts (45 for default, 78 for all-capabilities)
            var expectedCount = enableAllCapabilities ? 78 : 45;
            Assert.Equal(expectedCount, httpTools.Count);
            Assert.Equal(expectedCount, stdioTools.Count);

            // 4. Compare tool names
            var httpNames = httpTools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var stdioNames = stdioTools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(httpNames, stdioNames);

            // 5. Compare annotations and outputSchemas for every tool
            var stdioMap = stdioTools.ToDictionary(t => t.Name, StringComparer.Ordinal);
            foreach (var httpTool in httpTools)
            {
                var stdioTool = stdioMap[httpTool.Name];
                Assert.NotNull(httpTool.Annotations);
                Assert.NotNull(stdioTool.ProtocolTool.Annotations);

                Assert.Equal(httpTool.Annotations.ReadOnlyHint, stdioTool.ProtocolTool.Annotations.ReadOnlyHint);
                Assert.Equal(httpTool.Annotations.DestructiveHint, stdioTool.ProtocolTool.Annotations.DestructiveHint);
                Assert.Equal(httpTool.Annotations.IdempotentHint, stdioTool.ProtocolTool.Annotations.IdempotentHint);
                Assert.Equal(httpTool.Annotations.OpenWorldHint, stdioTool.ProtocolTool.Annotations.OpenWorldHint);

                Assert.NotNull(httpTool.OutputSchema);
                Assert.NotNull(stdioTool.ProtocolTool.OutputSchema);
                Assert.Equal(
                    JsonSerializer.Serialize(httpTool.OutputSchema.Value),
                    JsonSerializer.Serialize(stdioTool.ProtocolTool.OutputSchema.Value));
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
            AssertSuccessEnvelope(statusResult);

            // 2. Pure read: list_projects
            var projectsResult = await CallToolJsonRpcAsync(server.BaseUrl, client, "list_projects", new { });
            AssertValidStructuredEnvelope(projectsResult);
            AssertSuccessEnvelope(projectsResult);

            // 3. Structured search: list_notes
            var listNotesResult = await CallToolJsonRpcAsync(server.BaseUrl, client, "list_notes", new { limit = 10 });
            AssertValidStructuredEnvelope(listNotesResult);
            AssertSuccessEnvelope(listNotesResult);

            // 4. Session / task: list_tasks
            var tasksResult = await CallToolJsonRpcAsync(server.BaseUrl, client, "list_tasks", new { });
            AssertValidStructuredEnvelope(tasksResult);
            AssertSuccessEnvelope(tasksResult);

            // 5. Mixed preview / apply: process_inbox (apply=false preview)
            var inboxResult = await CallToolJsonRpcAsync(server.BaseUrl, client, "process_inbox", new { apply = false });
            AssertValidStructuredEnvelope(inboxResult);
            AssertSuccessEnvelope(inboxResult);

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

    [Fact]
    public async Task Get_server_status_returns_success_true_even_if_ollama_is_unavailable()
    {
        var tempVault = Path.Combine(Path.GetTempPath(), $"kioku-contract-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempVault);
        try
        {
            await using var server = await StartTestServerAsync(tempVault, enableAllCapabilities: false);
            using var client = CreateHttpClient();
            await InitializeMcpSessionAsync(server.BaseUrl, client);

            var result = await CallToolJsonRpcAsync(server.BaseUrl, client, "get_server_status", new { });
            AssertValidStructuredEnvelope(result);

            var envelope = result.GetProperty("structuredContent");
            Assert.True(envelope.GetProperty("success").GetBoolean(), "get_server_status must return success=true");
            Assert.Equal(JsonValueKind.Null, envelope.GetProperty("error").ValueKind);
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
    public async Task Manage_tags_returns_success_true_and_warning_for_non_existent_tag()
    {
        var tempVault = Path.Combine(Path.GetTempPath(), $"kioku-contract-tags-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempVault);
        try
        {
            await using var server = await StartTestServerAsync(tempVault, enableAllCapabilities: true);
            using var client = CreateHttpClient();
            await InitializeMcpSessionAsync(server.BaseUrl, client);

            var result = await CallToolJsonRpcAsync(server.BaseUrl, client, "manage_tags", new { operation = "rename", old_tag = "nonexistenttag999", new_tag = "newtag" });
            AssertValidStructuredEnvelope(result);

            var envelope = result.GetProperty("structuredContent");
            Assert.True(envelope.GetProperty("success").GetBoolean(), "manage_tags no-op must return success=true");
            Assert.Equal(JsonValueKind.Null, envelope.GetProperty("error").ValueKind);

            var warnings = envelope.GetProperty("warnings");
            Assert.True(warnings.GetArrayLength() > 0, "manage_tags no-op must include warning message");
            Assert.Contains("not found", warnings[0].GetString(), StringComparison.OrdinalIgnoreCase);
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

        Assert.True(schema.TryGetProperty("required", out var reqArray));
        Assert.Equal(RequiredEnvelopeProperties.Length, reqArray.GetArrayLength());

        Assert.True(schema.TryGetProperty("additionalProperties", out var addProps));
        Assert.False(addProps.GetBoolean());
    }

    private static void AssertValidStructuredEnvelope(JsonElement resultElement)
    {
        Assert.True(resultElement.TryGetProperty("structuredContent", out var envelope), "Response missing structuredContent");
        Assert.Equal(JsonValueKind.Object, envelope.ValueKind);

        // Additional properties check: exactly 5 top-level properties
        var propNames = envelope.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(RequiredEnvelopeProperties.Length, propNames.Count);
        foreach (var reqProp in RequiredEnvelopeProperties)
        {
            Assert.True(propNames.Contains(reqProp), $"StructuredContent envelope missing required property '{reqProp}'");
        }

        Assert.True(envelope.TryGetProperty("success", out var successProp));
        Assert.True(successProp.ValueKind is JsonValueKind.True or JsonValueKind.False);
        var success = successProp.GetBoolean();

        Assert.True(envelope.TryGetProperty("data", out _));

        Assert.True(envelope.TryGetProperty("error", out var errorProp));
        if (success)
        {
            Assert.Equal(JsonValueKind.Null, errorProp.ValueKind);
        }
        else
        {
            Assert.Equal(JsonValueKind.Object, errorProp.ValueKind);
            Assert.True(errorProp.TryGetProperty("code", out var codeProp));
            Assert.Equal(JsonValueKind.String, codeProp.ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(codeProp.GetString()));

            Assert.True(errorProp.TryGetProperty("message", out var msgProp));
            Assert.Equal(JsonValueKind.String, msgProp.ValueKind);
        }

        Assert.True(envelope.TryGetProperty("pagination", out var pageProp));
        if (pageProp.ValueKind != JsonValueKind.Null)
        {
            Assert.Equal(JsonValueKind.Object, pageProp.ValueKind);
            Assert.True(pageProp.TryGetProperty("total", out var totalProp) && totalProp.GetInt32() >= 0);
            Assert.True(pageProp.TryGetProperty("offset", out var offsetProp) && offsetProp.GetInt32() >= 0);
            Assert.True(pageProp.TryGetProperty("limit", out var limitProp) && limitProp.GetInt32() >= 1);
            Assert.True(pageProp.TryGetProperty("has_more", out var hasMoreProp) && (hasMoreProp.ValueKind is JsonValueKind.True or JsonValueKind.False));
        }

        Assert.True(envelope.TryGetProperty("warnings", out var warningsProp));
        Assert.Equal(JsonValueKind.Array, warningsProp.ValueKind);
        foreach (var warningItem in warningsProp.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.String, warningItem.ValueKind);
        }
    }

    private static void AssertSuccessEnvelope(JsonElement resultElement)
    {
        var envelope = resultElement.GetProperty("structuredContent");
        Assert.True(envelope.GetProperty("success").GetBoolean(), "Expected success=true");
        Assert.Equal(JsonValueKind.Null, envelope.GetProperty("error").ValueKind);
    }

    private static void AssertErrorEnvelope(JsonElement resultElement, string expectedCode)
    {
        var envelope = resultElement.GetProperty("structuredContent");
        Assert.False(envelope.GetProperty("success").GetBoolean(), "Expected success=false");
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
