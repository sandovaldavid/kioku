using System.ComponentModel;
using System.Text;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for work session management and vault context awareness.
/// Helps AI agents resume context between conversations and track activity.
/// </summary>
[McpServerToolType]
public sealed class SessionContextTools(VaultIndexService vault, KiokuConfiguration config)
{
    private static readonly string[] SessionsFolderCandidates = ["Sessions", "Journal", "Daily", "99_System/Sessions"];

    // get_recent_activity

    [McpServerTool, Description(
        "Returns the N most recently modified notes in the vault, ordered by last modification time. " +
        "Useful for the agent to quickly understand what the user has been working on.")]
    public Task<string> get_recent_activity(
        [Description("Maximum number of notes to return.")] int n = 10,
        [Description("Scope to a subfolder (relative to vault root). Leave empty for the full vault.")] string folder = "")
    {
        var notes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes()
            : vault.GetNotesInFolder(folder);

        var recent = notes
            .OrderByDescending(note => note.LastModified)
            .Take(n)
            .ToList();

        if (recent.Count == 0)
        {
            return Task.FromResult("[info] No notes found.");
        }

        var sb = new StringBuilder($"[ok] {recent.Count} most recently modified notes");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            sb.Append($" in '{folder}'");
        }

        sb.AppendLine(":\n");

        foreach (var note in recent)
        {
            var age = FormatAge(note.LastModified);
            var tagsSummary = note.Metadata.Tags.Count > 0
                ? $" [{string.Join(", ", note.Metadata.Tags.Take(3).Select(t => "#" + t))}]"
                : string.Empty;
            sb.AppendLine($"  {note.VaultRelativePath}{tagsSummary}");
            sb.AppendLine($"    Last modified: {note.LastModified:yyyy-MM-dd HH:mm} UTC ({age})");
        }

