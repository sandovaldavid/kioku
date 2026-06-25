using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for knowledge graph operations: timelines, concept maps, and vault snapshots.
/// All operations are read-only.
/// </summary>
[McpServerToolType]
public sealed class KnowledgeGraphTools(VaultIndexService vault)
{
    // -------------------------------------------------------------------------
    // get_knowledge_timeline
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Returns notes ordered chronologically by their frontmatter 'date' field. " +
        "Useful for reviewing the evolution of ideas over time. " +
        "Optionally filter by tag, folder, or date range. " +
        "Notes without a 'date' frontmatter field are excluded.")]
    public string get_knowledge_timeline(
        [Description("Filter by tag (without #). Leave empty for all notes.")] string tag = "",
        [Description("Folder path relative to vault root. Leave empty for all folders.")] string folder = "",
        [Description("Start date (inclusive) in YYYY-MM-DD format. Leave empty for no lower bound.")] string date_from = "",
        [Description("End date (inclusive) in YYYY-MM-DD format. Leave empty for no upper bound.")] string date_to = "",
        [Description("Maximum number of notes to return (default: 50, max: 200).")] int max_results = 50)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        max_results = Math.Clamp(max_results, 1, 200);

        // Parse date bounds
        DateTimeOffset? fromDate = null;
        DateTimeOffset? toDate = null;

        if (!string.IsNullOrWhiteSpace(date_from) &&
            DateTimeOffset.TryParse(date_from, out var parsedFrom))
        {
            fromDate = parsedFrom;
        }

        if (!string.IsNullOrWhiteSpace(date_to) &&
            DateTimeOffset.TryParse(date_to, out var parsedTo))
        {
            toDate = parsedTo.AddDays(1).AddTicks(-1); // inclusive end
        }

        var notes = vault.GetAllNotes().AsEnumerable();

        // Folder filter
        if (!string.IsNullOrWhiteSpace(folder))
        {
            var normalizedFolder = folder.Replace('\\', '/').TrimEnd('/');
            notes = notes.Where(n =>
                n.VaultRelativePath.StartsWith(normalizedFolder + "/", StringComparison.OrdinalIgnoreCase) ||
                n.VaultRelativePath.Equals(normalizedFolder, StringComparison.OrdinalIgnoreCase));
        }

        // Tag filter
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var tagLower = tag.TrimStart('#').ToLowerInvariant();
            notes = notes.Where(n =>
                n.Metadata.Tags.Any(t => t.TrimStart('#').Equals(tagLower, StringComparison.OrdinalIgnoreCase)));
        }

        // Only notes with a date
        var dated = notes
            .Where(n => n.Metadata.Date.HasValue)
            .Select(n => (note: n, date: n.Metadata.Date!.Value));

        // Date range filter — compare DateOnly
        if (fromDate.HasValue)
        {
            var fromDateOnly = DateOnly.FromDateTime(fromDate.Value.Date);
            dated = dated.Where(x => x.date >= fromDateOnly);
        }
        if (toDate.HasValue)
        {
            var toDateOnly = DateOnly.FromDateTime(toDate.Value.Date);
            dated = dated.Where(x => x.date <= toDateOnly);
        }

        var sorted = dated
            .OrderBy(x => x.date)
            .Take(max_results)
            .ToList();

        if (sorted.Count == 0)
        {
            return "[ok] No notes found matching the specified criteria.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[ok] Knowledge timeline — {sorted.Count} notes:");
        sb.AppendLine();

        string? lastYear = null;
        foreach (var (note, date) in sorted)
        {
            var year = date.Year.ToString();
            if (year != lastYear)
            {
                sb.AppendLine($"### {year}");
                lastYear = year;
            }

            var tagList = note.Metadata.Tags.Any()
                ? string.Join(", ", note.Metadata.Tags.Take(5))
                : "—";
            var status = note.Metadata.Status ?? "—";
            sb.AppendLine($"- **{date:yyyy-MM-dd}** [{note.Name}]({note.VaultRelativePath}) — {tagList} | status: {status}");
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // get_concept_map
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Returns a JSON graph centered on a specific note: nodes (notes) and edges (links). " +
        "Edges include outgoing wikilinks, backlinks, and (optionally) semantic similarity. " +
        "Use 'depth' to control traversal depth (1=direct links, 2=links of links). " +
        "Use 'max_nodes' to limit graph size. " +
        "The graph JSON can be visualized with tools like Obsidian Graph View, D3.js, or Cytoscape.")]
    public string get_concept_map(
        [Description("Name or path of the center note for the concept map.")] string note,
        [Description("Traversal depth: 1 = direct links only, 2 = links of links (default: 2, max: 3).")] int depth = 2,
        [Description("Maximum number of nodes to include (default: 50, max: 150).")] int max_nodes = 50)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        depth = Math.Clamp(depth, 1, 3);
        max_nodes = Math.Clamp(max_nodes, 5, 150);

        var center = ResolveNote(note);
        if (center is null)
        {
            return $"[error] Note not found: '{note}'. Use list_notes to see available notes.";
        }

        // BFS traversal
        var visited = new Dictionary<string, Note>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(Note node, int currentDepth)>();
        var edges = new List<GraphEdge>();

        queue.Enqueue((center, 0));
        visited[center.Name] = center;

        while (queue.Count > 0 && visited.Count < max_nodes)
        {
            var (current, currentDepth) = queue.Dequeue();

            // Outgoing links
            foreach (var target in current.OutgoingLinks)
            {
                var targetNote = vault.GetAllNotes()
                    .FirstOrDefault(n => n.Name.Equals(target, StringComparison.OrdinalIgnoreCase));

                if (targetNote is not null)
                {
                    edges.Add(new GraphEdge(current.Name, targetNote.Name, "link"));

                    if (!visited.ContainsKey(targetNote.Name) && currentDepth < depth && visited.Count < max_nodes)
                    {
                        visited[targetNote.Name] = targetNote;
                        queue.Enqueue((targetNote, currentDepth + 1));
                    }
                }
                else
                {
                    // Broken link — include as a stub node
                    if (!visited.ContainsKey(target) && visited.Count < max_nodes)
                    {
                        edges.Add(new GraphEdge(current.Name, target, "broken-link"));
                    }
                }
            }

            // Backlinks
            var backlinks = vault.GetBacklinks(current.Name);
            foreach (var backlinkNote in backlinks)
            {
                if (!edges.Any(e => e.Source == backlinkNote.Name && e.Target == current.Name && e.Type == "link"))
                {
                    edges.Add(new GraphEdge(backlinkNote.Name, current.Name, "backlink"));
                }

                if (!visited.ContainsKey(backlinkNote.Name) && currentDepth < depth && visited.Count < max_nodes)
                {
                    visited[backlinkNote.Name] = backlinkNote;
                    queue.Enqueue((backlinkNote, currentDepth + 1));
                }
            }
        }

        // Build graph JSON
        var nodes = visited.Values.Select(n => new GraphNode(
            Id: n.Name,
            Path: n.VaultRelativePath,
            Tags: n.Metadata.Tags.ToArray(),
            Status: n.Metadata.Status,
            Date: n.Metadata.Date?.ToString("yyyy-MM-dd"),
            IsCenter: n.Name.Equals(center.Name, StringComparison.OrdinalIgnoreCase)
        )).ToList();

        // Deduplicate edges
        var uniqueEdges = edges
            .GroupBy(e => $"{e.Source}→{e.Target}→{e.Type}")
            .Select(g => g.First())
            .ToList();

        var graph = new ConceptGraph(
            Center: center.Name,
            Depth: depth,
            NodeCount: nodes.Count,
            EdgeCount: uniqueEdges.Count,
            Nodes: nodes,
            Edges: uniqueEdges
        );

        var json = JsonSerializer.Serialize(graph, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

        return $"[ok] Concept map for '{center.Name}' (depth={depth}, {nodes.Count} nodes, {uniqueEdges.Count} edges):\n{json}";
    }

    // -------------------------------------------------------------------------
    // get_vault_snapshot
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Returns a comprehensive snapshot of the vault in a single call: " +
        "folder tree with note counts, top tags by frequency, frontmatter coverage stats, " +
        "and recent activity summary. " +
        "Replaces the need to call list_notes + get_vault_stats + multiple get_note_metadata.")]
    public string get_vault_snapshot()
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var allNotes = vault.GetAllNotes().ToList();

        if (allNotes.Count == 0)
        {
            return "[ok] The vault has no Markdown notes.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[ok] Vault snapshot — {allNotes.Count} notes");
        sb.AppendLine();

        // --- Folder tree ---
        sb.AppendLine("## Folder structure");
        var byFolder = allNotes
            .GroupBy(n =>
            {
                var parts = n.VaultRelativePath.Split('/');
                return parts.Length > 1 ? parts[0] : "(root)";
            })
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var folder in byFolder)
        {
            sb.AppendLine($"- **{folder.Key}/** — {folder.Count()} notes");

            // Subfolders (level 2)
            var subfolders = folder
                .Where(n => n.VaultRelativePath.Split('/').Length > 2)
                .GroupBy(n => string.Join("/", n.VaultRelativePath.Split('/').Take(2)))
                .OrderBy(g => g.Key)
                .ToList();

            foreach (var sub in subfolders.Take(10))
            {
                sb.AppendLine($"  - {sub.Key}/ ({sub.Count()})");
            }

            if (subfolders.Count > 10)
            {
                sb.AppendLine($"  - ... and {subfolders.Count - 10} more subfolders");
            }
        }

        sb.AppendLine();

        // --- Top tags ---
        sb.AppendLine("## Top 20 tags");
        var tagCounts = allNotes
            .SelectMany(n => n.Metadata.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(20)
            .ToList();

        if (tagCounts.Count == 0)
        {
            sb.AppendLine("No tags found in the vault.");
        }
        else
        {
            foreach (var tag in tagCounts)
            {
                sb.AppendLine($"- `{tag.Key}` ({tag.Count()})");
            }
        }

        sb.AppendLine();

        // --- Frontmatter coverage ---
        sb.AppendLine("## Frontmatter coverage");
        var withDate = allNotes.Count(n => n.Metadata.Date.HasValue);
        var withStatus = allNotes.Count(n => !string.IsNullOrWhiteSpace(n.Metadata.Status));
        var withType = allNotes.Count(n => !string.IsNullOrWhiteSpace(n.Metadata.NoteType));
        var withTags = allNotes.Count(n => n.Metadata.Tags.Any());
        var withAnyFrontmatter = allNotes.Count(n =>
            n.Metadata.Date.HasValue ||
            !string.IsNullOrWhiteSpace(n.Metadata.Status) ||
            !string.IsNullOrWhiteSpace(n.Metadata.NoteType) ||
            n.Metadata.Tags.Any());

        sb.AppendLine($"- Notes with frontmatter: {withAnyFrontmatter}/{allNotes.Count} ({Pct(withAnyFrontmatter, allNotes.Count)}%)");
        sb.AppendLine($"- With `date`: {withDate} ({Pct(withDate, allNotes.Count)}%)");
        sb.AppendLine($"- With `status`: {withStatus} ({Pct(withStatus, allNotes.Count)}%)");
        sb.AppendLine($"- With `type`: {withType} ({Pct(withType, allNotes.Count)}%)");
        sb.AppendLine($"- With `tags`: {withTags} ({Pct(withTags, allNotes.Count)}%)");

        // Status breakdown
        var statusGroups = allNotes
            .Where(n => !string.IsNullOrWhiteSpace(n.Metadata.Status))
            .GroupBy(n => n.Metadata.Status!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (statusGroups.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Status breakdown");
            foreach (var s in statusGroups)
            {
                sb.AppendLine($"- `{s.Key}`: {s.Count()}");
            }
        }

        sb.AppendLine();

        // --- Recent activity ---
        sb.AppendLine("## Recently modified (last 10)");
        var recent = allNotes
            .OrderByDescending(n => n.LastModified)
            .Take(10)
            .ToList();

        foreach (var n in recent)
        {
            sb.AppendLine($"- [{n.Name}]({n.VaultRelativePath}) — {n.LastModified:yyyy-MM-dd HH:mm}");
        }

        sb.AppendLine();

        // --- Link stats ---
        var totalOutgoing = allNotes.Sum(n => n.OutgoingLinks.Count);
        var notesWithLinks = allNotes.Count(n => n.OutgoingLinks.Count > 0);
        var orphans = allNotes.Count(n => !n.OutgoingLinks.Any() && !vault.GetBacklinks(n.Name).Any());

        sb.AppendLine("## Link statistics");
        sb.AppendLine($"- Total wikilinks: {totalOutgoing}");
        sb.AppendLine($"- Notes with outgoing links: {notesWithLinks} ({Pct(notesWithLinks, allNotes.Count)}%)");
        sb.AppendLine($"- Orphan notes (no links in or out): {orphans}");

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Note? ResolveNote(string input)
    {
        var all = vault.GetAllNotes();

        // Exact name match (without extension)
        var byName = all.FirstOrDefault(n =>
            n.Name.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        // Vault-relative path match
        var normalized = input.TrimStart('/').Replace('\\', '/');
        var byPath = all.FirstOrDefault(n =>
            n.VaultRelativePath.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            n.VaultRelativePath.Equals(normalized + ".md", StringComparison.OrdinalIgnoreCase));
        if (byPath is not null)
        {
            return byPath;
        }

        // Absolute path match
        var byAbsolute = all.FirstOrDefault(n =>
            n.FilePath.Equals(input, StringComparison.OrdinalIgnoreCase));
        return byAbsolute;
    }

    private static int Pct(int value, int total) =>
        total == 0 ? 0 : (int)Math.Round(value * 100.0 / total);

    // -------------------------------------------------------------------------
    // Graph DTOs
    // -------------------------------------------------------------------------

    private sealed record GraphNode(
        string Id,
        string Path,
        string[] Tags,
        string? Status,
        string? Date,
        bool IsCenter);

    private sealed record GraphEdge(string Source, string Target, string Type);

    private sealed record ConceptGraph(
        string Center,
        int Depth,
        int NodeCount,
        int EdgeCount,
        List<GraphNode> Nodes,
        List<GraphEdge> Edges);
}
