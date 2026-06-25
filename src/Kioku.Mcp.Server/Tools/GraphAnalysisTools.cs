using System.ComponentModel;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for analyzing the vault's knowledge graph structure.
/// </summary>
[McpServerToolType]
public sealed class GraphAnalysisTools(VaultIndexService vault)
{
    [McpServerTool, Description(
        "Finds all notes with no outgoing links and no backlinks (completely isolated from the graph).")]
    public string find_unlinked_notes()
    {
        var unlinked = vault.GetAllNotes()
            .Where(n => !n.OutgoingLinks.Any() && !vault.GetBacklinks(n.Name).Any())
            .OrderBy(n => n.Name)
            .ToList();

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

        var visited = new HashSet<string>();
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
