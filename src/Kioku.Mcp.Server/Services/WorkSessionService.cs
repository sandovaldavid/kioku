using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

internal sealed partial class WorkSessionService
{
    private static readonly string[] SessionsFolderCandidates =
        ["Sessions", "Journal", "Daily", "99_System/Sessions"];

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SessionLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly VaultIndexService _vault;
    private readonly KiokuConfiguration _config;
    private readonly VaultConfigService _vaultConfig;
    private readonly ProjectWorkspaceService _workspace;
    private readonly ObsidianBridgeService _bridge;
    private readonly IWorkSessionFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly IVaultMutationService? _mutations;
    private readonly ICoordinationService? _coordination;

    public WorkSessionService(
        VaultIndexService vault,
        KiokuConfiguration config,
        VaultConfigService vaultConfig,
        ProjectWorkspaceService workspace,
        ObsidianBridgeService bridge,
        IWorkSessionFileSystem fileSystem,
        TimeProvider timeProvider,
        IVaultMutationService? mutations = null,
        ICoordinationService? coordination = null)
    {
        _vault = vault;
        _config = config;
        _vaultConfig = vaultConfig;
        _workspace = workspace;
        _bridge = bridge;
        _fileSystem = fileSystem;
        _timeProvider = timeProvider;
        _mutations = mutations;
        _coordination = coordination;
    }

    public Task<string> GetWorkContextAsync(
        string inboxFolder,
        int maxPerSection,
        string recentFolder,
        int recentLimit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        inboxFolder = string.IsNullOrWhiteSpace(inboxFolder)
            ? _vaultConfig.GetFolder("inbox") ?? "Inbox"
            : inboxFolder;

        var now = _timeProvider.GetUtcNow();
        var sb = new StringBuilder("# Work Context Snapshot\n\n");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Generated:** {now:yyyy-MM-dd HH:mm} UTC\n");

        var inbox = _vault.GetNotesInFolder(inboxFolder)
            .OrderByDescending(note => note.LastModified)
            .Take(maxPerSection)
            .ToList();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## Inbox ({inboxFolder}) — {inbox.Count} note(s)");
        if (inbox.Count == 0)
        {
            sb.AppendLine("_(empty — inbox is clear)_");
        }
        else
        {
            foreach (var note in inbox)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine(CultureInfo.InvariantCulture, $"- [[{note.Name}]] _(modified {FormatAge(note.LastModified, now)} ago)_");
            }
        }

        sb.AppendLine();
        var drafts = _vault.FilterByMetadata(status: "draft")
            .OrderByDescending(note => note.LastModified)
            .Take(maxPerSection)
            .ToList();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## In Progress — Drafts ({drafts.Count} note(s))");
        if (drafts.Count == 0)
        {
            sb.AppendLine("_(no draft notes found)_");
        }
        else
        {
            foreach (var note in drafts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tags = note.Metadata.Tags.Count == 0
                    ? string.Empty
                    : $" [{string.Join(", ", note.Metadata.Tags.Take(2).Select(tag => "#" + tag))}]";
                sb.AppendLine(CultureInfo.InvariantCulture, $"- [[{note.Name}]]{tags} _(modified {FormatAge(note.LastModified, now)} ago)_");
            }
        }

        sb.AppendLine();
        var recent = (string.IsNullOrWhiteSpace(recentFolder)
                ? _vault.GetAllNotes()
                : _vault.GetNotesInFolder(recentFolder))
            .OrderByDescending(note => note.LastModified)
            .Take(recentLimit > 0 ? recentLimit : maxPerSection)
            .ToList();
        var scope = string.IsNullOrWhiteSpace(recentFolder) ? string.Empty : $" in '{recentFolder}'";
        sb.AppendLine(CultureInfo.InvariantCulture, $"## Recently Modified{scope} ({recent.Count} note(s))");
        foreach (var note in recent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sb.AppendLine(CultureInfo.InvariantCulture, $"- [[{note.Name}]] _(modified {FormatAge(note.LastModified, now)} ago)_");
        }