        return Task.FromResult(sb.ToString());
    }

    // get_work_context

    [McpServerTool, Description(
        "Returns a snapshot of the vault's current work state: notes in inbox folders, " +
        "notes with status 'draft', and the most recently modified notes. " +
        "Call this at the start of a session to quickly understand where to resume work.")]
    public Task<string> get_work_context(
        [Description("Folder treated as the inbox (relative to vault root). Default: 'Inbox'.")] string inbox_folder = "Inbox",
        [Description("Maximum number of recent notes to show in each section.")] int max_per_section = 5)
    {
        var sb = new StringBuilder("# Work Context Snapshot\n\n");
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC\n");

        // Section 1: Inbox notes
        var inboxNotes = vault.GetNotesInFolder(inbox_folder)
            .OrderByDescending(n => n.LastModified)
            .Take(max_per_section)
            .ToList();

        sb.AppendLine($"## Inbox ({inbox_folder}) — {inboxNotes.Count} note(s)");
        if (inboxNotes.Count == 0)
        {
            sb.AppendLine("_(empty — inbox is clear)_");
        }
        else
        {
            foreach (var n in inboxNotes)
            {
                sb.AppendLine($"- [[{n.Name}]] _(modified {FormatAge(n.LastModified)} ago)_");
            }
        }

        sb.AppendLine();

        // Section 2: Draft notes
        var drafts = vault.FilterByMetadata(status: "draft")
            .OrderByDescending(n => n.LastModified)
            .Take(max_per_section)
            .ToList();

        sb.AppendLine($"## In Progress — Drafts ({drafts.Count} note(s))");
        if (drafts.Count == 0)
        {
            sb.AppendLine("_(no draft notes found)_");
        }
        else
        {
            foreach (var n in drafts)
            {
                var tagSummary = n.Metadata.Tags.Count > 0
                    ? $" [{string.Join(", ", n.Metadata.Tags.Take(2).Select(t => "#" + t))}]"
                    : string.Empty;
                sb.AppendLine($"- [[{n.Name}]]{tagSummary} _(modified {FormatAge(n.LastModified)} ago)_");
            }
        }

        sb.AppendLine();

        // Section 3: Recent activity
        var recent = vault.GetAllNotes()
            .OrderByDescending(n => n.LastModified)
            .Take(max_per_section)
            .ToList();

        sb.AppendLine($"## Recently Modified ({recent.Count} note(s))");
        foreach (var n in recent)
        {
            sb.AppendLine($"- [[{n.Name}]] _(modified {FormatAge(n.LastModified)} ago)_");
        }

        sb.AppendLine();

        // Section 4: Active session note (if any)
        var activeSession = FindActiveSessionNote();
        sb.AppendLine("## Active Session");
        if (activeSession is not null)
        {
            sb.AppendLine($"- [[{activeSession.Name}]] _(started {FormatAge(activeSession.LastModified)} ago)_");
        }
        else
        {
            sb.AppendLine("_(no active session — use `start_work_session` to begin one)_");
        }

        return Task.FromResult(sb.ToString());
    }

    // start_work_session

    [McpServerTool, Description(
        "Creates a new work session note with a timestamp header. " +
        "Records the current date, time, and optional session goal. " +
        "The note is saved in the sessions folder for later reference.")]
    public async Task<string> start_work_session(
        [Description("Optional name for the session (e.g. 'Thesis Chapter 3 Review'). Defaults to today's date.")] string session_name = "",
        [Description("Folder where session notes are stored (relative to vault root).")] string sessions_folder = "Sessions",
        [Description("Optional goal or focus for this session.")] string goal = "")
    {
        var now = DateTime.Now;
        var dateStr = now.ToString("yyyy-MM-dd");
        var timeStr = now.ToString("HH:mm");
        var noteName = string.IsNullOrWhiteSpace(session_name)
            ? dateStr
            : $"{dateStr} — {session_name}";

        var destFolder = Path.Combine(config.VaultPath, sessions_folder);
        Directory.CreateDirectory(destFolder);

        var filePath = Path.Combine(destFolder, $"{noteName}.md");
        if (File.Exists(filePath))
        {
            // Append to existing session note instead of overwriting
            var appendContent = $"\n\n---\n\n## Session resumed at {timeStr}\n";
            await File.AppendAllTextAsync(filePath, appendContent, Encoding.UTF8);
            return $"[ok] Resumed existing session note: {sessions_folder}/{noteName}.md";
        }

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("tags:");
        sb.AppendLine("  - session");
        sb.AppendLine("  - work-log");
        sb.AppendLine($"type: session");
        sb.AppendLine($"status: active");
        sb.AppendLine($"date: {dateStr}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# Work Session — {noteName}");
        sb.AppendLine();
        sb.AppendLine($"**Started:** {timeStr}");
        sb.AppendLine($"**Date:** {dateStr}");

        if (!string.IsNullOrWhiteSpace(goal))
        {
            sb.AppendLine($"**Goal:** {goal}");
        }

        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine("_(session notes go here)_");
        sb.AppendLine();
        sb.AppendLine("## Modified during this session");
        sb.AppendLine();

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);

        var relativePath = Path.GetRelativePath(config.VaultPath, filePath);
        return $"[ok] Work session started: {relativePath}";
    }

    // end_work_session

    [McpServerTool, Description(
        "Closes the current work session by appending a summary of notes modified since the session started. " +
        "Updates the session note status to 'done'.")]
    public async Task<string> end_work_session(
        [Description("Name or path of the session note to close. If empty, finds the most recent active session.")] string session_note = "",
        [Description("Optional summary or outcome of the session.")] string summary = "")
    {
        Note? sessionNote;

        if (!string.IsNullOrWhiteSpace(session_note))
        {
            sessionNote = vault.GetNote(session_note) ?? vault.GetNoteByName(session_note);
            if (sessionNote is null)
            {
                return $"[error] Session note not found: '{session_note}'";
            }
        }
        else
        {
            sessionNote = FindActiveSessionNote();
            if (sessionNote is null)
            {
                return "[error] No active session found. Use start_work_session to begin one, " +
                       "or specify the session note name explicitly.";
            }
        }

        var sessionStart = sessionNote.LastModified;
        var now = DateTime.UtcNow;

        // Find notes modified since the session started
        var modifiedNotes = vault.GetAllNotes()
            .Where(n => n.LastModified > sessionStart && !n.FilePath.Equals(sessionNote.FilePath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.LastModified)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"## Session ended — {now:HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine($"**Duration:** {FormatDuration(now - sessionStart.UtcDateTime)}");

        if (!string.IsNullOrWhiteSpace(summary))
        {
            sb.AppendLine($"**Outcome:** {summary}");
        }

        if (modifiedNotes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"### Notes touched during session ({modifiedNotes.Count})");
            foreach (var n in modifiedNotes)
            {
                sb.AppendLine($"- [[{n.Name}]]");
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("_(no notes were modified during this session)_");
        }

        await File.AppendAllTextAsync(sessionNote.FilePath, sb.ToString(), Encoding.UTF8);

        // Update status to done
        var rawContent = await File.ReadAllTextAsync(sessionNote.FilePath, Encoding.UTF8);
        var updatedContent = rawContent.Replace("status: active", "status: done");
        await File.WriteAllTextAsync(sessionNote.FilePath, updatedContent, Encoding.UTF8);

        return $"[ok] Session closed: {sessionNote.VaultRelativePath}\n" +
               $"   Duration: {FormatDuration(now - sessionStart.UtcDateTime)}\n" +
               $"   Notes touched: {modifiedNotes.Count}";
    }

    // Private helpers

    private Note? FindActiveSessionNote()
    {
        return SessionsFolderCandidates
            .SelectMany(folder => vault.GetNotesInFolder(folder))
            .Where(n => string.Equals(n.Metadata.Status, "active", StringComparison.OrdinalIgnoreCase) ||
                        n.Metadata.Tags.Any(t => t.Equals("session", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(n => n.LastModified)
            .FirstOrDefault();
    }

    private static string FormatAge(DateTimeOffset lastModified)
    {
        var age = DateTimeOffset.UtcNow - lastModified;

        if (age.TotalMinutes < 60)
        {
            return $"{(int)age.TotalMinutes}m";
        }

        if (age.TotalHours < 24)
        {
            return $"{(int)age.TotalHours}h";
        }

        if (age.TotalDays < 7)
        {
            return $"{(int)age.TotalDays}d";
        }

        return $"{(int)(age.TotalDays / 7)}w";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{duration.Minutes}m";
    }
}
