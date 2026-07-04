using System.Net;
using System.Net.Http.Json;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for GenerationTools.generate_flashcards. Each test gets its own temporary
/// vault (not shared via IClassFixture) since this tool writes files.
/// </summary>
public class GenerationToolsFlashcardsTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private const string QaJson = """[{"q": "What is Kioku?", "a": "An MCP server."}, {"q": "What language?", "a": "C#."}]""";
    private const string ClozeJson = """[{"cloze": "Kioku is an ==MCP server=="}, {"cloze": "Written in ==C#=="}]""";

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
                Content = JsonContent.Create(new { response = QaJson }),
            };
            return Task.FromResult(response);
        });

        var generation = new GenerationService(config, NullLogger<GenerationService>.Instance, new FakeHttpClientFactory(handler));
        await generation.InitializeAsync();

        return (new GenerationTools(_fixture.Index, generation, config, vaultConfig), generation);
    }

    [Fact]
    public async Task GenerateFlashcards_NoModelConfigured_ReturnsDependencyUnavailable()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath, GenerationModel = null };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var generation = new GenerationService(config, NullLogger<GenerationService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));
        var tools = new GenerationTools(_fixture.Index, generation, config, vaultConfig);

        var result = await tools.generate_flashcards("Note One");

        Assert.Contains("[error] [DEPENDENCY_UNAVAILABLE]", result);
    }

    [Fact]
    public async Task GenerateFlashcards_NoteNotFound_ReturnsNotFound()
    {
        var (tools, _) = await CreateAvailableAsync();

        var result = await tools.generate_flashcards("Nonexistent Note");

        Assert.Contains("[error] [NOT_FOUND]", result);
    }

    [Fact]
    public async Task GenerateFlashcards_UnknownFormat_ReturnsInvalidArgument()
    {
        var (tools, _) = await CreateAvailableAsync();

        var result = await tools.generate_flashcards("Note One", format: "powerpoint");

        Assert.Contains("[error] [INVALID_ARGUMENT]", result);
    }

    [Fact]
    public async Task GenerateFlashcards_ZeroCount_ReturnsInvalidArgument()
    {
        var (tools, _) = await CreateAvailableAsync();

        var result = await tools.generate_flashcards("Note One", count: 0);

        Assert.Contains("[error] [INVALID_ARGUMENT]", result);
    }

    [Fact]
    public async Task GenerateFlashcards_SpacedRepetitionFormat_RendersQAndAWithFlashcardsTag()
    {
        var (tools, _) = await CreateAvailableAsync();

        var result = await tools.generate_flashcards("Note One", dry_run: true);

        Assert.Contains("#flashcards", result);
        Assert.Contains("What is Kioku?::An MCP server.", result);
        Assert.Contains("What language?::C#.", result);
    }

    [Fact]
    public async Task GenerateFlashcards_ClozeFormat_RendersHighlightedText()
    {
        var (tools, _) = await CreateAvailableAsync(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { response = ClozeJson }),
        });

        var result = await tools.generate_flashcards("Note One", format: "cloze", dry_run: true);

        Assert.Contains("#flashcards", result);
        Assert.Contains("Kioku is an ==MCP server==", result);
        Assert.Contains("Written in ==C#==", result);
    }

    [Fact]
    public async Task GenerateFlashcards_AnkiCsvFormat_RendersEscapedCsv()
    {
        const string jsonWithSpecialChars = """[{"q": "What is \"Kioku\"?", "a": "A server, an MCP one."}]""";
        var (tools, _) = await CreateAvailableAsync(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { response = jsonWithSpecialChars }),
        });

        var result = await tools.generate_flashcards("Note One", format: "anki-csv", dry_run: true);

        Assert.Contains("front,back,tags", result);
        Assert.Contains("\"What is \"\"Kioku\"\"?\"", result);
        Assert.Contains("\"A server, an MCP one.\"", result);
    }

    [Fact]
    public async Task GenerateFlashcards_DryRun_DoesNotWriteAnyFile()
    {
        var (tools, _) = await CreateAvailableAsync();

        await tools.generate_flashcards("Note One", dry_run: true);

        Assert.False(File.Exists(Path.Combine(_fixture.VaultPath, "Flashcards", "Note One.md")));
    }

    [Fact]
    public async Task GenerateFlashcards_SpacedRepetition_WritesNoteWithFrontmatterAndSource()
    {
        var (tools, _) = await CreateAvailableAsync();

        var result = await tools.generate_flashcards("Note One");

        Assert.Contains("[ok]", result);
        var path = Path.Combine(_fixture.VaultPath, "Flashcards", "Note One.md");
        Assert.True(File.Exists(path));

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("type: flashcards", content);
        Assert.Contains("source: \"[[Note One]]\"", content);
        Assert.Contains("#flashcards", content);
        Assert.Contains("What is Kioku?::An MCP server.", content);
    }

    [Fact]
    public async Task GenerateFlashcards_AnkiCsv_WritesRawCsvFileWithoutFrontmatter()
    {
        var (tools, _) = await CreateAvailableAsync();

        var result = await tools.generate_flashcards("Note One", format: "anki-csv");

        Assert.Contains("[ok]", result);
        var path = Path.Combine(_fixture.VaultPath, "Assets", "Note One-flashcards.csv");
        Assert.True(File.Exists(path));

        var content = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("---", content);
        Assert.Contains("front,back,tags", content);
    }

    [Fact]
    public async Task GenerateFlashcards_CustomOutputNote_WritesToSpecifiedPath()
    {
        var (tools, _) = await CreateAvailableAsync();

        var result = await tools.generate_flashcards("Note One", output_note: "Custom/My Cards");

        Assert.Contains("[ok]", result);
        Assert.True(File.Exists(Path.Combine(_fixture.VaultPath, "Custom", "My Cards.md")));
    }

    [Fact]
    public async Task GenerateFlashcards_ModelReturnsInvalidJsonTwice_ReturnsInternalErrorAfterRetry()
    {
        var callCount = 0;
        var (tools, _) = await CreateAvailableAsync(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = "this is not json at all" }),
            };
        });

        var result = await tools.generate_flashcards("Note One");

        Assert.Contains("[error] [INTERNAL] model output could not be parsed", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GenerateFlashcards_ModelReturnsInvalidJsonThenValidOnRetry_Succeeds()
    {
        var callCount = 0;
        var (tools, _) = await CreateAvailableAsync(_ =>
        {
            callCount++;
            var response = callCount == 1 ? "not valid json" : QaJson;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response }),
            };
        });

        var result = await tools.generate_flashcards("Note One", dry_run: true);

        Assert.Contains("What is Kioku?::An MCP server.", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GenerateFlashcards_ModelWrapsJsonInMarkdownFence_StillParsesCorrectly()
    {
        var (tools, _) = await CreateAvailableAsync(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { response = $"```json\n{QaJson}\n```" }),
        });

        var result = await tools.generate_flashcards("Note One", dry_run: true);

        Assert.Contains("What is Kioku?::An MCP server.", result);
    }

    [Fact]
    public async Task GenerateFlashcards_EmptyNoteBody_ReturnsInfoMessageWithoutCallingOllama()
    {
        await _fixture.CreateNoteAsync("Blank Note", "");
        await _fixture.Index.RebuildIndexAsync();

        var called = false;
        var (tools, _) = await CreateAvailableAsync(_ =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = QaJson }),
            };
        });

        var result = await tools.generate_flashcards("Blank Note");

        Assert.Contains("[info]", result);
        Assert.False(called);
    }
}
