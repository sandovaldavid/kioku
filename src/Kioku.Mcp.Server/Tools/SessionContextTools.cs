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
public sealed class SessionContextTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    VaultConfigService vaultConfig,
    ProjectWorkspaceService workspace,
    ObsidianBridgeService bridge)
{
    private static readonly string[] SessionsFolderCandidates = ["Sessions", "Journal", "Daily", "99_System/Sessions"];

    private static readonly string[] KnownAgentNames =
        ["claude", "codex", "antigravity", "opencode", "cursor", "gemini", "copilot", "windsurf"];

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
        "With a project, the session is stored in that project's sessions subfolder as " +
        "{date-time}-{agent}.md so multiple agents can hand work off to each other; " +
        "the agent name is auto-detected from the MCP client when not provided.")]
    public async Task<string> start_work_session(
        [Description("Optional name for the session (e.g. 'Thesis Chapter 3 Review'). Defaults to today's date.")] string session_name = "",
        [Description("Folder where session notes are stored (relative to vault root). Ignored when project is set.")] string sessions_folder = "Sessions",
        [Description("Optional goal or focus for this session.")] string goal = "",
        [Description("Project name: stores the session under {projects}/{project}/sessions/.")] string project = "",
        [Description("Agent running this session (claude, codex, ...). Auto-detected from the MCP client if empty.")] string agent = "",
        McpServer? server = null)
    {
        if (!string.IsNullOrWhiteSpace(project))
        {
            return await StartProjectSessionAsync(project, session_name, goal, agent, server);
        }

        var now = DateTime.Now;
        var dateStr = now.ToString("yyyy-MM-dd");
        var timeStr = now.ToString("HH:mm");
        var noteName = string.IsNullOrWhiteSpace(session_name)
            ? dateStr
            : $"{dateStr} — {session_name}";

        var destFolder = NoteHelpers.EnsureInsideVault(
            config.VaultPath,
            Path.Combine(config.VaultPath, sessions_folder));
        Directory.CreateDirectory(destFolder);

        var filePath = Path.Combine(destFolder, $"{noteName}.md");
        if (File.Exists(filePath))
        {
            // Append to existing session note instead of overwriting
            var appendContent = $"\n\n---\n\n## Session resumed at {timeStr}\n";
            await File.AppendAllTextAsync(filePath, appendContent, Encoding.UTF8);
            return $"[ok] Resumed existing session note: {sessions_folder}/{noteName}.md";
        }

        var frontmatter = new StringBuilder();
        frontmatter.AppendLine("---");
        frontmatter.AppendLine("tags:");
        frontmatter.AppendLine("  - session");
        frontmatter.AppendLine("  - work-log");
        frontmatter.AppendLine($"type: session");
        frontmatter.AppendLine($"status: active");
        frontmatter.AppendLine($"date: {dateStr}");
        frontmatter.AppendLine("---");

        var body = await TryRenderFolderTemplateAsync(
            sessions_folder,
            new Dictionary<string, string>
            {
                ["goal"] = string.IsNullOrWhiteSpace(goal) ? "" : goal,
                ["date"] = dateStr,
                ["time"] = timeStr,
            },
            noteName)
            ?? BuildDefaultSessionBody(noteName, dateStr, timeStr, goal);

        await File.WriteAllTextAsync(filePath, frontmatter + "\n" + body, Encoding.UTF8);

        var relativePath = Path.GetRelativePath(config.VaultPath, filePath).Replace('\\', '/');
        var evalResult = await bridge.EvaluateTemplaterInPlaceAsync(body, relativePath);
        if (evalResult.Applied)
        {
            await vault.SynchronizeFileReindexAsync(filePath);
        }

        var result = $"[ok] Work session started: {relativePath}";
        return evalResult.Warning is null ? result : $"{result}\n   [warning] {evalResult.Warning}";
    }

    private static string BuildDefaultSessionBody(string noteName, string dateStr, string timeStr, string goal)
    {
        var sb = new StringBuilder();
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

        return sb.ToString();
    }

    // end_work_session

    [McpServerTool, Description(
        "Closes the current work session by appending a summary of notes modified since the session started. " +
        "Updates the session note status to 'done'. For project sessions, the summary is also written " +
        "into the '## Summary' section at the top of the note so the next agent reads it first " +
        "via get_project_context.")]
    public async Task<string> end_work_session(
        [Description("Name or path of the session note to close. If empty, finds the most recent active session.")] string session_note = "",
        [Description("Optional summary or outcome of the session. Strongly recommended for project sessions: it is the handoff for the next agent.")] string summary = "",
        [Description("Project name: looks for the active session under {projects}/{project}/sessions/.")] string project = "")
    {
        Note? sessionNote;

        if (!string.IsNullOrWhiteSpace(session_note))
        {
            sessionNote = NoteHelpers.ResolveNote(session_note, vault);
            if (sessionNote is null)
            {
                return $"[error] Session note not found: '{session_note}'";
            }
        }
        else if (!string.IsNullOrWhiteSpace(project))
        {
            if (ProjectWorkspaceService.ValidateProjectName(project) is { } nameError)
            {
                return nameError;
            }

            var relSessions = workspace.ToVaultRelative(workspace.GetSubfolder(project, "sessions"));
            sessionNote = vault.GetNotesInFolder(relSessions)
                .Where(n => string.Equals(n.Metadata.Status, "active", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => n.LastModified)
                .FirstOrDefault();
            if (sessionNote is null)
            {
                return $"[error] No active session found for project '{project}'. " +
                       "Use start_work_session with the project parameter to begin one.";
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

        // Update status to done and surface the summary at the top for the next agent
        var rawContent = await File.ReadAllTextAsync(sessionNote.FilePath, Encoding.UTF8);
        var updatedContent = rawContent.Replace("status: active", "status: done");
        if (!string.IsNullOrWhiteSpace(summary))
        {
            updatedContent = WriteSummarySection(updatedContent, summary);
        }

        await File.WriteAllTextAsync(sessionNote.FilePath, updatedContent, Encoding.UTF8);
        await vault.SynchronizeFileReindexAsync(sessionNote.FilePath);

        return $"[ok] Session closed: {sessionNote.VaultRelativePath}\n" +
               $"   Duration: {FormatDuration(now - sessionStart.UtcDateTime)}\n" +
               $"   Notes touched: {modifiedNotes.Count}";
    }

    [McpServerTool, Description(
        "Lists all work session notes with their dates, status (active/done), and duration if closed.")]
    public Task<string> list_work_sessions(
        [Description("Folder where session notes are stored (relative to vault root). Auto-detects if empty.")] string sessions_folder = "",
        [Description("Project name: lists the sessions under {projects}/{project}/sessions/.")] string project = "")
    {
        if (!string.IsNullOrWhiteSpace(project))
        {
            if (ProjectWorkspaceService.ValidateProjectName(project) is { } nameError)
            {
                return Task.FromResult(nameError);
            }

            sessions_folder = workspace.ToVaultRelative(workspace.GetSubfolder(project, "sessions"));
        }

        var targetFolder = string.IsNullOrWhiteSpace(sessions_folder)
            ? FindSessionsFolder()
            : sessions_folder;

        if (string.IsNullOrEmpty(targetFolder))
        {
            return Task.FromResult("[info] No sessions folder found. Create a 'Sessions' folder or specify sessions_folder parameter.");
        }

        var sessionNotes = vault.GetNotesInFolder(targetFolder)
            .Where(n => string.Equals(n.Metadata.NoteType, "session", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.Metadata.Date)
            .ToList();

        if (sessionNotes.Count == 0)
        {
            return Task.FromResult($"[info] No work sessions found in '{targetFolder}'.");
        }

        var sb = new StringBuilder($"[ok] {sessionNotes.Count} work session(s) in '{targetFolder}':\n\n");

        foreach (var session in sessionNotes)
        {
            var dateStr = session.Metadata.Date?.ToString("yyyy-MM-dd") ?? "undated";
            var status = session.Metadata.Status ?? "unknown";

            sb.Append($"- {session.Name} ({dateStr}) — status: {status}");

            if (status.Equals("done", StringComparison.OrdinalIgnoreCase))
            {
                var durationMatch = System.Text.RegularExpressions.Regex.Match(session.RawContent, @"\*\*Duration:\*\*\s+(.+?)(?:\n|$)");
                if (durationMatch.Success)
                {
                    sb.Append($" — duration: {durationMatch.Groups[1].Value}");
                }
            }

            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    [McpServerTool, Description(
        "Returns all notes created or modified during a specific work session.")]
    public Task<string> get_session_activity(
        [Description("Name or path of the session note (e.g., '2025-06-25' or 'Sessions/2025-06-25').")] string session_note)
    {
        if (string.IsNullOrWhiteSpace(session_note))
        {
            return Task.FromResult("[error] The 'session_note' parameter cannot be empty.");
        }

        var note = NoteHelpers.ResolveNote(session_note, vault);
        if (note is null)
        {
            return Task.FromResult($"[error] Session note not found: '{session_note}'");
        }

        var isSession = string.Equals(note.Metadata.NoteType, "session", StringComparison.OrdinalIgnoreCase);
        if (!isSession)
        {
            return Task.FromResult($"[error] '{session_note}' is not a session note (type: session required).");
        }

        var sessionStart = note.LastModified;
        var allNotes = vault.GetAllNotes().ToList();

        // Extract the session end time from the note if it's closed
        var sessionEnd = DateTimeOffset.MaxValue;
        var endMatch = System.Text.RegularExpressions.Regex.Match(note.RawContent, @"## Session ended — (\d{2}:\d{2}) UTC");
        if (endMatch.Success && note.Metadata.Status?.Equals("done", StringComparison.OrdinalIgnoreCase) == true)
        {
            sessionEnd = note.LastModified;
        }

        var activityNotes = allNotes
            .Where(n => n.LastModified >= sessionStart &&
                        n.LastModified <= sessionEnd &&
                        !n.FilePath.Equals(note.FilePath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(n => n.LastModified)
            .ToList();

        if (activityNotes.Count == 0)
        {
            return Task.FromResult($"[info] No notes were modified during this session.");
        }

        var sb = new StringBuilder($"[ok] {activityNotes.Count} note(s) modified during session '{note.Name}':\n\n");

        foreach (var n in activityNotes)
        {
            var ageAtSession = sessionStart - n.LastModified;
            var ageStr = FormatDuration(ageAtSession);
            sb.AppendLine($"- {n.VaultRelativePath} (modified {ageStr} after session start)");
        }

        return Task.FromResult(sb.ToString());
    }

    // Private helpers

    private async Task<string> StartProjectSessionAsync(
        string project, string sessionName, string goal, string agent, McpServer? server)
    {
        if (ProjectWorkspaceService.ValidateProjectName(project) is { } nameError)
        {
            return nameError;
        }

        await workspace.EnsureProjectScaffoldAsync(project);

        var now = DateTime.Now;
        var agentName = NormalizeAgentName(string.IsNullOrWhiteSpace(agent) ? server?.ClientInfo?.Name : agent);
        var folder = workspace.GetSubfolder(project, "sessions");
        var filePath = Path.Combine(folder, $"{now:yyyy-MM-dd-HHmm}-{agentName}.md");

        if (File.Exists(filePath))
        {
            await File.AppendAllTextAsync(
                filePath, $"\n\n---\n\n## Session resumed at {now:HH:mm}\n", Encoding.UTF8);
            return $"[ok] Resumed existing session note: {workspace.ToVaultRelative(filePath)}";
        }

        var title = string.IsNullOrWhiteSpace(sessionName)
            ? $"{now:yyyy-MM-dd HH:mm} ({agentName})"
            : sessionName;
        var projectLink = $"[[{ProjectWorkspaceService.ProjectLeafName(project)}]]";
        var body = NoteHelpers.ExpandTemplateVariables(
            await workspace.ResolveTemplateAsync("session"),
            new Dictionary<string, string>
            {
                ["goal"] = string.IsNullOrWhiteSpace(goal) ? "_(not specified)_" : goal,
                ["agent"] = agentName,
                ["project"] = project,
                ["project_link"] = projectLink,
                ["date"] = now.ToString("yyyy-MM-dd"),
                ["time"] = now.ToString("HH:mm"),
            },
            noteTitle: title);

        var relFolder = workspace.ToVaultRelative(folder);
        var tags = NoteHelpers.MergeTagsWithInheritance(
            ["session", "work-log"],
            vaultConfig.GetInheritedTags(relFolder),
            vaultConfig.ExcludeFromTags);
        var frontmatter = NoteHelpers.BuildFrontmatter(
            tags,
            type: "session",
            status: "active",
            date: DateOnly.FromDateTime(now),
            domain: vaultConfig.GetDomainForFolder(relFolder),
            cssClasses: ["kioku-session"],
            extraFields: new Dictionary<string, string>
            {
                ["project"] = project,
                ["project_link"] = $"\"{projectLink}\"",
                ["agent"] = agentName,
            });

        await File.WriteAllTextAsync(filePath, frontmatter + "\n" + body, Encoding.UTF8);
        await vault.SynchronizeFileReindexAsync(filePath);

        var vaultRelPath = workspace.ToVaultRelative(filePath);
        var evalResult = await bridge.EvaluateTemplaterInPlaceAsync(body, vaultRelPath);
        if (evalResult.Applied)
        {
            await vault.SynchronizeFileReindexAsync(filePath);
        }

        var result = $"[ok] Work session started: {vaultRelPath} (agent: {agentName})\n" +
               "   Log work and decisions in the '## Log' section; close with end_work_session and a summary.";
        return evalResult.Warning is null ? result : $"{result}\n   [warning] {evalResult.Warning}";
    }

    /// <summary>
    /// Maps an MCP client name (e.g. 'claude-code 2.1') or user input to a short agent slug
    /// used in session file names. Unknown names are sanitized as-is; empty input becomes 'agent'.
    /// </summary>
    internal static string NormalizeAgentName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "agent";
        }

        var lowered = raw.ToLowerInvariant();
        foreach (var known in KnownAgentNames)
        {
            if (lowered.Contains(known))
            {
                return known;
            }
        }

        var sanitized = NoteHelpers.SanitizeFileName(lowered);
        return string.IsNullOrWhiteSpace(sanitized) ? "agent" : sanitized;
    }

    /// <summary>
    /// Replaces the content of the '## Summary' section (up to the next H2) with the given
    /// summary. Notes without a Summary section (legacy/global sessions) are returned unchanged —
    /// their summary already lives in the appended '## Session ended' block.
    /// </summary>
    internal static string WriteSummarySection(string content, string summary)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        var headingIndex = lines.FindIndex(l =>
            l.TrimEnd().Equals("## Summary", StringComparison.OrdinalIgnoreCase));
        if (headingIndex < 0)
        {
            return content;
        }

        var sectionEnd = headingIndex + 1;
        while (sectionEnd < lines.Count && !lines[sectionEnd].StartsWith("## ", StringComparison.Ordinal))
        {
            sectionEnd++;
        }

        lines.RemoveRange(headingIndex + 1, sectionEnd - (headingIndex + 1));
        lines.InsertRange(headingIndex + 1, ["", summary.Trim(), ""]);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Resolves a per-folder template (Kioku's own template_folders override, or Templater's own
    /// Folder Templates settings) for <paramref name="targetFolder"/>, reads and renders it with
    /// {{var}} substitution. Returns null when no template is configured for this folder or the
    /// configured file doesn't exist — callers should fall back to their own hardcoded body.
    /// </summary>
    private async Task<string?> TryRenderFolderTemplateAsync(
        string targetFolder, IReadOnlyDictionary<string, string> variables, string? noteTitle)
    {
        var resolvedPath = await vaultConfig.ResolveFolderTemplateAsync(targetFolder);
        if (resolvedPath is null)
        {
            return null;
        }

        var fullPath = NoteHelpers.EnsureInsideVault(config.VaultPath, Path.Combine(config.VaultPath, resolvedPath));
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var raw = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
        return NoteHelpers.ExpandTemplateVariables(raw, variables, noteTitle);
    }

    private string? FindSessionsFolder()
    {
        foreach (var folder in SessionsFolderCandidates)
        {
            var folderPath = Path.Combine(config.VaultPath, folder);
            if (Directory.Exists(folderPath))
            {
                return folder;
            }
        }
        return null;
    }

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
