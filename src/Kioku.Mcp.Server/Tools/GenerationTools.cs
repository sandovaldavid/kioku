using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    VaultConfigService vaultConfig,
    MetricsService? metrics = null)
{
    private static void Count(string name, MetricsService? metrics) => metrics?.RecordToolCall(name);

    [McpServerTool, Description(
        "Summarizes a note locally via Ollama (no cloud calls). Styles: 'bullets' (default), " +
        "'paragraph', 'eli5'. Requires KIOKU_GEN_MODEL and Ollama; output quality depends on " +
        "the local model.")]
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

    [McpServerTool, Description(
        "Generates flashcards from a note locally via Ollama (no cloud calls). Formats: " +
        "'spaced-repetition' (Q::A markdown, default), 'anki-csv' (front,back,tags CSV), or " +
        "'cloze' (==hidden text== cards). Requires KIOKU_GEN_MODEL and Ollama; review the cards " +
        "before studying.")]
    public async Task<string> generate_flashcards(
        [Description("Name or path of the note to generate flashcards from.")] string note,
        [Description("Number of flashcards to generate (default: 10).")] int count = 10,
        [Description("Output format: 'spaced-repetition' (default), 'anki-csv', or 'cloze'.")] string format = "spaced-repetition",
        [Description("Path to write the flashcards to. Default: 'Flashcards/{note}.md' ('.csv' for anki-csv, in the assets folder).")] string output_note = "",
        [Description("Preview the generated flashcards without writing any file.")] bool dry_run = false)
    {
        Count(nameof(generate_flashcards), metrics);

        if (!generation.IsAvailable)
        {
            return KiokuError.DependencyUnavailable(
                "Local generation is unavailable. Set KIOKU_GEN_MODEL to an Ollama model you have " +
                $"pulled (e.g. 'ollama pull llama3.2') and make sure Ollama is running at {config.OllamaUrl}.");
        }

        var normalizedFormat = format.Trim().ToLowerInvariant();
        if (normalizedFormat is not ("spaced-repetition" or "anki-csv" or "cloze"))
        {
            return KiokuError.InvalidArgument(
                $"Unknown format '{format}'. Use 'spaced-repetition', 'anki-csv', or 'cloze'.");
        }

        if (count < 1)
        {
            return KiokuError.InvalidArgument("count must be at least 1.");
        }

        var found = NoteHelpers.ResolveNote(note, vault);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        if (string.IsNullOrWhiteSpace(found.PlainText))
        {
            return "[info] Note has no readable content to generate flashcards from.";
        }

        var isCloze = normalizedFormat == "cloze";
        var prompt = $"Generate exactly {count} flashcards from the following note:\n\n---\n{found.PlainText}\n---";
        var systemPrompt = isCloze ? ClozeSystemPrompt(count) : QaSystemPrompt(count);

        var (qaCards, clozeCards) = await GenerateCardsWithRetryAsync(prompt, systemPrompt, isCloze);
        if (qaCards is null && clozeCards is null)
        {
            return KiokuError.Internal("model output could not be parsed");
        }

        var rendered = normalizedFormat switch
        {
            "anki-csv" => RenderAnkiCsv(qaCards!),
            "cloze" => RenderCloze(clozeCards!),
            _ => RenderSpacedRepetition(qaCards!),
        };

        var actualCount = isCloze ? clozeCards!.Count : qaCards!.Count;

        if (dry_run)
        {
            return $"[ok] Generated {actualCount} flashcard(s) (dry run, not written):\n\n{rendered}";
        }

        if (normalizedFormat == "anki-csv")
        {
            var csvPath = ResolveCsvPath(output_note, found.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);
            await File.WriteAllTextAsync(csvPath, rendered, NoteHelpers.Utf8NoBom);
            var relCsvPath = Path.GetRelativePath(config.VaultPath, csvPath).Replace('\\', '/');
            return $"[ok] Generated {actualCount} flashcard(s), written to {relCsvPath}:\n\n{rendered}";
        }

        var notePath = ResolveFlashcardNotePath(output_note, found.Name);
        var frontmatter = NoteHelpers.BuildFrontmatter(
            [], "flashcards",
            extraFields: new Dictionary<string, string> { ["source"] = $"\"[[{found.Name}]]\"" });

        Directory.CreateDirectory(Path.GetDirectoryName(notePath)!);
        await File.WriteAllTextAsync(notePath, frontmatter + "\n" + rendered, NoteHelpers.Utf8NoBom);

        var relPath = Path.GetRelativePath(config.VaultPath, notePath).Replace('\\', '/');
        return $"[ok] Generated {actualCount} flashcard(s), written to {relPath}";
    }

    // Flashcard helpers

    private async Task<(List<QaCardDto>? Qa, List<ClozeCardDto>? Cloze)> GenerateCardsWithRetryAsync(
        string prompt, string systemPrompt, bool isCloze)
    {
        var raw = await generation.GenerateAsync(prompt, systemPrompt);
        var (qa, cloze) = ParseCards(raw, isCloze);
        if (qa is not null || cloze is not null)
        {
            return (qa, cloze);
        }

        var retryPrompt = prompt +
            "\n\nIMPORTANT: your previous response was not a valid JSON array. " +
            "Return ONLY the JSON array — no prose, no markdown code fences, no trailing text.";
        var retryRaw = await generation.GenerateAsync(retryPrompt, systemPrompt);
        return ParseCards(retryRaw, isCloze);
    }

    private static (List<QaCardDto>? Qa, List<ClozeCardDto>? Cloze) ParseCards(string? raw, bool isCloze)
    {
        if (raw is null)
        {
            return (null, null);
        }

        return isCloze ? (null, TryParseClozeCards(raw)) : (TryParseQaCards(raw), null);
    }

    private static string QaSystemPrompt(int count) =>
        "You are a flashcard generator for spaced repetition study. Given a note's content, produce " +
        $"exactly {count} question/answer flashcards that test understanding of the key facts and " +
        "ideas in the note. Return ONLY a JSON array of objects with 'q' and 'a' string fields — no " +
        "prose, no markdown code fences, no explanation before or after. " +
        """Example: [{"q": "What is the capital of France?", "a": "Paris"}]""";

    private static string ClozeSystemPrompt(int count) =>
        "You are a cloze flashcard generator for spaced repetition study. Given a note's content, " +
        $"produce exactly {count} cloze-deletion flashcards. Each card is a single sentence based on " +
        "the note, with the key fact hidden using ==double equals== markers (Obsidian Spaced " +
        "Repetition plugin syntax). Return ONLY a JSON array of objects with a 'cloze' string field " +
        "— no prose, no markdown code fences, no explanation before or after. " +
        """Example: [{"cloze": "The capital of France is ==Paris==."}]""";

    private static List<QaCardDto>? TryParseQaCards(string raw)
    {
        try
        {
            var cards = JsonSerializer.Deserialize(ExtractJsonArray(raw), FlashcardJsonContext.Default.ListQaCardDto);
            if (cards is null || cards.Count == 0 || cards.Any(c => string.IsNullOrWhiteSpace(c.Q) || string.IsNullOrWhiteSpace(c.A)))
            {
                return null;
            }

            return cards;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<ClozeCardDto>? TryParseClozeCards(string raw)
    {
        try
        {
            var cards = JsonSerializer.Deserialize(ExtractJsonArray(raw), FlashcardJsonContext.Default.ListClozeCardDto);
            if (cards is null || cards.Count == 0 || cards.Any(c => string.IsNullOrWhiteSpace(c.Cloze)))
            {
                return null;
            }

            return cards;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractJsonArray(string raw)
    {
        var trimmed = raw.Trim();
        var start = trimmed.IndexOf('[');
        var end = trimmed.LastIndexOf(']');
        if (start < 0 || end < 0 || end <= start)
        {
            return trimmed;
        }

        return trimmed[start..(end + 1)];
    }

    private static string RenderSpacedRepetition(IReadOnlyList<QaCardDto> cards)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#flashcards");
        sb.AppendLine();
        foreach (var card in cards)
        {
            sb.AppendLine($"{card.Q!.Trim()}::{card.A!.Trim()}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static string RenderCloze(IReadOnlyList<ClozeCardDto> cards)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#flashcards");
        sb.AppendLine();
        foreach (var card in cards)
        {
            sb.AppendLine(card.Cloze!.Trim());
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static string RenderAnkiCsv(IReadOnlyList<QaCardDto> cards)
    {
        var sb = new StringBuilder();
        sb.AppendLine("front,back,tags");
        foreach (var card in cards)
        {
            sb.AppendLine($"{EscapeCsvField(card.Q!.Trim())},{EscapeCsvField(card.A!.Trim())},{EscapeCsvField("flashcards")}");
        }

        return sb.ToString();
    }

    private static string EscapeCsvField(string value)
    {
        var needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        return needsQuoting ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private string ResolveCsvPath(string outputNote, string sourceNoteName)
    {
        var relative = string.IsNullOrWhiteSpace(outputNote)
            ? $"{(vaultConfig.GetFolder("assets") ?? "Assets").TrimEnd('/')}/{sourceNoteName}-flashcards.csv"
            : (outputNote.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? outputNote : outputNote + ".csv");

        var combined = Path.Combine(config.VaultPath, relative.Replace('/', Path.DirectorySeparatorChar));
        return NoteHelpers.EnsureInsideVault(config.VaultPath, combined);
    }

    private string ResolveFlashcardNotePath(string outputNote, string sourceNoteName)
    {
        var relative = string.IsNullOrWhiteSpace(outputNote) ? $"Flashcards/{sourceNoteName}" : outputNote;
        return NoteHelpers.BuildFilePath(relative, config.VaultPath);
    }
}

// Flashcard JSON DTOs (AOT-safe)

internal sealed class QaCardDto
{
    [JsonPropertyName("q")]
    public string? Q { get; init; }

    [JsonPropertyName("a")]
    public string? A { get; init; }
}

internal sealed class ClozeCardDto
{
    [JsonPropertyName("cloze")]
    public string? Cloze { get; init; }
}

[JsonSerializable(typeof(List<QaCardDto>))]
[JsonSerializable(typeof(List<ClozeCardDto>))]
internal partial class FlashcardJsonContext : JsonSerializerContext
{
}
