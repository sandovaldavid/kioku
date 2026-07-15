using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for knowledge graph operations: concept maps and vault snapshots.
/// All operations are read-only.
/// </summary>
[McpServerToolType]
public sealed class KnowledgeGraphTools(VaultIndexService vault)
{
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
            return KiokuError.NotFound($"Note not found or basename is ambiguous: '{note}'. Use list_notes to see available notes.");
        }

        // BFS traversal
        var visited = new Dictionary<string, Note>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(Note node, int currentDepth)>();
        var edges = new List<GraphEdge>();

        queue.Enqueue((center, 0));
        visited[center.FilePath] = center;

        while (queue.Count > 0 && visited.Count < max_nodes)
        {
            var (current, currentDepth) = queue.Dequeue();

            // Outgoing links
            foreach (var target in current.OutgoingLinks)
            {
                var targetNote = vault.ResolveLink(current, target);

                if (targetNote is not null)
                {
                    edges.Add(new GraphEdge(current.VaultRelativePath, targetNote.VaultRelativePath, "link"));

                    if (!visited.ContainsKey(targetNote.FilePath) && currentDepth < depth && visited.Count < max_nodes)
                    {
                        visited[targetNote.FilePath] = targetNote;
                        queue.Enqueue((targetNote, currentDepth + 1));
                    }
                }
                else
                {
                    // Broken link — include as a stub node
                    if (!visited.ContainsKey(target) && visited.Count < max_nodes)
                    {
                        edges.Add(new GraphEdge(current.VaultRelativePath, target, "broken-link"));
                    }
                }
            }

            // Backlinks
            var backlinks = vault.GetBacklinks(current);
            foreach (var backlinkNote in backlinks)
            {
                if (!edges.Any(e => e.Source == backlinkNote.VaultRelativePath &&
                                   e.Target == current.VaultRelativePath && e.Type == "link"))
                {
                    edges.Add(new GraphEdge(backlinkNote.VaultRelativePath, current.VaultRelativePath, "backlink"));
                }

                if (!visited.ContainsKey(backlinkNote.FilePath) && currentDepth < depth && visited.Count < max_nodes)
                {
                    visited[backlinkNote.FilePath] = backlinkNote;
                    queue.Enqueue((backlinkNote, currentDepth + 1));
                }
            }
        }

        // Build graph JSON
        var nodes = visited.Values.Select(n => new GraphNode(
            Id: n.VaultRelativePath,
            Path: n.VaultRelativePath,
            Tags: n.Metadata.Tags.ToArray(),
            Status: n.Metadata.Status,
            Date: n.Metadata.Date?.ToString("yyyy-MM-dd"),
            IsCenter: n.FilePath.Equals(center.FilePath, StringComparison.OrdinalIgnoreCase)
        )).ToList();

        // Deduplicate edges
        var uniqueEdges = edges
            .GroupBy(e => $"{e.Source}→{e.Target}→{e.Type}")
            .Select(g => g.First())
            .ToList();

        var graph = new ConceptGraph(
            Center: center.VaultRelativePath,
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
        "recent activity summary, graph density, unlinked notes, and graph islands. " +
        "Combines note listing, metadata coverage, and graph analysis in one report.")]
    public string get_vault_snapshot(
        [Description("Maximum connected-component size to report as a graph island (default: 3).")] int island_threshold = 3)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (island_threshold < 1)
        {
            return KiokuError.InvalidArgument("Island threshold must be at least 1.");
        }

        try
        {
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
        var orphans = allNotes.Count(n => !n.OutgoingLinks.Any() && !vault.GetBacklinks(n).Any());

        sb.AppendLine("## Link statistics");
        sb.AppendLine($"- Total wikilinks: {totalOutgoing}");
        sb.AppendLine($"- Notes with outgoing links: {notesWithLinks} ({Pct(notesWithLinks, allNotes.Count)}%)");
        sb.AppendLine($"- Orphan notes (no links in or out): {orphans}");

        // --- Consolidated graph analysis ---
        var backlinkCounts = allNotes
            .Select(n => (Note: n, Count: vault.GetBacklinks(n).Count))
            .ToList();
        var totalBacklinks = backlinkCounts.Sum(x => x.Count);
        var notesWithBacklinks = backlinkCounts.Count(x => x.Count > 0);
        var unlinkedNotes = backlinkCounts
            .Where(x => x.Count == 0 && x.Note.OutgoingLinks.Count == 0)
            .Select(x => x.Note)
            .OrderBy(n => n.Name)
            .ToList();
        var avgOutgoing = (double)totalOutgoing / allNotes.Count;
        var avgBacklinks = (double)totalBacklinks / allNotes.Count;

        sb.AppendLine();
        sb.AppendLine("## Graph density");
        sb.AppendLine("Vault Graph Density Metrics:");
        sb.AppendLine($"  Total notes: {allNotes.Count}");
        sb.AppendLine($"  Total outgoing links: {totalOutgoing}");
        sb.AppendLine($"  Total backlinks: {totalBacklinks}");
        sb.AppendLine($"  Average outgoing links/note: {avgOutgoing:F2}");
        sb.AppendLine($"  Average backlinks/note: {avgBacklinks:F2}");
        sb.AppendLine($"  Notes with outgoing links: {notesWithLinks} ({notesWithLinks * 100.0 / allNotes.Count:F1}%)");
        sb.AppendLine($"  Notes with backlinks: {notesWithBacklinks} ({notesWithBacklinks * 100.0 / allNotes.Count:F1}%)");
        sb.AppendLine($"  Unlinked notes (isolated): {unlinkedNotes.Count} ({unlinkedNotes.Count * 100.0 / allNotes.Count:F1}%)");

        sb.AppendLine();
        sb.AppendLine("## Unlinked notes");
        if (unlinkedNotes.Count == 0)
        {
            sb.AppendLine("[info] No unlinked notes found — all notes are part of the graph.");
        }
        else
        {
            sb.AppendLine($"Found {unlinkedNotes.Count} unlinked note(s):");
            foreach (var note in unlinkedNotes)
            {
                sb.AppendLine($"- {note.Name} (modified: {note.LastModified:yyyy-MM-dd})");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Graph islands");
        var islands = FindIslands(allNotes, island_threshold);
        if (islands.Count == 0)
        {
            sb.AppendLine($"[info] No graph islands found (all components > {island_threshold} notes).");
        }
        else
        {
            sb.AppendLine($"Found {islands.Count} island(s) (max {island_threshold} notes each):");
            foreach (var island in islands.OrderByDescending(i => i.Count))
            {
                var noteNames = string.Join(", ", island.Select(n => n.VaultRelativePath).OrderBy(path => path));
                sb.AppendLine($"- Island ({island.Count} notes): {noteNames}");
            }
        }

            return sb.ToString();
        }
        catch (Exception)
        {
            return KiokuError.Internal("Could not build the vault snapshot.");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Note? ResolveNote(string input) => vault.ResolveNote(input);

    private static int Pct(int value, int total) =>
        total == 0 ? 0 : (int)Math.Round(value * 100.0 / total);

    private List<List<Note>> FindIslands(IReadOnlyList<Note> allNotes, int threshold)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var islands = new List<List<Note>>();

        foreach (var note in allNotes)
        {
            if (visited.Contains(note.FilePath))
            {
                continue;
            }

            var component = new List<Note>();
            var queue = new Queue<Note>();
            queue.Enqueue(note);
            visited.Add(note.FilePath);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                foreach (var link in current.OutgoingLinks)
                {
                    var linkedNote = vault.ResolveLink(current, link);
                    if (linkedNote is not null && visited.Add(linkedNote.FilePath))
                    {
                        queue.Enqueue(linkedNote);
                    }
                }

                foreach (var backlink in vault.GetBacklinks(current))
                {
                    if (visited.Add(backlink.FilePath))
                    {
                        queue.Enqueue(backlink);
                    }
                }
            }

            if (component.Count <= threshold)
            {
                islands.Add(component);
            }
        }

        return islands;
    }

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
