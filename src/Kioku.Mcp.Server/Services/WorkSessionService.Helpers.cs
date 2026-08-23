using System.Globalization;
using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Domain.Coordination;

namespace Kioku.Mcp.Server.Services;

internal sealed partial class WorkSessionService
{
    private static readonly string[] KnownAgentNames =
        ["claude", "codex", "antigravity", "opencode", "cursor", "gemini", "copilot", "windsurf"];

    private static readonly string[] RequiredCoordinatedPreconditions =
        ["expected_revision", "claim_id", "fence_generation"];

    private static readonly string[] RequiredCoordinationLinkPreconditions =
        ["expected_revision"];

    /// <summary>
    /// Notes eligible to be reported as "touched during this session". Scoped to the session's
    /// own project folder when the session has one, so a concurrent agent's or user's edits to an
    /// unrelated project never leak into this session's activity summary (GitHub #438). A session
    /// with no project (legacy/global sessions) falls back to the prior vault-wide behavior, since
    /// there is no narrower folder to scope to.
    /// </summary>
    private IEnumerable<Note> GetProjectScopedNotes(string? project) =>
        string.IsNullOrWhiteSpace(project)
            ? _vault.GetAllNotes()
            : _vault.GetNotesInFolder(_workspace.GetProjectFolder(project));

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
            ParseUtc(GetString(fields, "ended_at")),
            GetString(fields, "parent_session_id"),
            ReadCoordinationLink(fields));
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

    private CoordinationRequestResult NormalizeCoordinationRequest(
        WorkSessionCoordinationRequest? request)
    {
        if (request is null || !request.IsRequested)
        {
            return new(null, null);
        }

        if (!_vaultConfig.IsGroupEnabled("coordination"))
        {
            return new(
                null,
                FormatError(
                    "COORDINATION_DISABLED",
                    "The coordination capability is disabled for this vault.",
                    new { capability = "coordination" }));
        }

        if (_coordination is null || _mutations is null)
        {
            return new(
                null,
                FormatError(
                    "COORDINATION_UNAVAILABLE",
                    "The coordination compatibility service is unavailable.",
                    new { capability = "coordination" }));
        }

        var runId = request.RunId?.Trim();
        var workItemId = request.WorkItemId?.Trim();
        if (string.IsNullOrWhiteSpace(runId) != string.IsNullOrWhiteSpace(workItemId))
        {
            return new(
                null,
                FormatError(
                    "COORDINATION_LINK_INVALID",
                    "run_id and work_item_id must be supplied together.",
                    new { run_id = runId, work_item_id = workItemId }));
        }

        if (string.IsNullOrWhiteSpace(runId))
        {
            return new(
                null,
                FormatError(
                    "COORDINATION_LINK_INVALID",
                    "attempt_id cannot be supplied without run_id and work_item_id.",
                    new { attempt_id = request.AttemptId }));
        }

        var attemptId = string.IsNullOrWhiteSpace(request.AttemptId)
            ? Guid.CreateVersion7().ToString("D")
            : request.AttemptId.Trim();
        var invalid = FirstInvalidIdentifier(
            ("run_id", runId),
            ("work_item_id", workItemId),
            ("attempt_id", attemptId));
        if (invalid is not null)
        {
            return new(
                null,
                FormatError(
                    "COORDINATION_LINK_INVALID",
                    $"{invalid.Value.Field} is missing or unsafe.",
                    new { field = invalid.Value.Field }));
        }

        return new(
            new WorkSessionCoordinationLink(
                runId!,
                workItemId!,
                attemptId),
            null);
    }

    private async Task<CoordinationLinkResult> TryCreateCoordinationLinkAsync(
        WorkSessionCoordinationLink? requested,
        string sessionId,
        string parentSessionId,
        string relativePath,
        string project,
        string agent,
        string clientName,
        CancellationToken cancellationToken)
    {
        if (requested is null)
        {
            return new(null, null);
        }

        try
        {
            var snapshot = await _coordination!.CreateWorkItemAsync(
                new CoordinationCreateWorkItemRequest
                {
                    RunId = requested.RunId,
                    WorkItemId = requested.WorkItemId,
                    AttemptId = requested.AttemptId,
                    SessionId = sessionId,
                    ParentSessionId = string.IsNullOrWhiteSpace(parentSessionId)
                        ? null
                        : parentSessionId.Trim(),
                    Agent = agent,
                    ClientName = clientName,
                    Project = string.IsNullOrWhiteSpace(project) ? "global" : project,
                    ResourceScope = [$"note:{relativePath}"],
                    Summary = "The work session was linked to a durable coordination work item.",
                    TransitionId = $"session-link:{sessionId}:{requested.WorkItemId}",
                },
                cancellationToken).ConfigureAwait(false);

            return new(
                new WorkSessionCoordinationLink(
                    snapshot.Projection.RunId,
                    snapshot.Projection.WorkItemId,
                    snapshot.Projection.AttemptId ?? requested.AttemptId),
                null);
        }
        catch (CoordinationOperationException exception)
        {
            return new(
                null,
                $"Coordination link was not created ({exception.Code}); the session remains legacy/uncoordinated.");
        }
    }

    private async Task<string?> PersistCoordinationLinkAsync(
        string filePath,
        WorkSessionCoordinationLink link,
        CancellationToken cancellationToken)
    {
        var current = await _fileSystem.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var document = FrontmatterDocument.Parse(current);
        var fields = document.ToNoteMetadata().ExtraFields;
        var existing = ReadCoordinationLink(fields);
        var hasPartialLink = HasCoordinationMetadata(fields);
        if (existing is not null)
        {
            return existing == link
                ? null
                : "Coordination link was not persisted because the session already references another work item.";
        }

        if (hasPartialLink)
        {
            return "Coordination link was not persisted because the session has partial coordination metadata.";
        }

        document.SetString("run_id", link.RunId);
        document.SetString("work_item_id", link.WorkItemId);
        document.SetString("attempt_id", link.AttemptId);
        try
        {
            var preconditions = _mutations is null
                ? null
                : new VaultMutationPreconditions
                {
                    ExpectedRevision = VaultRevision.Compute(current),
                    ResourceKey = $"note:{Path.GetRelativePath(_config.VaultPath, filePath).Replace('\\', '/')}",
                };
            await WriteSessionTextAsync(
                filePath,
                document.Serialize(),
                preconditions,
                cancellationToken).ConfigureAwait(false);
            await _vault.SynchronizeFileReindexAsync(filePath).WaitAsync(cancellationToken);
            return null;
        }
        catch (VaultMutationException exception)
        {
            return $"Coordination link was not persisted ({exception.Code}); the session remains usable without the link.";
        }
        catch (IOException)
        {
            return "Coordination link was not persisted; the session remains usable without the link.";
        }
    }

    private static string? ValidateRequestedCoordinationLink(
        WorkSessionCoordinationLink? existing,
        WorkSessionCoordinationLink? requested)
    {
        if (existing is null || requested is null)
        {
            return null;
        }

        return existing == requested
            ? null
            : FormatError(
                "COORDINATION_LINK_MISMATCH",
                "The requested coordination context does not match the session's persisted context.",
                new
                {
                    persisted_run_id = existing.RunId,
                    persisted_work_item_id = existing.WorkItemId,
                    persisted_attempt_id = existing.AttemptId,
                });
    }

    private string? ValidateCoordinationLinkPreconditions(
        SessionDescriptor session,
        VaultMutationPreconditions? preconditions,
        WorkSessionCoordinationLink? persistedLink,
        WorkSessionCoordinationLink? requestedLink)
    {
        if (persistedLink is null && requestedLink is null)
        {
            return null;
        }

        if (_coordination is null || _mutations is null)
        {
            return FormatError(
                "COORDINATION_UNAVAILABLE",
                "The coordination compatibility service is unavailable; the session cannot be mutated safely.",
                new { capability = "coordination", session_id = session.SessionId });
        }

        if (persistedLink is null && requestedLink is not null &&
            preconditions is not { HasContentPrecondition: true })
        {
            return FormatError(
                "COORDINATED_SESSION_REQUIRES_PRECONDITIONS",
                "Linking an existing session requires an expected revision or hash before it can be mutated.",
                new
                {
                    session_id = session.SessionId,
                    run_id = requestedLink.RunId,
                    work_item_id = requestedLink.WorkItemId,
                    required = RequiredCoordinationLinkPreconditions,
                });
        }

        return ValidateCoordinatedPreconditions(session, preconditions, persistedLink);
    }

    private string? ValidateCoordinatedPreconditions(
        SessionDescriptor session,
        VaultMutationPreconditions? preconditions,
        WorkSessionCoordinationLink? persistedLink)
    {
        if (persistedLink is null)
        {
            return null;
        }

        if (_coordination is null || _mutations is null)
        {
            return FormatError(
                "COORDINATION_UNAVAILABLE",
                "The coordination compatibility service is unavailable; the session cannot be mutated safely.",
                new { capability = "coordination", session_id = session.SessionId });
        }

        if (preconditions is { HasContentPrecondition: true, HasClaimPrecondition: true })
        {
            return null;
        }

        return FormatError(
            "COORDINATED_SESSION_REQUIRES_PRECONDITIONS",
            "This coordinated session requires an expected revision/hash and the current claim fence before it can be mutated.",
            new
            {
                session_id = session.SessionId,
                run_id = persistedLink.RunId,
                work_item_id = persistedLink.WorkItemId,
                required = RequiredCoordinatedPreconditions,
            });
    }

    private static object? ToCoordinationPayload(WorkSessionCoordinationLink? link) =>
        link is null
            ? null
            : new
            {
                run_id = link.RunId,
                work_item_id = link.WorkItemId,
                attempt_id = link.AttemptId,
            };

    private static string? CombineWarnings(params string?[] warnings)
    {
        var values = warnings
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
        return values.Length == 0 ? null : string.Join(" ", values);
    }

    private static (string Field, string Value)? FirstInvalidIdentifier(
        params (string Field, string? Value)[] values)
    {
        foreach (var (field, value) in values)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > 128 ||
                value.Any(character =>
                    !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':'))
            {
                return (field, value ?? string.Empty);
            }
        }

        return null;
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
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Work Session — {title}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Started:** {FormatUtc(startedAt)}");
        if (!string.IsNullOrWhiteSpace(goal))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Goal:** {goal}");
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
        List<Note> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## Session ended — {FormatUtc(endedAt)}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Duration:** {FormatDuration(endedAt - startedAt)}");
        if (!string.IsNullOrWhiteSpace(summary))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Outcome:** {summary.Trim()}");
        }
        sb.AppendLine();
        if (notes.Count == 0)
        {
            sb.AppendLine("_(no notes were modified during this session)_");
        }
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"### Notes touched during session ({notes.Count})");
            foreach (var note in notes)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- [[{note.Name}]]");
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

    private static WorkSessionCoordinationLink? ReadCoordinationLink(
        IReadOnlyDictionary<string, string> fields)
    {
        var runId = GetString(fields, "run_id");
        var workItemId = GetString(fields, "work_item_id");
        var attemptId = GetString(fields, "attempt_id");
        return string.IsNullOrWhiteSpace(runId) ||
            string.IsNullOrWhiteSpace(workItemId) ||
            string.IsNullOrWhiteSpace(attemptId)
                ? null
                : new WorkSessionCoordinationLink(runId, workItemId, attemptId);
    }

    private static WorkSessionCoordinationLink? ReadCoordinationLink(
        IReadOnlyDictionary<string, object?> fields)
    {
        var runId = GetString(fields, "run_id");
        var workItemId = GetString(fields, "work_item_id");
        var attemptId = GetString(fields, "attempt_id");
        return string.IsNullOrWhiteSpace(runId) ||
            string.IsNullOrWhiteSpace(workItemId) ||
            string.IsNullOrWhiteSpace(attemptId)
                ? null
                : new WorkSessionCoordinationLink(runId, workItemId, attemptId);
    }

    private static bool HasCoordinationMetadata(IReadOnlyDictionary<string, string> fields) =>
        fields.Keys.Any(key =>
            key.Equals("run_id", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("work_item_id", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("attempt_id", StringComparison.OrdinalIgnoreCase));

    private static bool HasCoordinationMetadata(IReadOnlyDictionary<string, object?> fields) =>
        fields.Keys.Any(key =>
            key.Equals("run_id", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("work_item_id", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("attempt_id", StringComparison.OrdinalIgnoreCase));

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
        string? warning,
        WorkSessionCoordinationLink? coordination = null)
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
            coordination = ToCoordinationPayload(coordination),
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

    private sealed record CoordinationRequestResult(
        WorkSessionCoordinationLink? Link,
        string? Error);

    private sealed record CoordinationLinkResult(
        WorkSessionCoordinationLink? Link,
        string? Warning);

    private sealed record SessionDescriptor(
        Note Note,
        string? SessionId,
        string? Project,
        string? Agent,
        string? ClientName,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset? EndedAt,
        string? ParentSessionId,
        WorkSessionCoordinationLink? Coordination);

    private sealed record SessionResolution(SessionDescriptor? Session, string? Error);
}
