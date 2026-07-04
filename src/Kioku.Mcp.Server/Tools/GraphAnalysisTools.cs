using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
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

    [McpServerTool, Description(
        "Finds all notes with no outgoing links and no backlinks (completely isolated from the graph).")]
    public string find_unlinked_notes()
    {
        var unlinked = FindOrphanNotes().OrderBy(n => n.Name).ToList();

        if (unlinked.Count == 0)
        {
            return "[info] No unlinked notes found — all notes are part of the graph.";
        }

        var lines = new List<string>
        {
            $"Found {unlinked.Count} unlinked note(s):",
        };

        foreach (var note in unlinked)
        {
            lines.Add($"- {note.Name} (modified: {note.LastModified:yyyy-MM-dd})");
        }

        return string.Join("\n", lines);
    }

    [McpServerTool, Description(
        "Finds connected components in the graph smaller than a threshold (graph islands). Small isolated clusters often indicate notes that should be linked to the main graph.")]
    public string find_graph_islands(
        [Description("Maximum size of a connected component to be considered an island (default: 3).")] int threshold = 3)
    {
        if (threshold < 1)
        {
            return "[error] Threshold must be at least 1.";
        }

        var allNotes = vault.GetAllNotes().ToList();
        if (allNotes.Count == 0)
        {
            return "[info] Vault is empty.";
        }

        var islands = FindIslands(threshold);

        if (islands.Count == 0)
        {
            return $"[info] No graph islands found (all components > {threshold} notes).";
        }

        var lines = new List<string>
        {
            $"Found {islands.Count} island(s) (max {threshold} notes each):",
        };

        foreach (var island in islands.OrderByDescending(i => i.Count))
        {
            var noteNames = string.Join(", ", island.Select(n => n.Name).OrderBy(x => x));
            lines.Add($"- Island ({island.Count} notes): {noteNames}");
        }

        return string.Join("\n", lines);
    }

    [McpServerTool, Description(
        "Computes vault graph density metrics: average links per note, percentage of notes with backlinks, connectivity profile.")]
    public string measure_vault_density()
    {
        var allNotes = vault.GetAllNotes().ToList();

        if (allNotes.Count == 0)
        {
            return "[info] Vault is empty.";
        }

        var totalOutgoing = 0;
        var totalBacklinks = 0;
        var notesWithOutgoing = 0;
        var notesWithBacklinks = 0;
        var unlinked = 0;

        foreach (var note in allNotes)
        {
            var outgoing = note.OutgoingLinks.Count;
            var backlinks = vault.GetBacklinks(note.Name).Count();

            totalOutgoing += outgoing;
            totalBacklinks += backlinks;

            if (outgoing > 0)
            {
                notesWithOutgoing++;
            }

            if (backlinks > 0)
            {
                notesWithBacklinks++;
            }

            if (outgoing == 0 && backlinks == 0)
            {
                unlinked++;
            }
        }

        var avgOutgoing = allNotes.Count > 0 ? (double)totalOutgoing / allNotes.Count : 0;
        var avgBacklinks = allNotes.Count > 0 ? (double)totalBacklinks / allNotes.Count : 0;
        var percentageWithOutgoing = (double)notesWithOutgoing / allNotes.Count * 100;
        var percentageWithBacklinks = (double)notesWithBacklinks / allNotes.Count * 100;
        var percentageUnlinked = (double)unlinked / allNotes.Count * 100;

        var lines = new List<string>
        {
            "Vault Graph Density Metrics:",
            $"  Total notes: {allNotes.Count}",
            $"  Total outgoing links: {totalOutgoing}",
            $"  Total backlinks: {totalBacklinks}",
            $"  Average outgoing links/note: {avgOutgoing:F2}",
            $"  Average backlinks/note: {avgBacklinks:F2}",
            $"  Notes with outgoing links: {notesWithOutgoing} ({percentageWithOutgoing:F1}%)",
            $"  Notes with backlinks: {notesWithBacklinks} ({percentageWithBacklinks:F1}%)",
            $"  Unlinked notes (isolated): {unlinked} ({percentageUnlinked:F1}%)",
        };

        return string.Join("\n", lines);
    }

    // suggest_links

    [McpServerTool, Description(
        "Suggests wikilinks that don't exist yet. With 'note': semantic candidates for that note, " +
        "excluding any pair already linked in either direction. Without 'note' (vault-wide mode): " +
        "prioritizes orphaned notes and small graph islands, proposing a bridge for each. Requires " +
        "Ollama for semantic scoring — per-note mode fails without it; vault-wide mode degrades to " +
        "a structural report of orphans/islands with no specific targets.")]
    public string suggest_links(
        [Description("Name or path of a note to suggest links for. Leave empty for vault-wide mode (orphans + islands).")] string note = "",
        [Description("Maximum number of suggestions to return.")] int max_suggestions = 10,
        [Description("Minimum similarity score 0.0–1.0 (default: 0.7).")] float min_similarity = 0.7f)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
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
            return FormatSuggestions(suggestions, $"'{found.Name}'");
        }

        if (!embedding.IsAvailable)
        {
            return FormatStructuralFallback();
        }

        var vaultSuggestions = SuggestLinksForVault(max_suggestions, min_similarity);
        return FormatSuggestions(vaultSuggestions, "the vault");
    }

    // apply_link_suggestions

    [McpServerTool, Description(
        "Applies accepted link suggestions: appends (or extends) a section at the end of a note " +
        "with wikilinks to the given targets. Idempotent — targets already linked from the note " +
        "are skipped, so running it again with the same targets adds nothing new. " +
        "Set dry_run=true to preview without writing.")]
    public async Task<string> apply_link_suggestions(
        [Description("Name or path of the note to add links to.")] string note,
        [Description("Comma-separated list of target note names/paths to link.")] string targets,
        [Description("Heading for the links section (default: 'Related').")] string section = "Related",
        [Description("If true, previews the change without writing any file.")] bool dry_run = false)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
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
        foreach (var t in targetNames)
        {
            var resolved = NoteHelpers.ResolveNote(t, vault);
            if (resolved is null)
            {
                missing.Add(t);
            }
            else if (!resolved.FilePath.Equals(found.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                resolvedTargets.Add(resolved);
            }
        }

        if (resolvedTargets.Count == 0)
        {
            return missing.Count > 0
                ? KiokuError.NotFound($"None of the targets could be resolved: {string.Join(", ", missing)}")
                : "[info] No valid targets to link (cannot link a note to itself).";
        }

        var currentContent = await File.ReadAllTextAsync(found.FilePath, Encoding.UTF8);
        var existingLinks = found.OutgoingLinks.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var updatedContent = NoteHelpers.AppendLinkSection(
            currentContent, existingLinks, section,
            resolvedTargets.Select(t => (t.Name, (string?)null)));

        var newTargets = resolvedTargets.Where(t => !existingLinks.Contains(t.Name)).ToList();

        if (updatedContent is null)
        {
            return $"[info] All {resolvedTargets.Count} target(s) are already linked from '{found.Name}'.";
        }

        if (dry_run)
        {
            var preview = $"[info] dry_run=true — would add {newTargets.Count} link(s) to '{found.Name}' under '## {section}':\n" +
                          string.Join("\n", newTargets.Select(t => $"  - [[{t.Name}]]"));
            if (missing.Count > 0)
            {
                preview += $"\n\n[info] Could not resolve: {string.Join(", ", missing)}";
            }

            return preview;
        }

        await File.WriteAllTextAsync(found.FilePath, updatedContent, Encoding.UTF8);
        await vault.SynchronizeFileReindexAsync(found.FilePath);

        var result = $"[ok] Added {newTargets.Count} link(s) to '{found.Name}' under '## {section}':\n" +
                     string.Join("\n", newTargets.Select(t => $"  - [[{t.Name}]]"));
        if (missing.Count > 0)
        {
            result += $"\n\n[info] Could not resolve: {string.Join(", ", missing)}";
        }

        return result;
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

    private string FormatStructuralFallback()
    {
        var sb = new StringBuilder(
            "[info] Semantic link suggestions require Ollama — showing structural analysis instead:\n\n");
        sb.AppendLine(find_unlinked_notes());
        sb.AppendLine();
        sb.AppendLine(find_graph_islands(IslandThreshold));
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
