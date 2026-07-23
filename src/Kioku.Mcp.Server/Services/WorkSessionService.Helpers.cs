using System.Globalization;
using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

internal sealed partial class WorkSessionService
{
    private static readonly string[] KnownAgentNames =
        ["claude", "codex", "antigravity", "opencode", "cursor", "gemini", "copilot", "windsurf"];

    private List<SessionDescriptor> FindActiveSessions() =>
        _vault.GetAllNotes()
            .Where(note =>
                IsSessionNote(note) &&
                string.Equals(note.Metadata.Status, "active", StringComparison.OrdinalIgnoreCase))
            .Select(ReadSession)
            .ToList();

    private List<SessionDescriptor> FindSessionsById(string sessionId) =>
        _vault.GetAllNotes()
            .Where(IsSessionNote)
            .Select(ReadSession)
            .Where(session => string.Equals(
                session.SessionId,
                sessionId,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static bool IsSessionNote(Note note) =>
        string.Equals(note.Metadata.NoteType, "session", StringComparison.OrdinalIgnoreCase);

    private static SessionDescriptor ReadSession(Note note)
    {
        var fields = note.Metadata.ExtraFields;
        return new SessionDescriptor(
            note,
            GetString(fields, "session_id"),
            GetString(fields, "project"),
            GetString(fields, "agent"),
            GetString(fields, "client_name"),
            note.Metadata.Status ?? "unknown",
            ParseUtc(GetString(fields, "started_at")) ?? note.LastModified.ToUniversalTime(),
            ParseUtc(GetString(fields, "ended_at")));
    }

    private static SemaphoreSlim GetLock(SessionDescriptor session)
    {
        var key = session.SessionId ?? Path.GetFullPath(session.Note.FilePath);
        return SessionLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }

    private async Task<string?> TryRenderFolderTemplateAsync(
        string targetFolder,
        IReadOnlyDictionary<string, string> variables,
        string title,
        CancellationToken cancellationToken)
    {
        var templatePath = await _vaultConfig.ResolveFolderTemplateAsync(targetFolder)
            .WaitAsync(cancellationToken);
        if (templatePath is null)
        {
            return null;
        }

        var fullPath = NoteHelpers.EnsureInsideVault(
            _config.VaultPath,
            Path.Combine(_config.VaultPath, templatePath));
        var raw = await _fileSystem.ReadIfExistsAsync(fullPath, cancellationToken);
        return raw is null
            ? null
            : NoteHelpers.ExpandTemplateVariables(
                raw,
                variables,
                title,
                _timeProvider.GetUtcNow());
    }

    private string? FindSessionsFolder()
    {
        var candidates = SessionsFolderCandidates.AsEnumerable();
        var configured = _vaultConfig.GetFolder("sessions");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates = new[] { configured }.Concat(candidates);
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(folder => _fileSystem.DirectoryExists(
                Path.Combine(_config.VaultPath, folder)));
    }

    private static string BuildDefaultBody(
        string title,
        string goal,
        DateTimeOffset startedAt)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Work Session — {title}");
        sb.AppendLine();
        sb.AppendLine($"**Started:** {FormatUtc(startedAt)}");
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

    private static string BuildEndBlock(
        DateTimeOffset endedAt,
        DateTimeOffset startedAt,
        string summary,
        IReadOnlyCollection<Note> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"## Session ended — {FormatUtc(endedAt)}");
        sb.AppendLine();
        sb.AppendLine($"**Duration:** {FormatDuration(endedAt - startedAt)}");
        if (!string.IsNullOrWhiteSpace(summary))
        {
            sb.AppendLine($"**Outcome:** {summary.Trim()}");
        }
        sb.AppendLine();
        if (notes.Count == 0)
        {
            sb.AppendLine("_(no notes were modified during this session)_");
        }
        else
        {
            sb.AppendLine($"### Notes touched during session ({notes.Count})");
            foreach (var note in notes)
            {
                sb.AppendLine($"- [[{note.Name}]]");
            }
        }
        return sb.ToString();
    }

    internal static string NormalizeAgentName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "agent";
        }

        var lowered = raw.ToLowerInvariant();
        foreach (var known in KnownAgentNames)
        {
            if (lowered.Contains(known, StringComparison.Ordinal))
            {
                return known;
            }
        }

        var sanitized = NoteHelpers.SanitizeFileName(lowered);
        return string.IsNullOrWhiteSpace(sanitized) ? "agent" : sanitized;
    }

    internal static string WriteSummarySection(string content, string summary)
    {
        var newLine = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();
        var heading = lines.FindIndex(line =>
            line.TrimEnd().Equals("## Summary", StringComparison.OrdinalIgnoreCase));
        if (heading < 0)
        {
            return content;
        }

        var end = heading + 1;
        while (end < lines.Count && !lines[end].StartsWith("## ", StringComparison.Ordinal))
        {
            end++;
        }
        lines.RemoveRange(heading + 1, end - heading - 1);
        lines.InsertRange(heading + 1, ["", summary.Trim(), ""]);
        return string.Join(newLine, lines);
    }

    private static string? NormalizeClientName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? GetString(
        IReadOnlyDictionary<string, string> fields,
        string key) =>
        fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static string? GetString(
        IReadOnlyDictionary<string, object?> fields,
        string key) =>
        fields.TryGetValue(key, out var value) && value is not null
            ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim()
            : null;

    private static DateTimeOffset? ParseUtc(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
                ? parsed.ToUniversalTime()
                : null;

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string FormatSuccess(
        string action,
        string id,
        string path,
        string project,
        string agent,
        string clientName,
        DateTimeOffset startedAt,
        string? warning)
    {
        var payload = JsonSerializer.Serialize(new
        {
            action,
            session_id = id,
            path,
            project = string.IsNullOrWhiteSpace(project) ? null : project,
            agent,
            client_name = clientName,
            started_at = FormatUtc(startedAt),
        });
        var result = $"[ok] Work session {action}: {path}\n{payload}";
        return warning is null ? result : $"{result}\n[warning] {warning}";
    }

    private static string FormatError(string code, string message, object details) =>
        $"[error] {message}\n{JsonSerializer.Serialize(new { code, message, details })}";

    private static string FormatAmbiguity(
        IReadOnlyCollection<SessionDescriptor> sessions,
        string message) =>
        $"[error] {message}\n{JsonSerializer.Serialize(new
        {
            code = "AMBIGUOUS_SESSION",
            message,
            candidates = sessions.Select(session => new
            {
                session_id = session.SessionId,
                path = session.Note.VaultRelativePath,
                project = session.Project,
                agent = session.Agent,
                client_name = session.ClientName,
                started_at = FormatUtc(session.StartedAt),
            }),
        })}";

    private static string FormatAge(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var age = now - timestamp.ToUniversalTime();
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }
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
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{duration.Minutes}m";
    }

    private sealed record SessionDescriptor(
        Note Note,
        string? SessionId,
        string? Project,
        string? Agent,
        string? ClientName,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset? EndedAt);

    private sealed record SessionResolution(SessionDescriptor? Session, string? Error);
}
