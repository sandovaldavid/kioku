using System.ComponentModel;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools backed by local text generation (Ollama). Requires KIOKU_GEN_MODEL to be
/// configured and Ollama running with that model pulled — degrades to
/// [error] [DEPENDENCY_UNAVAILABLE] otherwise. Never sends note content anywhere but
/// the configured KIOKU_OLLAMA_URL.
/// </summary>
[McpServerToolType]
public sealed class GenerationTools(
    VaultIndexService vault,
    GenerationService generation,
    KiokuConfiguration config,
    MetricsService? metrics = null)
{
    private static void Count(string name, MetricsService? metrics) => metrics?.RecordToolCall(name);

    [McpServerTool, Description(
        "Summarizes a note locally using Ollama (no cloud calls). Styles: 'bullets' (default), " +
        "'paragraph', or 'eli5' (explain like I'm 5). Requires KIOKU_GEN_MODEL configured and " +
        "Ollama running with that model pulled. Treat the output as a local draft, not a " +
        "final answer — quality depends on the configured model.")]
    public async Task<string> summarize_note(
        [Description("Name or path of the note to summarize.")] string note,
        [Description("Summary style: 'bullets' (default), 'paragraph', or 'eli5'.")] string style = "bullets",
        [Description("Approximate maximum word count for the summary (default: 150).")] int max_words = 150)
    {
        Count(nameof(summarize_note), metrics);

        if (!generation.IsAvailable)
        {
            return KiokuError.DependencyUnavailable(
                "Local generation is unavailable. Set KIOKU_GEN_MODEL to an Ollama model you have " +
                $"pulled (e.g. 'ollama pull llama3.2') and make sure Ollama is running at {config.OllamaUrl}.");
        }

        var found = NoteHelpers.ResolveNote(note, vault);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        if (string.IsNullOrWhiteSpace(found.PlainText))
        {
            return "[info] Note has no readable content to summarize.";
        }

        var instruction = style.Trim().ToLowerInvariant() switch
        {
            "paragraph" => $"Summarize the following note in a single coherent paragraph, no more than {max_words} words.",
            "eli5" => $"Explain the following note as if to a five-year-old, in simple everyday language, no more than {max_words} words.",
            _ => $"Summarize the following note as a concise bulleted list, no more than {max_words} words total.",
        };

        var prompt = $"{instruction}\n\n---\n{found.PlainText}\n---";

        var summary = await generation.GenerateAsync(prompt);
        if (summary is null)
        {
            return KiokuError.DependencyUnavailable(
                "Local generation failed. Check that Ollama is running and the configured " +
                $"KIOKU_GEN_MODEL ('{generation.GenerationModel}') is pulled.");
        }

        return $"{summary.Trim()}\n\n[info] Generated locally with {generation.GenerationModel}";
    }
}