        sb.AppendLine();
        sb.AppendLine("## Active Session");
        var active = FindActiveSessions().OrderByDescending(session => session.StartedAt).ToList();
        if (active.Count == 0)
        {
            sb.AppendLine("_(no active session — use `start_work_session` to begin one)_");
        }
        else
        {
            foreach (var session in active)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var project = string.IsNullOrWhiteSpace(session.Project) ? "global" : session.Project;
                var id = session.SessionId ?? "(legacy session without id)";
                var coordination = session.Coordination is null
                    ? string.Empty
                    : $" · coordination `{session.Coordination.RunId}/{session.Coordination.WorkItemId}`";
                sb.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"- [[{session.Note.Name}]] — `{id}` · {session.Agent ?? "unknown agent"} · {project}{coordination} " +
                    $"_(started {FormatAge(session.StartedAt, now)} ago)_");
            }
        }

        return Task.FromResult(sb.ToString());
    }

    public async Task<string> StartAsync(
        string sessionName,
        string sessionsFolder,
        string goal,
        string project,
        string agent,
        string sessionId,
        string parentSessionId,
        string? mcpClientName,
        WorkSessionCoordinationRequest? coordination = null,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestedCoordination = NormalizeCoordinationRequest(coordination);
        if (requestedCoordination.Error is not null)
        {
            return requestedCoordination.Error;
        }

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            return await ResumeAsync(
                sessionId.Trim(),
                project,
                preconditions,
                requestedCoordination.Link,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(project) &&
            ProjectWorkspaceService.ValidateProjectName(project) is { } projectError)
        {
            return projectError;
        }

        var now = _timeProvider.GetUtcNow();
        var clientName = NormalizeClientName(mcpClientName);
        var agentName = NormalizeAgentName(string.IsNullOrWhiteSpace(agent) ? clientName : agent);
        clientName ??= string.IsNullOrWhiteSpace(agent) ? "unknown" : agent.Trim();
        var id = Guid.CreateVersion7().ToString("D", CultureInfo.InvariantCulture);

        string targetFolder;
        string relativeFolder;
        string? projectLink = null;

        if (!string.IsNullOrWhiteSpace(project))
        {
            await _workspace.EnsureProjectScaffoldAsync(project).WaitAsync(cancellationToken);
            targetFolder = _workspace.GetSubfolder(project, "sessions");
            relativeFolder = _workspace.ToVaultRelative(targetFolder);
            projectLink = $"[[{ProjectWorkspaceService.ProjectLeafName(project)}]]";
        }
        else
        {
            relativeFolder = string.IsNullOrWhiteSpace(sessionsFolder)
                ? _vaultConfig.GetFolder("sessions") ?? "Sessions"
                : sessionsFolder;
            targetFolder = NoteHelpers.EnsureInsideVault(
                _config.VaultPath,
                Path.Combine(_config.VaultPath, relativeFolder));
        }

        var title = string.IsNullOrWhiteSpace(sessionName)
            ? $"{now:yyyy-MM-dd HH:mm:ss} ({agentName})"
            : sessionName.Trim();
        var preferredName = !string.IsNullOrWhiteSpace(project)
            ? $"{now:yyyy-MM-dd-HHmm}-{agentName}.md"
            : string.IsNullOrWhiteSpace(sessionName)
                ? $"{now:yyyy-MM-dd-HHmm}-{agentName}.md"
                : $"{now:yyyy-MM-dd} — {sessionName.Trim()}.md";
        var fallbackName = $"{Path.GetFileNameWithoutExtension(preferredName)}-{id[..8]}.md";

        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["session_id"] = id,
            ["agent"] = agentName,
            ["client_name"] = clientName,
            ["started_at"] = FormatUtc(now),
        };
        if (!string.IsNullOrWhiteSpace(project))
        {
            extra["project"] = project;
            extra["project_link"] = projectLink!;
        }
        if (!string.IsNullOrWhiteSpace(parentSessionId))
        {
            extra["parent_session_id"] = parentSessionId.Trim();
        }

        var tags = NoteHelpers.MergeTagsWithInheritance(
            ["session", "work-log"],
            _vaultConfig.GetInheritedTags(relativeFolder),
            _vaultConfig.ExcludeFromTags);
        var frontmatter = NoteHelpers.BuildFrontmatter(
            tags,
            type: "session",
            status: "active",
            date: DateOnly.FromDateTime(now.UtcDateTime),
            domain: _vaultConfig.GetDomainForFolder(relativeFolder),
            cssClasses: string.IsNullOrWhiteSpace(project) ? null : ["kioku-session"],
            extraFields: extra,
            updated: _vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(now.UtcDateTime) : null);

        var body = !string.IsNullOrWhiteSpace(project)
            ? await BuildProjectBodyAsync(
                title,
                goal,
                project,
                projectLink!,
                agentName,
                now,
                cancellationToken)
            : await BuildGlobalBodyAsync(title, goal, relativeFolder, now, cancellationToken);
        var filePath = await WriteNewSessionFileAsync(
            targetFolder,
            preferredName,
            fallbackName,
            frontmatter + "\n" + body,
            cancellationToken);
        await _vault.SynchronizeFileReindexAsync(filePath).WaitAsync(cancellationToken);

        var relativePath = Path.GetRelativePath(_config.VaultPath, filePath).Replace('\\', '/');
        var evaluation = await _bridge.EvaluateTemplaterInPlaceAsync(
            body,
            relativePath,
            cancellationToken);
        if (evaluation.Applied)
        {
            await _vault.SynchronizeFileReindexAsync(filePath).WaitAsync(cancellationToken);
        }

        var coordinationResult = await TryCreateCoordinationLinkAsync(
            requestedCoordination.Link,
            id,
            parentSessionId,
            relativePath,
            project,
            agentName,
            clientName,
            cancellationToken);
        if (coordinationResult.Link is not null)
        {
            var linkWarning = await PersistCoordinationLinkAsync(
                filePath,
                coordinationResult.Link,
                cancellationToken);
            coordinationResult = coordinationResult with
            {
                Warning = CombineWarnings(coordinationResult.Warning, linkWarning),
                Link = linkWarning is null ? coordinationResult.Link : null,
            };
        }

        return FormatSuccess(
            "started",
            id,
            relativePath,
            project,
            agentName,
            clientName,
            now,
            CombineWarnings(evaluation.Warning, coordinationResult.Warning),
            coordinationResult.Link);
    }

    public async Task<string> EndAsync(
        string sessionNote,
        string summary,
        string project,
        string sessionId,
        string agent,
        string? mcpClientName,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolution = ResolveSession(
            sessionId,
            sessionNote,
            project,
            agent,
            NormalizeClientName(mcpClientName));
        return resolution.Error is null
            ? await CloseAsync(resolution.Session!, summary, preconditions, cancellationToken)
            : resolution.Error;
    }

    public Task<string> ListAsync(
        string sessionsFolder,
        string project,
        bool includeActivity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(project))
        {
            if (ProjectWorkspaceService.ValidateProjectName(project) is { } error)
            {
                return Task.FromResult(error);
            }
            sessionsFolder = _workspace.ToVaultRelative(_workspace.GetSubfolder(project, "sessions"));
        }

        var targetFolder = string.IsNullOrWhiteSpace(sessionsFolder)
            ? FindSessionsFolder()
            : sessionsFolder;
        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            return Task.FromResult(
                "[info] No sessions folder found. Create a 'Sessions' folder or specify sessions_folder.");
        }

        var sessions = _vault.GetNotesInFolder(targetFolder)
            .Where(IsSessionNote)
            .Select(ReadSession)
            .OrderByDescending(session => session.StartedAt)
            .ToList();
        if (sessions.Count == 0)
        {
            return Task.FromResult($"[info] No work sessions found in '{targetFolder}'.");
        }

        var now = _timeProvider.GetUtcNow();
        var sb = new StringBuilder($"[ok] {sessions.Count} work session(s) in '{targetFolder}':\n\n");
        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = session.SessionId ?? "(legacy)";
            var projectLabel = string.IsNullOrWhiteSpace(session.Project) ? "global" : session.Project;
            var end = session.EndedAt ??
                (session.Status.Equals("done", StringComparison.OrdinalIgnoreCase)
                    ? session.Note.LastModified.ToUniversalTime()
                    : now);
            sb.AppendLine(
                CultureInfo.InvariantCulture,
                $"- {session.Note.Name} — id: `{id}` — status: {session.Status} — agent: {session.Agent ?? "unknown"} — " +
                $"project: {projectLabel} — started: {FormatUtc(session.StartedAt)} — duration: {FormatDuration(end - session.StartedAt)}");
            if (session.Coordination is not null)
            {
                sb.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"  Coordination: run `{session.Coordination.RunId}`, work item `{session.Coordination.WorkItemId}`, " +
                    $"attempt `{session.Coordination.AttemptId}`.");
            }
            if (includeActivity)
            {
                var activityEnd = session.Status.Equals("active", StringComparison.OrdinalIgnoreCase)
                    ? DateTimeOffset.MaxValue
                    : end;
                AppendActivity(sb, session, activityEnd);
            }
        }

        return Task.FromResult(sb.ToString());
    }

    private async Task<string> ResumeAsync(
        string sessionId,
        string project,
        VaultMutationPreconditions? preconditions,
        WorkSessionCoordinationLink? requestedLink,
        CancellationToken cancellationToken)
    {
        var matches = FindSessionsById(sessionId);
        if (matches.Count == 0)
        {
            return FormatError(
                "SESSION_NOT_FOUND",
                $"No work session exists with session_id '{sessionId}'.",
                new { session_id = sessionId });
        }
        if (matches.Count > 1)
        {
            return FormatAmbiguity(matches, $"Duplicate session_id '{sessionId}' was found.");
        }

        var session = matches[0];
        if (!string.IsNullOrWhiteSpace(project) &&
            !string.Equals(session.Project, project, StringComparison.OrdinalIgnoreCase))
        {
            return FormatError(
                "SESSION_PROJECT_MISMATCH",
                $"Session '{sessionId}' belongs to project '{session.Project ?? "global"}', not '{project}'.",
                new { session_id = sessionId, expected_project = project, actual_project = session.Project });
        }

        var gate = GetLock(session);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var document = FrontmatterDocument.Parse(
                await _fileSystem.ReadAllTextAsync(session.Note.FilePath, cancellationToken));
            var metadata = document.ToFrontmatter();
            var status = metadata.Status ?? "unknown";
            if (!status.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                return FormatError(
                    "SESSION_NOT_ACTIVE",
                    $"Session '{sessionId}' cannot be resumed because its status is '{status}'.",
                    new { session_id = sessionId, status });
            }

            var persistedLink = ReadCoordinationLink(metadata.ExtraFields);
            if (HasCoordinationMetadata(metadata.ExtraFields) && persistedLink is null)
            {
                return FormatError(
                    "COORDINATION_LINK_INVALID",
                    "The session contains partial coordination metadata and cannot be resumed safely.",
                    new { session_id = sessionId });
            }

            var linkError = ValidateRequestedCoordinationLink(persistedLink, requestedLink);
            if (linkError is not null)
            {
                return linkError;
            }

            var currentLink = persistedLink ?? requestedLink;
            var preconditionError = ValidateCoordinationLinkPreconditions(
                session,
                preconditions,
                persistedLink,
                requestedLink);
            if (preconditionError is not null)
            {
                return preconditionError;
            }

            var now = _timeProvider.GetUtcNow();
            document.ReplaceBody(
                document.Body.TrimEnd() +
                $"\n\n---\n\n## Session resumed — {FormatUtc(now)}\n");
            if (_vaultConfig.MaintainUpdated)
            {
                document.SetDate("updated", DateOnly.FromDateTime(now.UtcDateTime), "modified");
            }

            await WriteSessionTextAsync(
                session.Note.FilePath,
                document.Serialize(),
                preconditions,
                cancellationToken);
            await _vault.SynchronizeFileReindexAsync(session.Note.FilePath).WaitAsync(cancellationToken);

            var coordinationResult = new CoordinationLinkResult(currentLink, null);
            if (persistedLink is null && requestedLink is not null)
            {
                coordinationResult = await TryCreateCoordinationLinkAsync(
                    requestedLink,
                    sessionId,
                    GetString(metadata.ExtraFields, "parent_session_id") ?? session.ParentSessionId ?? string.Empty,
                    session.Note.VaultRelativePath,
                    session.Project ?? project,
                    session.Agent ?? "unknown",
                    session.ClientName ?? "unknown",
                    cancellationToken);
                if (coordinationResult.Link is not null)
                {
                    var linkWarning = await PersistCoordinationLinkAsync(
                        session.Note.FilePath,
                        coordinationResult.Link,
                        cancellationToken);
                    coordinationResult = coordinationResult with
                    {
                        Warning = CombineWarnings(coordinationResult.Warning, linkWarning),
                        Link = linkWarning is null ? coordinationResult.Link : null,
                    };
                }
            }

            return FormatSuccess(
                "resumed",
                sessionId,
                session.Note.VaultRelativePath,
                session.Project ?? string.Empty,
                session.Agent ?? "unknown",
                session.ClientName ?? "unknown",
                session.StartedAt,
                coordinationResult.Warning,
                coordinationResult.Link);
        }
        finally
        {
            gate.Release();
        }
    }

    private SessionResolution ResolveSession(
        string sessionId,
        string sessionNote,
        string project,
        string agent,
        string? clientName)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var matches = FindSessionsById(sessionId.Trim());
            return matches.Count switch
            {
                0 => new(null, FormatError(
                    "SESSION_NOT_FOUND",
                    $"No work session exists with session_id '{sessionId}'.",
                    new { session_id = sessionId })),
                1 => new(matches[0], null),
                _ => new(null, FormatAmbiguity(
                    matches,
                    $"Duplicate session_id '{sessionId}' was found.")),
            };
        }

        if (!string.IsNullOrWhiteSpace(sessionNote))
        {
            var note = NoteHelpers.ResolveNote(sessionNote, _vault);
            return note is not null && IsSessionNote(note)
                ? new(ReadSession(note), null)
                : new(null, FormatError(
                    "SESSION_NOT_FOUND",
                    $"Session note not found: '{sessionNote}'.",
                    new { session_note = sessionNote }));
        }

        if (!string.IsNullOrWhiteSpace(project) &&
            ProjectWorkspaceService.ValidateProjectName(project) is { } projectError)
        {
            return new(null, projectError);
        }

        var candidates = FindActiveSessions();
        if (!string.IsNullOrWhiteSpace(project))
        {
            var folder = _workspace.ToVaultRelative(_workspace.GetSubfolder(project, "sessions"));
            candidates = candidates.Where(session =>
                    string.Equals(session.Project, project, StringComparison.OrdinalIgnoreCase) ||
                    session.Note.VaultRelativePath.StartsWith(
                        folder.TrimEnd('/') + "/",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var normalizedAgent = string.IsNullOrWhiteSpace(agent)
            ? NormalizeAgentName(clientName)
            : NormalizeAgentName(agent);
        var hasIdentity = !string.IsNullOrWhiteSpace(agent) || !string.IsNullOrWhiteSpace(clientName);
        if (hasIdentity)
        {
            candidates = candidates.Where(session =>
                    (!string.IsNullOrWhiteSpace(clientName) &&
                     string.Equals(session.ClientName, clientName, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(session.Agent, normalizedAgent, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return candidates.Count switch
        {
            0 => new(null, FormatError(
                "NO_ACTIVE_SESSION",
                "No active work session matches the current selector, project, and client/agent identity.",
                new
                {
                    project = string.IsNullOrWhiteSpace(project) ? null : project,
                    agent = hasIdentity ? normalizedAgent : null,
                    client_name = clientName,
                })),
            1 => new(candidates[0], null),
            _ => new(null, FormatAmbiguity(
                candidates,
                "Multiple active work sessions match. Pass session_id explicitly.")),
        };
    }

    private async Task<string> CloseAsync(
        SessionDescriptor selected,
        string summary,
        VaultMutationPreconditions? preconditions,
        CancellationToken cancellationToken)
    {
        var gate = GetLock(selected);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var document = FrontmatterDocument.Parse(
                await _fileSystem.ReadAllTextAsync(selected.Note.FilePath, cancellationToken));
            var metadata = document.ToFrontmatter();
            var status = metadata.Status ?? "unknown";
            var sessionId = GetString(metadata.ExtraFields, "session_id") ?? selected.SessionId;
            if (!status.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                return FormatError(
                    "SESSION_ALREADY_CLOSED",
                    $"Session '{sessionId ?? selected.Note.VaultRelativePath}' is not active; current status is '{status}'.",
                    new { session_id = sessionId, path = selected.Note.VaultRelativePath, status });
            }

            var persistedLink = ReadCoordinationLink(metadata.ExtraFields);
            if (HasCoordinationMetadata(metadata.ExtraFields) && persistedLink is null)
            {
                return FormatError(
                    "COORDINATION_LINK_INVALID",
                    "The session contains partial coordination metadata and cannot be closed safely.",
                    new { session_id = sessionId });
            }

            if (selected.Coordination is not null && persistedLink is null)
            {
                return FormatError(
                    "COORDINATION_LINK_MISMATCH",
                    "The session's coordination metadata changed since it was selected; re-read the session and retry.",
                    new { session_id = sessionId });
            }

            var preconditionError = ValidateCoordinatedPreconditions(
                selected,
                preconditions,
                persistedLink);
            if (preconditionError is not null)
            {
                return preconditionError;
            }

            var startedAt = ParseUtc(GetString(metadata.ExtraFields, "started_at")) ?? selected.StartedAt;
            var now = _timeProvider.GetUtcNow();
            if (now < startedAt)
            {
                now = startedAt;
            }

            var modified = _vault.GetAllNotes()
                .Where(note =>
                    note.LastModified.ToUniversalTime() > startedAt &&
                    note.LastModified.ToUniversalTime() <= now &&
                    !note.FilePath.Equals(selected.Note.FilePath, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(note => note.LastModified)
                .ToList();

            var body = document.Body.TrimEnd() + BuildEndBlock(now, startedAt, summary, modified);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                body = WriteSummarySection(body, summary);
            }
            document.ReplaceBody(body);
            document.SetString("status", "done");
            document.SetString("ended_at", FormatUtc(now));
            if (_vaultConfig.MaintainUpdated)
            {
                document.SetDate("updated", DateOnly.FromDateTime(now.UtcDateTime), "modified");
            }

            await WriteSessionTextAsync(
                selected.Note.FilePath,
                document.Serialize(),
                preconditions,
                cancellationToken);
            await _vault.SynchronizeFileReindexAsync(selected.Note.FilePath).WaitAsync(cancellationToken);
            var payload = JsonSerializer.Serialize(new
            {
                action = "closed",
                session_id = sessionId,
                path = selected.Note.VaultRelativePath,
                started_at = FormatUtc(startedAt),
                ended_at = FormatUtc(now),
                duration_seconds = (long)(now - startedAt).TotalSeconds,
                notes_touched = modified.Count,
                coordination = ToCoordinationPayload(persistedLink),
            });
            return $"[ok] Session closed: {selected.Note.VaultRelativePath}\n{payload}";
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<string> WriteNewSessionFileAsync(
        string directory,
        string preferredName,
        string fallbackName,
        string content,
        CancellationToken cancellationToken)
    {
        if (_mutations is null)
        {
            return await _fileSystem.WriteNewSessionFileAsync(
                directory,
                preferredName,
                fallbackName,
                content,
                cancellationToken);
        }

        foreach (var name in new[] { preferredName, fallbackName })
        {
            var path = Path.Combine(directory, name);
            try
            {
                await _mutations.CreateTextAsync(path, content, cancellationToken: cancellationToken);
                return path;
            }
            catch (VaultMutationException exception) when (exception.Code == "INVALID_ARGUMENT")
            {
                // Another process claimed the preferred name; use the UUID-derived fallback.
            }
        }

        throw new IOException("Could not allocate a unique work-session filename.");
    }

    private async Task WriteSessionTextAsync(
        string path,
        string content,
        VaultMutationPreconditions? preconditions,
        CancellationToken cancellationToken)
    {
        if (_mutations is null)
        {
            await _fileSystem.WriteAtomicallyAsync(path, content, cancellationToken);
            return;
        }

        await _mutations.WriteTextAsync(path, content, preconditions, cancellationToken);
    }

    private async Task<string> BuildProjectBodyAsync(
        string title,
        string goal,
        string project,
        string projectLink,
        string agent,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        NoteHelpers.ExpandTemplateVariables(
            await _workspace.ResolveTemplateAsync("session").WaitAsync(cancellationToken),
            new Dictionary<string, string>
            {
                ["goal"] = string.IsNullOrWhiteSpace(goal) ? "_(not specified)_" : goal,
                ["agent"] = agent,
                ["project"] = project,
                ["project_link"] = projectLink,
                ["date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["time"] = now.ToString("HH:mm", CultureInfo.InvariantCulture),
                ["started_at"] = FormatUtc(now),
            },
            title,
            now);

    private async Task<string> BuildGlobalBodyAsync(
        string title,
        string goal,
        string folder,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var variables = new Dictionary<string, string>
        {
            ["goal"] = string.IsNullOrWhiteSpace(goal) ? string.Empty : goal,
            ["date"] = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["time"] = now.ToString("HH:mm", CultureInfo.InvariantCulture),
            ["started_at"] = FormatUtc(now),
        };
        return await TryRenderFolderTemplateAsync(
            folder,
            variables,
            title,
            cancellationToken)
            ?? BuildDefaultBody(title, goal, now);
    }

    private void AppendActivity(
        StringBuilder sb,
        SessionDescriptor session,
        DateTimeOffset end)
    {
        var notes = _vault.GetAllNotes()
            .Where(note =>
                note.LastModified.ToUniversalTime() >= session.StartedAt &&
                note.LastModified.ToUniversalTime() <= end &&
                !note.FilePath.Equals(session.Note.FilePath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(note => note.LastModified)
            .ToList();
        if (notes.Count == 0)
        {
            sb.AppendLine("  Activity: no notes were modified during this session.");
            return;
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"  Activity: {notes.Count} note(s) modified during session '{session.Note.Name}':");
        foreach (var note in notes)
        {
            sb.AppendLine(
                CultureInfo.InvariantCulture,
                $"    - {note.VaultRelativePath} (modified {FormatDuration(note.LastModified.ToUniversalTime() - session.StartedAt)} " +
                $"after session start)");
        }
    }
}
