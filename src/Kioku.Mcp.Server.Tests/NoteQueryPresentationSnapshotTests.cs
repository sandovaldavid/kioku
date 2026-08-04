using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Golden/snapshot coverage for NoteQueryTools' exact textual and JSON output, captured against
/// the pre-extraction implementation (#250 slice 5). These pin today's byte-for-byte wording so
/// the query-logic/presentation split can be proven behavior-preserving: every assertion here
/// must keep passing, unchanged, after the extraction lands. A diff means output changed, not
/// that the test was "wrong."
/// </summary>
public sealed class NoteQueryPresentationSnapshotTests : IClassFixture<VaultFixture>
{
    private readonly VaultFixture _fixture;

    public NoteQueryPresentationSnapshotTests(VaultFixture fixture)
    {
        _fixture = fixture;
    }

    private NoteQueryTools CreateTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        return new NoteQueryTools(new NoteQueryService(_fixture.Index, config, null!, null!));
    }

    private NoteQueryTools CreateToolsWithSearchServices()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var embedding = new EmbeddingService(
            config,
            NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))));
        var hybrid = new HybridSearchService(_fixture.Index, embedding);
        return new NoteQueryTools(new NoteQueryService(_fixture.Index, config, embedding, hybrid));
    }

    private static string ModifiedStamp(Kioku.Mcp.Server.Domain.Note note) =>
        note.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    // read_note

    [Fact]
    public async Task read_note_text_returns_raw_file_content()
    {
        var tools = CreateTools();
        var expected = await File.ReadAllTextAsync(_fixture.GetNotePath("Note One"));

        var result = await tools.read_note("Note One");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task read_note_json_returns_name_path_content_envelope()
    {
        var tools = CreateTools();
        var expectedContent = await File.ReadAllTextAsync(_fixture.GetNotePath("Note One"));

        var result = await tools.read_note("Note One", format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.Equal("Note One", root.GetProperty("name").GetString());
        Assert.Equal("Note One.md", root.GetProperty("path").GetString());
        Assert.Equal(expectedContent, root.GetProperty("content").GetString());
        Assert.Equal(VaultRevision.Compute(expectedContent), root.GetProperty("revision").GetString());
    }

    [Fact]
    public async Task read_note_text_not_found_returns_hint()
    {
        var tools = CreateTools();

        var result = await tools.read_note("Does Not Exist");

        Assert.Equal(
            "[error] [NOT_FOUND] Note not found: 'Does Not Exist'. Use list_notes to see available notes.",
            result);
    }

    [Fact]
    public async Task read_note_json_not_found_omits_hint_suffix()
    {
        var tools = CreateTools();

        var result = await tools.read_note("Does Not Exist", format: "json");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Note not found: 'Does Not Exist'", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task read_note_metadata_only_text_lists_tags_and_status()
    {
        var tools = CreateTools();
        var note = _fixture.Index.GetNote(_fixture.GetNotePath("Note One"))!;

        var result = await tools.read_note("Note One", metadata_only: true);

        Assert.Equal(
            "**Note One**\n" +
            "Path: Note One.md\n" +
            $"Modified: {ModifiedStamp(note)}\n" +
            "Tags: #alpha, #beta\n" +
            "Status: draft",
            result);
    }

    [Fact]
    public async Task read_note_metadata_only_json_includes_extra_fields_and_link_count()
    {
        var tools = CreateTools();

        var result = await tools.read_note("Note Three", metadata_only: true, format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("Note Three", root.GetProperty("name").GetString());
        Assert.Equal("delta", Assert.Single(root.GetProperty("tags").EnumerateArray()).GetString());
        Assert.Equal(2, root.GetProperty("outgoing_links").GetArrayLength());
    }

    // list_notes

    [Fact]
    public void list_notes_text_shows_pagination_header_and_tags()
    {
        var tools = CreateTools();
        var note = _fixture.Index.GetNote(_fixture.GetNotePath("Note One"))!;

        var result = tools.list_notes(folder: "", tag: "alpha", format: "text");

        Assert.Equal(
            $"Showing 1-1 of 1 note(s):\n- Note One.md [#alpha, #beta] (modified: {ModifiedStamp(note)})",
            result);
    }

    [Fact]
    public void list_notes_text_in_folder_mentions_folder_name()
    {
        var tools = CreateTools();

        var result = tools.list_notes(folder: "Projects");

        Assert.StartsWith("Showing 1-2 of 2 note(s) in 'Projects':\n", result);
    }

    [Fact]
    public void list_notes_json_returns_expected_shape()
    {
        var tools = CreateTools();

        var result = tools.list_notes(folder: "Projects", format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal(2, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("offset").GetInt32());
        Assert.Equal(20, root.GetProperty("limit").GetInt32());
        Assert.Equal("Projects", root.GetProperty("folder").GetString());
        var notes = root.GetProperty("notes").EnumerateArray().ToList();
        Assert.Equal(2, notes.Count);
        Assert.Equal(8, notes[0].EnumerateObject().Count());
        Assert.False(string.IsNullOrWhiteSpace(notes[0].GetProperty("revision").GetString()));
    }

    [Fact]
    public void list_notes_text_empty_folder_message()
    {
        var tools = CreateTools();

        var result = tools.list_notes(folder: "Nonexistent");

        Assert.Equal("No matching notes in folder 'Nonexistent' (or the requested page is empty).", result);
    }

    [Fact]
    public void list_notes_text_empty_vault_wide_message()
    {
        var tools = CreateTools();

        var result = tools.list_notes(tag: "no-such-tag");

        Assert.Equal("No notes match (or the requested page is empty).", result);
    }

    [Fact]
    public void list_notes_json_empty_returns_empty_array_shape()
    {
        var tools = CreateTools();

        var result = tools.list_notes(tag: "no-such-tag", format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("notes").GetArrayLength());
    }

    [Fact]
    public void list_notes_text_offset_below_zero_is_rejected()
    {
        var tools = CreateTools();

        var result = tools.list_notes(offset: -1);

        Assert.Equal("[error] [INVALID_ARGUMENT] 'offset' must be 0 or greater.", result);
    }

    [Fact]
    public void list_notes_json_offset_below_zero_is_rejected()
    {
        var tools = CreateTools();

        var result = tools.list_notes(offset: -1, format: "json");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("'offset' must be 0 or greater.", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void list_notes_text_limit_not_positive_is_rejected()
    {
        var tools = CreateTools();

        var result = tools.list_notes(limit: 0);

        Assert.Equal("[error] [INVALID_ARGUMENT] 'limit' must be greater than 0.", result);
    }

    [Fact]
    public void list_notes_text_invalid_date_format_is_rejected()
    {
        var tools = CreateTools();

        var result = tools.list_notes(date_from: "not-a-date");

        Assert.Equal("[error] [INVALID_ARGUMENT] 'date_from' and 'date_to' must use YYYY-MM-DD.", result);
    }

    [Fact]
    public void list_notes_text_date_from_after_date_to_is_rejected()
    {
        var tools = CreateTools();

        var result = tools.list_notes(date_from: "2026-01-01", date_to: "2025-01-01");

        Assert.Equal("[error] [INVALID_ARGUMENT] 'date_from' cannot be later than 'date_to'.", result);
    }

    [Fact]
    public void list_notes_text_index_not_ready_includes_retry_hint()
    {
        var index = new VaultIndexService(
            NullLogger<VaultIndexService>.Instance,
            new KiokuConfiguration { VaultPath = _fixture.VaultPath });
        var tools = new NoteQueryTools(new NoteQueryService(index, new KiokuConfiguration { VaultPath = _fixture.VaultPath }, null!, null!));

        var result = tools.list_notes();

        Assert.Equal("[loading] The index is still loading. Wait a moment and try again.", result);
    }

    [Fact]
    public void list_notes_json_index_not_ready_omits_retry_hint()
    {
        var index = new VaultIndexService(
            NullLogger<VaultIndexService>.Instance,
            new KiokuConfiguration { VaultPath = _fixture.VaultPath });
        var tools = new NoteQueryTools(new NoteQueryService(index, new KiokuConfiguration { VaultPath = _fixture.VaultPath }, null!, null!));

        var result = tools.list_notes(format: "json");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("[loading] The index is still loading.", doc.RootElement.GetProperty("error").GetString());
    }

    // search_notes

    [Fact]
    public async Task search_notes_keyword_text_no_results()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("zzznomatchzzz", mode: "keyword");

        Assert.Equal("No notes found for: 'zzznomatchzzz'", result);
    }

    [Fact]
    public async Task search_notes_keyword_json_no_results_shape()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("zzznomatchzzz", mode: "keyword", format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("zzznomatchzzz", root.GetProperty("query").GetString());
        Assert.Equal("keyword", root.GetProperty("mode").GetString());
        Assert.Equal(0, root.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task search_notes_keyword_text_reports_title_match_and_relevance()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("Note One", mode: "keyword");

        // The BM25 candidate count and score are corpus-dependent; the surrounding format
        // (header, rank, label, bold name, tag list, relevance suffix, path line) is not.
        Assert.Matches(
            @"^\d+ result\(s\) for 'Note One' \[keyword\]:\n\n" +
            @"1\. \[title\] \*\*Note One\*\* \[#alpha, #beta\] \(\d+% relevance\)\n   Note One\.md",
            result);
    }

    [Fact]
    public async Task search_notes_keyword_json_result_fields()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("Note One", mode: "keyword", format: "json");

        using var doc = JsonDocument.Parse(result);
        var first = doc.RootElement.GetProperty("results")[0];
        Assert.Equal(1, first.GetProperty("rank").GetInt32());
        Assert.Equal("Note One", first.GetProperty("name").GetString());
        Assert.Equal("Note One.md", first.GetProperty("path").GetString());
        Assert.Equal("title", first.GetProperty("match").GetString());
        Assert.True(first.TryGetProperty("score", out _));
        Assert.True(first.TryGetProperty("tags", out _));
        Assert.True(first.TryGetProperty("snippet", out _));
    }

    [Fact]
    public async Task search_notes_text_index_not_ready()
    {
        var index = new VaultIndexService(
            NullLogger<VaultIndexService>.Instance,
            new KiokuConfiguration { VaultPath = _fixture.VaultPath });
        var embedding = new EmbeddingService(
            new KiokuConfiguration { VaultPath = _fixture.VaultPath },
            NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))));
        var hybrid = new HybridSearchService(index, embedding);
        var tools = new NoteQueryTools(new NoteQueryService(index, new KiokuConfiguration { VaultPath = _fixture.VaultPath }, embedding, hybrid));

        var result = await tools.search_notes("anything");

        Assert.Equal("[loading] The index is still loading.", result);
    }

    [Fact]
    public async Task search_notes_text_empty_query_is_rejected()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("   ");

        Assert.Equal("[error] [INVALID_ARGUMENT] The 'query' parameter cannot be empty.", result);
    }

    [Fact]
    public async Task search_notes_text_non_positive_max_results_is_rejected()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("note", max_results: 0);

        Assert.Equal("[error] [INVALID_ARGUMENT] 'max_results' must be greater than 0.", result);
    }

    [Fact]
    public async Task search_notes_text_min_score_out_of_range_is_rejected()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("note", min_score: 2f);

        Assert.Equal(
            "[error] [INVALID_ARGUMENT] 'min_score' must be between 0 and 1, or -1 to use the mode default.",
            result);
    }

    [Fact]
    public async Task search_notes_text_unknown_mode_is_rejected()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("note", mode: "fuzzy");

        Assert.Equal(
            "[error] [INVALID_ARGUMENT] Unknown mode 'fuzzy'. Use 'hybrid', 'keyword', or 'semantic'.",
            result);
    }

    [Fact]
    public async Task search_notes_semantic_text_reports_ollama_unavailable_with_url()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("note", mode: "semantic");

        Assert.Equal(
            "[info] Semantic search unavailable — Ollama is not running at http://localhost:11434",
            result);
    }

    [Fact]
    public async Task search_notes_semantic_json_reports_ollama_unavailable_with_query_and_mode()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("note", mode: "semantic", format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("note", root.GetProperty("query").GetString());
        Assert.Equal("semantic", root.GetProperty("mode").GetString());
        Assert.Equal(
            "[info] Semantic search unavailable — Ollama is not running at http://localhost:11434",
            root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task search_notes_hybrid_text_degrades_to_keyword_only_label()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("Note One", mode: "hybrid");

        Assert.Matches(
            @"^\d+ result\(s\) for 'Note One' \[hybrid \(keyword only — Ollama unavailable\)\]:\n\n",
            result);
    }

    [Fact]
    public async Task search_notes_hybrid_json_no_results_mode_label()
    {
        var tools = CreateToolsWithSearchServices();

        var result = await tools.search_notes("zzznomatchzzz", mode: "hybrid", format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("hybrid", root.GetProperty("mode").GetString());
        Assert.Equal(0, root.GetProperty("results").GetArrayLength());
    }

    // get_links

    [Fact]
    public void get_links_text_both_directions_report_counts()
    {
        var tools = CreateTools();

        var result = tools.get_links("Note One", direction: "both");

        Assert.Equal(
            "1 note(s) link to '[[Note One]]':\n- [[Note Three]] → Note Three.md\n\n" +
            "The note 'Note One' does not contain outgoing links.",
            result);
    }

    [Fact]
    public void get_links_text_in_only_reports_backlinks_only()
    {
        var tools = CreateTools();

        var result = tools.get_links("Note One", direction: "in");

        Assert.Equal("1 note(s) link to '[[Note One]]':\n- [[Note Three]] → Note Three.md", result);
    }

    [Fact]
    public void get_links_text_out_only_reports_outgoing_only()
    {
        var tools = CreateTools();

        var result = tools.get_links("Note Three", direction: "out");

        Assert.Equal(
            "2 outgoing link(s) in 'Note Three':\n- [[Note One]]\n- [[Note Two]]",
            result);
    }

    [Fact]
    public void get_links_json_returns_backlinks_and_outgoing_links()
    {
        var tools = CreateTools();

        var result = tools.get_links("Note One", direction: "both", format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("Note One", root.GetProperty("note").GetString());
        Assert.Equal("Note One.md", root.GetProperty("path").GetString());
        var backlinks = root.GetProperty("backlinks").EnumerateArray().ToList();
        Assert.Single(backlinks);
        Assert.Equal("Note Three", backlinks[0].GetProperty("name").GetString());
        Assert.Equal(0, root.GetProperty("outgoing_links").GetArrayLength());
    }

    [Fact]
    public void get_links_text_unknown_direction_is_rejected()
    {
        var tools = CreateTools();

        var result = tools.get_links("Note One", direction: "sideways");

        Assert.Equal(
            "[error] [INVALID_ARGUMENT] Unknown direction 'sideways'. Use 'in', 'out', or 'both'.",
            result);
    }

    [Fact]
    public void get_links_json_unknown_direction_is_rejected()
    {
        var tools = CreateTools();

        var result = tools.get_links("Note One", direction: "sideways", format: "json");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal(
            "Unknown direction 'sideways'. Use 'in', 'out', or 'both'.",
            doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void get_links_text_note_not_found()
    {
        var tools = CreateTools();

        var result = tools.get_links("Does Not Exist");

        Assert.Equal("[error] [NOT_FOUND] Note not found: 'Does Not Exist'", result);
    }

    [Fact]
    public void get_links_json_note_not_found()
    {
        var tools = CreateTools();

        var result = tools.get_links("Does Not Exist", format: "json");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("Note not found: 'Does Not Exist'", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void get_links_text_index_not_ready()
    {
        var index = new VaultIndexService(
            NullLogger<VaultIndexService>.Instance,
            new KiokuConfiguration { VaultPath = _fixture.VaultPath });
        var tools = new NoteQueryTools(new NoteQueryService(index, new KiokuConfiguration { VaultPath = _fixture.VaultPath }, null!, null!));

        var result = tools.get_links("Note One");

        Assert.Equal("[loading] The index is still loading.", result);
    }

    // find_similar_notes

    [Fact]
    public void find_similar_notes_reports_ollama_unavailable_with_url()
    {
        var tools = CreateToolsWithSearchServices();

        var result = tools.find_similar_notes("Note One");

        Assert.Equal(
            "[info] Semantic search unavailable — Ollama is not running at http://localhost:11434",
            result);
    }
}

/// <summary>
/// Snapshot coverage for the presentation shapes that only appear once real (deterministic-fake)
/// embeddings are available: semantic/hybrid search results and find_similar_notes. Reuses the
/// checked-in EvalVault + deterministic embedding responder already relied on by
/// RetrievalRankingTests so scores are reproducible without a live Ollama. Exact score/percentage
/// values are intentionally not pinned (they are corpus/embedding-formula-dependent, not part of
/// the presentation contract) — only the surrounding literal format is.
/// </summary>
public sealed class NoteQuerySemanticPresentationSnapshotTests : IClassFixture<EvalVaultFixture>
{
    private readonly EvalVaultFixture _fixture;

    public NoteQuerySemanticPresentationSnapshotTests(EvalVaultFixture fixture)
    {
        _fixture = fixture;
    }

    private NoteQueryTools CreateTools() =>
        new(new NoteQueryService(
            _fixture.Vault,
            new KiokuConfiguration { VaultPath = _fixture.VaultPath },
            _fixture.Embedding,
            _fixture.Hybrid));

    [Fact]
    public async Task search_notes_semantic_text_reports_semantic_label_and_similarity()
    {
        var tools = CreateTools();

        var result = await tools.search_notes(
            "Getting Things Done productividad", mode: "semantic", min_score: 0.05f);

        // Semantic snippets are truncated plain text, which can itself span lines — Singleline
        // lets '.' cross those without loosening the rest of the anchored structural match.
        Assert.Matches(
            new Regex(
                @"^\d+ result\(s\) for 'Getting Things Done productividad' \[semantic\]:\n\n" +
                @"1\. \[semantic\] \*\*.+?\*\* (\[#.+?\] )?\(\d+% relevance\)\n   \S+\.md(\n   > .+)?",
                RegexOptions.Singleline),
            result);
    }

    [Fact]
    public async Task search_notes_semantic_json_result_shape()
    {
        var tools = CreateTools();

        var result = await tools.search_notes(
            "Getting Things Done productividad", mode: "semantic", min_score: 0.05f, format: "json");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("semantic", root.GetProperty("mode").GetString());
        var results = root.GetProperty("results").EnumerateArray().ToList();
        Assert.NotEmpty(results);
        var first = results[0];
        Assert.Equal(1, first.GetProperty("rank").GetInt32());
        Assert.Equal("semantic", first.GetProperty("match").GetString());
        Assert.True(first.TryGetProperty("snippet", out _));
    }

    [Fact]
    public async Task search_notes_hybrid_text_reports_fused_results()
    {
        var tools = CreateTools();

        var result = await tools.search_notes("Getting Things Done productividad", mode: "hybrid");

        Assert.Matches(
            @"^\d+ result\(s\) for 'Getting Things Done productividad' \[hybrid\]:\n\n" +
            @"1\. \[[a-z+]+\] \*\*.+\*\*",
            result);
    }

    [Fact]
    public void find_similar_notes_text_reports_similar_label_and_similarity()
    {
        var tools = CreateTools();

        var result = tools.find_similar_notes("GTD Getting Things Done", max_results: 5, min_score: 0.05f);

        Assert.Matches(
            @"^\d+ note\(s\) similar to 'GTD Getting Things Done':\n\n" +
            @"1\. \[similar\] \*\*.+\*\* (\[#.+\] )?\(\d+% similarity\)\n   \S+\.md",
            result);
    }

    [Fact]
    public void find_similar_notes_text_no_results_above_threshold()
    {
        var tools = CreateTools();

        var result = tools.find_similar_notes("GTD Getting Things Done", min_score: 0.999f);

        Assert.Equal(
            "No notes similar to 'GTD Getting Things Done' found above 100% similarity.",
            result);
    }
}
