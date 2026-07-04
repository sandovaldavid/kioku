using System.Net;
using System.Net.Http.Json;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class GenerationToolsTests : IClassFixture<VaultFixture>
{
    private readonly VaultFixture _fixture;

    public GenerationToolsTests(VaultFixture fixture)
    {
        _fixture = fixture;
    }

    private GenerationTools CreateTools(HttpMessageHandler? handler = null, string? generationModel = "llama3.2")
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath, GenerationModel = generationModel };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var generation = new GenerationService(
            config,
            NullLogger<GenerationService>.Instance,
            new FakeHttpClientFactory(handler ?? new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));
        return new GenerationTools(_fixture.Index, generation, config, vaultConfig);
    }

    private async Task<(GenerationTools tools, GenerationService generation)> CreateAvailableAsync(
        Func<HttpRequestMessage, HttpResponseMessage>? onGenerate = null)
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath, GenerationModel = "llama3.2" };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            var response = onGenerate?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = "- summary point" }),
            };
            return Task.FromResult(response);
        });

        var generation = new GenerationService(config, NullLogger<GenerationService>.Instance, new FakeHttpClientFactory(handler));
        await generation.InitializeAsync();

        return (new GenerationTools(_fixture.Index, generation, config, vaultConfig), generation);
    }

    [Fact]
    public async Task SummarizeNote_NoModelConfigured_ReturnsDependencyUnavailable()
    {
        var tools = CreateTools(generationModel: null);

        var result = await tools.summarize_note("Note One");

        Assert.Contains("[error] [DEPENDENCY_UNAVAILABLE]", result);
        Assert.Contains("KIOKU_GEN_MODEL", result);
    }

    [Fact]
    public async Task SummarizeNote_NoteNotFound_ReturnsNotFound()
    {
        var (tools, _) = await CreateAvailableAsync();

        var result = await tools.summarize_note("Nonexistent Note");

        Assert.Contains("[error] [NOT_FOUND]", result);
    }

    [Fact]
    public async Task SummarizeNote_DefaultStyle_ReturnsBulletsAndProvenanceNote()
    {
        var (tools, _) = await CreateAvailableAsync(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { response = "- Body of note one." }),
        });

        var result = await tools.summarize_note("Note One");

        Assert.Contains("- Body of note one.", result);
        Assert.Contains("[info] Generated locally with llama3.2", result);
    }

    [Fact]
    public async Task SummarizeNote_ParagraphStyle_SendsParagraphInstruction()
    {
        string? capturedPrompt = null;
        var (tools, _) = await CreateAvailableAsync(request =>
        {
            capturedPrompt = ReadPromptSync(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = "A single paragraph summary." }),
            };
        });

        var result = await tools.summarize_note("Note One", style: "paragraph");

        Assert.Contains("A single paragraph summary.", result);
        Assert.Contains("single coherent paragraph", capturedPrompt);
    }

    [Fact]
    public async Task SummarizeNote_Eli5Style_SendsEli5Instruction()
    {
        string? capturedPrompt = null;
        var (tools, _) = await CreateAvailableAsync(request =>
        {
            capturedPrompt = ReadPromptSync(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = "Like you're five." }),
            };
        });

        var result = await tools.summarize_note("Note One", style: "eli5");

        Assert.Contains("Like you're five.", result);
        Assert.Contains("five-year-old", capturedPrompt);
    }

    [Fact]
    public async Task SummarizeNote_GenerationFails_ReturnsDependencyUnavailable()
    {
        var (tools, _) = await CreateAvailableAsync(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { error = "model not found" }),
        });

        var result = await tools.summarize_note("Note One");

        Assert.Contains("[error] [DEPENDENCY_UNAVAILABLE]", result);
    }

    [Fact]
    public async Task SummarizeNote_EmptyNoteBody_ReturnsInfoMessageWithoutCallingOllama()
    {
        await _fixture.CreateNoteAsync("Blank Note", "");
        await _fixture.Index.RebuildIndexAsync();

        var called = false;
        var (tools, _) = await CreateAvailableAsync(request =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = "should not be called" }),
            };
        });

        var result = await tools.summarize_note("Blank Note");

        Assert.Contains("[info]", result);
        Assert.False(called);
    }

    private static string? ReadPromptSync(HttpRequestMessage request)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("prompt").GetString();
    }
}
