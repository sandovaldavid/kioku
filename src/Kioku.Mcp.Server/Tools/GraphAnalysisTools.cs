using System.ComponentModel;
using System.Text;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for analyzing the vault's knowledge graph structure.
/// </summary>
[McpServerToolType]
public sealed class GraphAnalysisTools(
    VaultIndexService vault,
    HybridSearchService hybrid,
    EmbeddingService embedding,
    KiokuConfiguration config)
{
    private const int IslandThreshold = 3;

    // suggest_links

    [McpServerTool, Description(
        "Suggests or adds wikilinks that don't exist yet. Provide 'targets' to explicitly choose " +
        "targets; otherwise semantic candidates are generated for 'note', or for the whole vault " +
        "when 'note' is empty. Suggestions are a dry run by default. Set apply=true to apply " +
        "them. Explicit targets work without Ollama; semantic mode falls back to structural " +
        "orphan/island analysis when Ollama is unavailable.")]
    public async Task<string> suggest_links(
        [Description("Name or path of a note to suggest or add links for. Leave empty for vault-wide mode.")] string note = "",
        [Description("Comma-separated target note names/paths. When provided, these explicit targets take precedence over semantic suggestions.")] string targets = "",
        [Description("Heading for the links section (default: 'Related').")] string section = "Related",
        [Description("If true, apply the suggestions. The default false only previews them.")] bool apply = false,
        [Description("Maximum number of semantic suggestions to return or apply (default: 10).")]
        int max_suggestions = 10,
        [Description("Minimum semantic similarity score 0.0–1.0 (default: 0.7).")]
        float min_similarity = 0.7f)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (!string.IsNullOrWhiteSpace(targets))
        {
            return await ApplyExplicitTargets(note, targets, section, !apply);
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            var found = NoteHelpers.ResolveNote(note, vault);
            if (found is null)
            {
                return KiokuError.NotFound($"Note not found: '{note}'");
            }

            if (!embedding.IsAvailable)
            {
                return KiokuError.DependencyUnavailable(
                    $"Semantic link suggestions require Ollama running at {config.OllamaUrl} with embeddings available.");
            }

            var suggestions = SuggestLinksForNote(found, max_suggestions, min_similarity);
            return !apply
                ? FormatSuggestions(suggestions, $"'{found.Name}'")
                : await ApplySemanticSuggestions(suggestions, section);
        }

        if (!embedding.IsAvailable)
        {
            return FormatStructuralFallback();
        }

        var vaultSuggestions = SuggestLinksForVault(max_suggestions, min_similarity);
        return !apply
            ? FormatSuggestions(vaultSuggestions, "the vault")
            : await ApplySemanticSuggestions(vaultSuggestions, section);
    }

    // Private helpers — link suggestions

    private sealed record LinkSuggestion(Note Source, Note Target, float Score, string Reason, string? Snippet);

    private List<LinkSuggestion> SuggestLinksForNote(Note source, int maxSuggestions, float minSimilarity)
    {
        var outgoingNames = source.OutgoingLinks.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var backlinkNames = vault.GetBacklinks(source.Name).Select(n => n.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return hybrid.FindSimilar(source, maxSuggestions + 10, minSimilarity)
            .Where(r => !outgoingNames.Contains(r.Note.Name) && !backlinkNames.Contains(r.Note.Name))
            .Take(maxSuggestions)
            .Select(r => new LinkSuggestion(source, r.Note, r.Score, "semantic-similarity", BuildSnippet(r.Note)))
            .ToList();
    }

    private List<LinkSuggestion> SuggestLinksForVault(int maxSuggestions, float minSimilarity)
    {
        var suggestions = new List<LinkSuggestion>();

        foreach (var orphan in FindOrphanNotes())
        {
            var best = hybrid.FindSimilar(orphan, 1, minSimilarity).FirstOrDefault();
            if (best is not null)
            {
                suggestions.Add(new LinkSuggestion(orphan, best.Note, best.Score, "orphan-rescue", BuildSnippet(best.Note)));
            }
        }

        // Islands of size 1 are already covered by FindOrphanNotes above — only bridge
        // genuinely connected-but-small clusters here to avoid duplicate suggestions.
        foreach (var island in FindIslands(IslandThreshold).Where(i => i.Count > 1))
        {
            var islandNames = island.Select(n => n.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var representative = island[0];
            var repOutgoing = representative.OutgoingLinks.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var repBacklinks = vault.GetBacklinks(representative.Name).Select(n => n.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var best = hybrid.FindSimilar(representative, island.Count + 10, minSimilarity)
                .FirstOrDefault(r => !islandNames.Contains(r.Note.Name)
                                   && !repOutgoing.Contains(r.Note.Name)
                                   && !repBacklinks.Contains(r.Note.Name));

            if (best is not null)
            {
                suggestions.Add(new LinkSuggestion(representative, best.Note, best.Score, "island-bridge", BuildSnippet(best.Note)));
            }
        }

        return suggestions
            .OrderByDescending(s => s.Score)
            .Take(maxSuggestions)
            .ToList();
    }

    private static string FormatSuggestions(IReadOnlyList<LinkSuggestion> suggestions, string scopeDescription)
    {
        if (suggestions.Count == 0)
        {
            return $"[info] No link suggestions found for {scopeDescription}.";
        }

        var sb = new StringBuilder($"[ok] {suggestions.Count} link suggestion(s) for {scopeDescription}:\n\n");
        var i = 1;
        foreach (var s in suggestions)
        {
            sb.AppendLine($"{i}. [[{s.Source.Name}]] → [[{s.Target.Name}]]  (score: {s.Score:P0}, {s.Reason})");
            if (!string.IsNullOrWhiteSpace(s.Snippet))
            {
                sb.AppendLine($"   \"{s.Snippet}\"");
            }

            i++;
        }

        return sb.ToString();
    }

    private async Task<string> ApplyExplicitTargets(
        string note,
        string targets,
        string section,
        bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return KiokuError.InvalidArgument("The 'note' parameter is required when 'targets' is provided.");
        }

        var found = NoteHelpers.ResolveNote(note, vault);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        var targetNames = targets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (targetNames.Length == 0)
        {
            return KiokuError.InvalidArgument("The 'targets' parameter cannot be empty.");
        }

        var resolvedTargets = new List<Note>();
        var missing = new List<string>();
        foreach (var targetName in targetNames)
        {
            var resolved = NoteHelpers.ResolveNote(targetName, vault);
            if (resolved is null)
            {
                missing.Add(targetName);
            }
            else if (!resolved.FilePath.Equals(found.FilePath, StringComparison.OrdinalIgnoreCase) &&
                     resolvedTargets.All(t => !t.FilePath.Equals(resolved.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                resolvedTargets.Add(resolved);
            }
        }

        if (resolvedTargets.Count == 0)
        {
            var result = missing.Count > 0
                ? KiokuError.NotFound($"None of the targets could be resolved: {string.Join(", ", missing)}")
                : "[info] No valid targets to link (cannot link a note to itself).";
            return result;
        }

        var currentContent = await File.ReadAllTextAsync(found.FilePath, Encoding.UTF8);
        var existingLinks = found.OutgoingLinks.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newTargets = resolvedTargets.Where(t => !existingLinks.Contains(t.Name)).ToList();
        var updatedContent = NoteHelpers.AppendLinkSection(
            currentContent,
            existingLinks,
            section,
            newTargets.Select(t => (t.Name, (string?)null)));

        if (updatedContent is null)
        {
            var result = $"[info] All {resolvedTargets.Count} target(s) are already linked from '{found.Name}'.";
            return AppendMissingTargets(result, missing);
        }

        if (dryRun)
        {
            var preview = $"[info] dry_run=true — would add {newTargets.Count} link(s) to '{found.Name}' under '## {section}':\n" +
                          string.Join("\n", newTargets.Select(t => $"  - [[{t.Name}]]"));
            return AppendMissingTargets(preview, missing);
        }

        await File.WriteAllTextAsync(found.FilePath, updatedContent, NoteHelpers.Utf8NoBom);
        await vault.SynchronizeFileReindexAsync(found.FilePath);

        var applied = $"[ok] Added {newTargets.Count} link(s) to '{found.Name}' under '## {section}':\n" +
                      string.Join("\n", newTargets.Select(t => $"  - [[{t.Name}]]"));
        return AppendMissingTargets(applied, missing);
    }

    private async Task<string> ApplySemanticSuggestions(IReadOnlyList<LinkSuggestion> suggestions, string section)
    {
        if (suggestions.Count == 0)
        {
            return "[info] No link suggestions to apply.";
        }

        var applied = new List<(Note Source, List<LinkSuggestion> Suggestions)>();
        foreach (var sourceGroup in suggestions.GroupBy(s => s.Source.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var source = sourceGroup.First().Source;
            var currentContent = await File.ReadAllTextAsync(source.FilePath, Encoding.UTF8);
            var existingLinks = source.OutgoingLinks.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newSuggestions = sourceGroup
                .Where(s => !existingLinks.Contains(s.Target.Name))
                .ToList();
            if (newSuggestions.Count == 0)
            {
                continue;
            }

            var updatedContent = NoteHelpers.AppendLinkSection(
                currentContent,
                existingLinks,
                section,
                newSuggestions.Select(s => (s.Target.Name, (string?)$"{s.Score:P0} similar")));
            if (updatedContent is null)
            {
                continue;
            }

            await File.WriteAllTextAsync(source.FilePath, updatedContent, NoteHelpers.Utf8NoBom);
            await vault.SynchronizeFileReindexAsync(source.FilePath);
            applied.Add((source, newSuggestions));
        }

        if (applied.Count == 0)
        {
            return "[info] All suggested targets are already linked.";
        }

        var sb = new StringBuilder();
        foreach (var (source, sourceSuggestions) in applied)
        {
            sb.AppendLine($"[ok] Added {sourceSuggestions.Count} related link(s) to '{source.Name}':");
            foreach (var suggestion in sourceSuggestions)
            {
                sb.AppendLine($"  - [[{suggestion.Target.Name}]] ({suggestion.Score:P0})");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string AppendMissingTargets(string result, IReadOnlyList<string> missing) =>
        missing.Count == 0
            ? result
            : $"{result}\n\n[info] Could not resolve: {string.Join(", ", missing)}";

    private string FormatStructuralFallback()
    {
        var sb = new StringBuilder(
            "[info] Semantic link suggestions require Ollama — showing structural analysis instead:\n\n");
        var unlinked = FindOrphanNotes().OrderBy(n => n.Name).ToList();
        if (unlinked.Count == 0)
        {
            sb.AppendLine("[info] No unlinked notes found — all notes are part of the graph.");
        }
        else
        {
            sb.AppendLine($"Found {unlinked.Count} unlinked note(s):");
            foreach (var note in unlinked)
            {
                sb.AppendLine($"- {note.Name} (modified: {note.LastModified:yyyy-MM-dd})");
            }
        }

        sb.AppendLine();
        var islands = FindIslands(IslandThreshold);
        if (islands.Count == 0)
        {
            sb.AppendLine($"[info] No graph islands found (all components > {IslandThreshold} notes).");
        }
        else
        {
            sb.AppendLine($"Found {islands.Count} island(s) (max {IslandThreshold} notes each):");
            foreach (var island in islands.OrderByDescending(i => i.Count))
            {
                var noteNames = string.Join(", ", island.Select(n => n.Name).OrderBy(x => x));
                sb.AppendLine($"- Island ({island.Count} notes): {noteNames}");
            }
        }

        return sb.ToString();
    }

    private static string BuildSnippet(Note note)
    {
        const int maxLength = 100;
        var text = note.PlainText.Trim();
        return text.Length > maxLength ? text[..maxLength] + "..." : text;
    }

    private List<Note> FindOrphanNotes() =>
        vault.GetAllNotes()
            .Where(n => !n.OutgoingLinks.Any() && !vault.GetBacklinks(n.Name).Any())
            .ToList();

    private List<List<Note>> FindIslands(int threshold)
    {
        var allNotes = vault.GetAllNotes().ToList();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var islands = new List<List<Note>>();

        foreach (var note in allNotes)
        {
            if (visited.Contains(note.Name))
            {
                continue;
            }

            var component = BfsComponent(note, visited, allNotes);
            if (component.Count <= threshold)
            {
                islands.Add(component);
            }
        }

        return islands;
    }

    // BFS to find connected component of a note
    private List<Note> BfsComponent(Note startNote, HashSet<string> visited, List<Note> allNotes)
    {
        var component = new List<Note>();
        var queue = new Queue<Note>();
        var notesByName = allNotes.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);

        queue.Enqueue(startNote);
        visited.Add(startNote.Name);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            component.Add(current);

            foreach (var link in current.OutgoingLinks)
            {
                if (!visited.Contains(link))
                {
                    visited.Add(link);
                    if (notesByName.TryGetValue(link, out var linkedNote))
                    {
                        queue.Enqueue(linkedNote);
                    }
                }
            }

            var backlinks = vault.GetBacklinks(current.Name);
            foreach (var backlink in backlinks)
            {
                if (!visited.Contains(backlink.Name))
                {
                    visited.Add(backlink.Name);
                    queue.Enqueue(backlink);
                }
            }
        }

        return component;
    }
}
