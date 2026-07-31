using System.Text;
using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Workflow orchestration for per-project engineering documents: architecture decision records
/// (ADRs), bug logs, implementation plans, knowledge notes, backlog ideas, project context
/// re-reading, project discovery, and engineering template management. Documents live in the
/// vault so humans edit them from Obsidian and agents re-read them through
/// <see cref="GetProjectContextAsync"/>.
/// </summary>
internal sealed class ProjectDocumentService : IProjectDocumentService
{
    private static readonly Dictionary<string, string[]> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["decision"] = ["proposed", "accepted", "superseded"],
        ["bug"] = ["open", "fixed"],
        ["plan"] = ["draft", "active", "done"],
        ["idea"] = ["proposed", "adopted", "discarded"],
    };

    // Aliases accepted by get_project_context's `types` filter, mapped to subfolder keys.
    private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["adr"] = "decisions",
        ["decision"] = "decisions",
        ["decisions"] = "decisions",
        ["bug"] = "bugs",
        ["bugs"] = "bugs",
        ["plan"] = "plans",
        ["plans"] = "plans",
        ["knowledge"] = "knowledge",
        ["session"] = "sessions",
        ["sessions"] = "sessions",
        ["daily"] = "daily",
        ["ticket"] = "tickets",
        ["tickets"] = "tickets",
        ["idea"] = "backlog",
        ["backlog"] = "backlog",
    };

    private readonly VaultIndexService _vault;
    private readonly KiokuConfiguration _config;
    private readonly VaultConfigService _vaultConfig;
    private readonly ProjectWorkspaceService _workspace;
    private readonly ObsidianBridgeService _bridge;
    private readonly IProjectDocumentFileSystem _fileSystem;
    private readonly IVaultMutationService? _mutations;

    public ProjectDocumentService(
        VaultIndexService vault,
        KiokuConfiguration config,
        VaultConfigService vaultConfig,
        ProjectWorkspaceService workspace,
        ObsidianBridgeService bridge,
        IProjectDocumentFileSystem fileSystem,
        IVaultMutationService? mutations = null)
    {
        _vault = vault;
        _config = config;
        _vaultConfig = vaultConfig;
        _workspace = workspace;
        _bridge = bridge;
        _fileSystem = fileSystem;
        _mutations = mutations;
    }

    public async Task<string> CreateProjectDocAsync(
        string docType,
        string project,
        string title,
        string status,
        string tags,
        string context,
        string decision,
        string consequences,
        string alternatives,
        string symptom,
        string rootCause,
        string fix,
        string relatedFiles,
        string objective,
        string steps,
        string ticket,
        string content,
        string description,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedType = docType.Trim().ToLowerInvariant();
        if (normalizedType is not ("adr" or "bug" or "plan" or "backlog" or "knowledge"))
        {
            return $"[error] Unknown document type '{docType}'. Valid types: adr, bug, plan, backlog, knowledge.";
        }

        var effectiveStatus = normalizedType switch
        {
            "adr" => string.IsNullOrWhiteSpace(status) ? "accepted" : status,
            "bug" => string.IsNullOrWhiteSpace(status) ? "fixed" : status,
            "plan" => string.IsNullOrWhiteSpace(status) ? "draft" : status,
            "backlog" => string.IsNullOrWhiteSpace(status) ? "proposed" : status,
            _ => "active",
        };
        var statusType = normalizedType switch
        {
            "adr" => "decision",
            "backlog" => "idea",
            _ => normalizedType,
        };
        if (normalizedType != "knowledge" && ValidateStatus(statusType, effectiveStatus) is { } statusError)
        {
            return statusError;
        }

        if (normalizedType == "knowledge")
        {
            if (!string.IsNullOrWhiteSpace(status) && !status.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                return $"[error] Invalid status '{status}' for a knowledge. Valid options: active.";
            }

            if (string.IsNullOrWhiteSpace(project))
            {
                return await CreateGeneralKnowledgeAsync(title, content, tags, preconditions, cancellationToken);
            }

            return await CreateDocAsync(
                project, "knowledge", NoteHelpers.SanitizeFileName(title), "knowledge", "active", "knowledge", tags,
                "knowledge", title, new Dictionary<string, string> { ["content"] = content },
                 preconditions: preconditions,
                 cancellationToken: cancellationToken);
        }

        if (normalizedType == "adr")
        {
            if (ProjectWorkspaceService.ValidateProjectName(project) is { } nameError)
            {
                return nameError;
            }

            // Allocation and write share the lock so concurrent ADR calls cannot reuse a number.
            using var adrLock = await _workspace.AcquireAdrLockAsync(project).WaitAsync(cancellationToken);
            var number = _workspace.GetNextAdrNumber(project);
            return await CreateDocAsync(
                project, "decisions", $"ADR-{number:D4}-{NoteHelpers.SanitizeFileName(title)}", "decision", effectiveStatus,
                "adr", tags, "adr", title,
                new Dictionary<string, string>
                {
                    ["number"] = number.ToString("D4"),
                    ["context"] = context,
                    ["decision"] = decision,
                    ["consequences"] = consequences,
                    ["alternatives"] = string.IsNullOrWhiteSpace(alternatives) ? "_(none recorded)_" : alternatives,
                },
                new Dictionary<string, string> { ["adr"] = $"\"{number:D4}\"" }, [$"ADR-{number:D4}"],
                 preconditions: preconditions,
                 cancellationToken: cancellationToken);
        }

        if (normalizedType == "bug")
        {
            var relatedList = string.IsNullOrWhiteSpace(relatedFiles)
                ? "_(none recorded)_"
                : string.Join("\n", relatedFiles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(f => $"- `{f}`"));
            return await CreateDocAsync(
                project, "bugs", $"BUG-{DateTime.Now:yyyy-MM-dd}-{NoteHelpers.SanitizeFileName(title)}", "bug", effectiveStatus,
                "bug", tags, "bug", title,
                new Dictionary<string, string>
                {
                    ["symptom"] = symptom,
                    ["root_cause"] = rootCause,
                    ["fix"] = fix,
                    ["related_files"] = relatedList,
                },
                 preconditions: preconditions,
                 cancellationToken: cancellationToken);
        }

        if (normalizedType == "plan")
        {
            return await CreateDocAsync(
                project, "plans", $"PLAN-{DateTime.Now:yyyy-MM-dd}-{NoteHelpers.SanitizeFileName(title)}", "plan", effectiveStatus,
                "plan", tags, "plan", title,
                new Dictionary<string, string>
                {
                    ["objective"] = objective,
                    ["steps"] = steps,
                    ["ticket"] = string.IsNullOrWhiteSpace(ticket) ? "_(none)_" : $"[[{ticket}]]",
                },
                string.IsNullOrWhiteSpace(ticket) ? null : new Dictionary<string, string> { ["ticket"] = $"\"[[{ticket}]]\"" },
                 preconditions: preconditions,
                 cancellationToken: cancellationToken);
        }

        return await CreateDocAsync(
            project, "backlog", NoteHelpers.SanitizeFileName(title), "idea", effectiveStatus,
            "idea", tags, "idea", title, new Dictionary<string, string> { ["description"] = description },
            preconditions: preconditions,
            cancellationToken: cancellationToken);
    }

    public Task<string> RecordAdrAsync(
        string project,
        string title,
        string context,
        string decision,
        string consequences,
        string alternatives,
        string status,
        string tags,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default) =>
        CreateProjectDocAsync(
            "adr", project, title, status, tags, context, decision, consequences, alternatives,
            symptom: "", rootCause: "", fix: "", relatedFiles: "", objective: "", steps: "", ticket: "",
             content: "", description: "", preconditions: preconditions, cancellationToken: cancellationToken);

    public Task<string> LogBugAsync(
        string project,
        string title,
        string symptom,
        string rootCause,
        string fix,
        string status,
        string relatedFiles,
        string tags,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default) =>
        CreateProjectDocAsync(
            "bug", project, title, status, tags,
            context: "", decision: "", consequences: "", alternatives: "",
            symptom: symptom, rootCause: rootCause, fix: fix, relatedFiles: relatedFiles,
             objective: "", steps: "", ticket: "", content: "", description: "",
             preconditions: preconditions, cancellationToken: cancellationToken);

    public Task<string> CreatePlanAsync(
        string project,
        string title,
        string objective,
        string steps,
        string status,
        string ticket,
        string tags,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default) =>
        CreateProjectDocAsync(
            "plan", project, title, status, tags,
            context: "", decision: "", consequences: "", alternatives: "",
            symptom: "", rootCause: "", fix: "", relatedFiles: "",
             objective: objective, steps: steps, ticket: ticket, content: "", description: "",
             preconditions: preconditions, cancellationToken: cancellationToken);

    public Task<string> AddKnowledgeAsync(
        string title,
        string content,
        string project,
        string tags,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default) =>
        CreateProjectDocAsync(
            "knowledge", project: project, title: title, status: "", tags: tags,
            context: "", decision: "", consequences: "", alternatives: "",
            symptom: "", rootCause: "", fix: "", relatedFiles: "",
             objective: "", steps: "", ticket: "", content: content, description: "",
             preconditions: preconditions, cancellationToken: cancellationToken);

    public Task<string> AddBacklogItemAsync(
        string project,
        string title,
        string description,
        string tags,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default) =>
        CreateProjectDocAsync(
            "backlog", project: project, title: title, status: "", tags: tags,
            context: "", decision: "", consequences: "", alternatives: "",
            symptom: "", rootCause: "", fix: "", relatedFiles: "",
             objective: "", steps: "", ticket: "", content: "", description: description,
             preconditions: preconditions, cancellationToken: cancellationToken);

    public async Task<string> GetProjectContextAsync(
        string project,
        bool includeContent,
        string types,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ProjectWorkspaceService.ValidateProjectName(project) is { } nameError)
        {
            return nameError;
        }

        var projectFolder = _workspace.GetProjectFolder(project);
        if (!_fileSystem.DirectoryExists(projectFolder))
        {
            return $"[error] Project '{project}' not found under '{_workspace.ProjectsRootRelative}/'. " +
                   "Use list_projects to see existing projects or setup_agent_workflow to create one.";
        }

        var typeFilter = ParseTypeFilter(types);
        if (typeFilter is null)
        {
            return $"[error] Unknown type in filter '{types}'. Valid types: {string.Join(", ", TypeAliases.Keys.Distinct())}.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Project context: {project}");
        sb.AppendLine();
        sb.AppendLine($"**Folder:** {_workspace.ToVaultRelative(projectFolder)}/");
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        // Project MOC verbatim: it is the human-curated overview. Named after the leaf segment,
        // not the full (possibly grouped) identifier — same convention as EnsureProjectScaffoldAsync.
        var mocPath = Path.Combine(projectFolder, $"{Path.GetFileName(projectFolder)}.md");
        if (_fileSystem.FileExists(mocPath))
        {
            sb.AppendLine("## Project overview (MOC)");
            sb.AppendLine();
            sb.AppendLine(RenderProjectOverview(await _fileSystem.ReadAllTextAsync(mocPath, cancellationToken)).Trim());
            sb.AppendLine();
        }

        // Recent session summaries: the handoff from previous agents.
        if (typeFilter.Contains("sessions"))
        {
            var sessions = _workspace.EnumerateProjectDocs(project, "sessions").Take(limit).ToList();
            if (sessions.Count > 0)
            {
                sb.AppendLine($"## Recent sessions ({sessions.Count})");
                sb.AppendLine();
                foreach (var file in sessions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var raw = await _fileSystem.ReadAllTextAsync(file.FullName, cancellationToken);
                    var meta = FrontmatterParser.Parse(raw);
                    sb.AppendLine($"### [{meta.Status ?? "unknown"}] {Path.GetFileNameWithoutExtension(file.Name)} — {_workspace.ToVaultRelative(file.FullName)}");
                    var summary = ExtractSection(raw, "## Summary");
                    sb.AppendLine(string.IsNullOrWhiteSpace(summary) ? "_(no summary recorded)_" : summary.Trim());
                    sb.AppendLine();
                }
            }
        }

        var sections = new (string Key, string Heading)[]
        {
            ("decisions", "Decisions (ADRs)"),
            ("bugs", "Bugs"),
            ("plans", "Plans"),
            ("tickets", "Tickets"),
            ("backlog", "Backlog"),
            ("knowledge", "Knowledge"),
            ("daily", "Daily"),
        };

        var fullContent = new StringBuilder();

        foreach (var (key, heading) in sections)
        {
            if (!typeFilter.Contains(key))
            {
                continue;
            }

            var docs = _workspace.EnumerateProjectDocs(project, key);
            sb.AppendLine($"## {heading} ({docs.Count})");
            if (docs.Count == 0)
            {
                sb.AppendLine("_(none)_");
                sb.AppendLine();
                continue;
            }

            foreach (var file in docs.Take(limit))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var raw = await _fileSystem.ReadAllTextAsync(file.FullName, cancellationToken);
                var meta = FrontmatterParser.Parse(raw);
                var relPath = _workspace.ToVaultRelative(file.FullName);
                var dateStr = meta.Date?.ToString("yyyy-MM-dd") ?? file.LastWriteTimeUtc.ToString("yyyy-MM-dd");
                var summaryLine = FirstBodyLine(raw);

                sb.Append($"- [{meta.Status ?? "-"}] {Path.GetFileNameWithoutExtension(file.Name)} — {relPath} ({dateStr})");
                if (!string.IsNullOrWhiteSpace(summaryLine))
                {
                    sb.Append($" — {summaryLine}");
                }

                sb.AppendLine();

                if (includeContent)
                {
                    fullContent.AppendLine($"### {relPath}");
                    fullContent.AppendLine();
                    fullContent.AppendLine(raw.Trim());
                    fullContent.AppendLine();
                }
            }

            if (docs.Count > limit)
            {
                sb.AppendLine($"_(+{docs.Count - limit} more — raise `limit` to see them)_");
            }

            sb.AppendLine();
        }

        if (includeContent && fullContent.Length > 0)
        {
            sb.AppendLine("## Full document contents");
            sb.AppendLine();
            sb.Append(fullContent);
        }

        sb.AppendLine("_Read any single document with `read_note <path>`; change statuses with `update_frontmatter`._");
        return sb.ToString();
    }

    public Task<string> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_fileSystem.DirectoryExists(_workspace.ProjectsRoot))
        {
            return Task.FromResult(
                $"[info] No projects folder found at '{_workspace.ProjectsRootRelative}/'. " +
                "Use setup_agent_workflow to create the structure.");
        }

        var projects = _workspace.DiscoverProjects();
        if (projects.Count == 0)
        {
            return Task.FromResult(
                $"[info] No projects yet under '{_workspace.ProjectsRootRelative}/'. " +
                "Use setup_agent_workflow with a project name, or create_project_doc to create one.");
        }

        var sb = new StringBuilder($"[ok] {projects.Count} project(s) under '{_workspace.ProjectsRootRelative}/':\n\n");
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var counts = ProjectWorkspaceService.SubfolderKeys
                .Select(key => (key, count: _workspace.EnumerateProjectDocs(project, key).Count))
                .Where(t => t.count > 0)
                .Select(t => $"{t.key}: {t.count}")
                .ToList();

            var projectDir = _workspace.GetProjectFolder(project);
            var lastModified = _fileSystem.EnumerateMarkdownFilesRecursive(projectDir)
                .Select(f => _fileSystem.GetFileLastWriteTimeUtc(f))
                .DefaultIfEmpty(_fileSystem.GetDirectoryLastWriteTimeUtc(projectDir))
                .Max();

            sb.Append($"- **{project}**");
            sb.Append(counts.Count > 0 ? $" — {string.Join(", ", counts)}" : " — empty");
            sb.AppendLine($" (last modified {lastModified:yyyy-MM-dd})");
        }

        return Task.FromResult(sb.ToString());
    }

    public Task<string> ListEngineeringTemplatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sb = new StringBuilder($"[ok] {ProjectWorkspaceService.TemplateKeys.Length} engineering template(s):\n\n");

        foreach (var typeKey in ProjectWorkspaceService.TemplateKeys)
        {
            var overridePath = _workspace.GetVaultTemplatePath(typeKey);
            var isOverride = overridePath is not null && _fileSystem.FileExists(overridePath);
            var vars = ProjectWorkspaceService.SupportedVariablesFor(typeKey);

            sb.Append($"  **{typeKey}** — ");
            sb.Append(isOverride
                ? $"override at {_workspace.ToVaultRelative(overridePath!)}"
                : "using embedded default");
            sb.AppendLine($" — variables: {string.Join(", ", vars.Select(v => "{{" + v + "}}"))}");
        }

        return Task.FromResult(sb.ToString());
    }

    public async Task<string> GetEngineeringTemplateAsync(
        string typeKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ProjectWorkspaceService.TemplateKeys.Contains(typeKey, StringComparer.OrdinalIgnoreCase))
        {
            return $"[error] Unknown template type '{typeKey}'. Valid types: {string.Join(", ", ProjectWorkspaceService.TemplateKeys)}.";
        }

        var overridePath = _workspace.GetVaultTemplatePath(typeKey);
        var isOverride = overridePath is not null && _fileSystem.FileExists(overridePath);
        var content = await _workspace.ResolveTemplateAsync(typeKey).WaitAsync(cancellationToken);
        var vars = ProjectWorkspaceService.SupportedVariablesFor(typeKey);

        var sb = new StringBuilder($"[ok] Template '{typeKey}' ({(isOverride ? $"override: {_workspace.ToVaultRelative(overridePath!)}" : "embedded default")}):\n\n");
        sb.AppendLine($"Supported variables: {string.Join(", ", vars.Select(v => "{{" + v + "}}"))}");
        sb.AppendLine();
        sb.AppendLine("```markdown");
        sb.AppendLine(content);
        sb.AppendLine("```");

        return sb.ToString();
    }

    public async Task<string> SetEngineeringTemplateAsync(
        string typeKey,
        string content,
        bool resetToDefault,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ProjectWorkspaceService.TemplateKeys.Contains(typeKey, StringComparer.OrdinalIgnoreCase))
        {
            return $"[error] Unknown template type '{typeKey}'. Valid types: {string.Join(", ", ProjectWorkspaceService.TemplateKeys)}.";
        }

        if (resetToDefault)
        {
            var existing = _workspace.GetVaultTemplatePath(typeKey);
            if (existing is not null && _fileSystem.FileExists(existing))
            {
                await DeleteFileAsync(existing, preconditions, cancellationToken);
                return $"[ok] Reverted '{typeKey}' to the embedded default (removed {_workspace.ToVaultRelative(existing)}).";
            }

            return $"[ok] '{typeKey}' already uses the embedded default (no override to remove).";
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return "[error] The 'content' parameter cannot be empty unless reset_to_default=true.";
        }

        var targetDir = Path.Combine(_workspace.ResolveTemplatesFolderOrDefault(), "kioku");
        _fileSystem.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, $"{typeKey}.md");

        await WriteTextAsync(
            targetPath,
            content,
            requireAbsent: false,
            preconditions,
            cancellationToken);

        var recognized = new HashSet<string>(ProjectWorkspaceService.SupportedVariablesFor(typeKey), StringComparer.OrdinalIgnoreCase);
        var unknownVars = ProjectWorkspaceService.ExtractTemplateVariableNames(content)
            .Where(v => !recognized.Contains(v))
            .ToList();

        var result = $"[ok] Template '{typeKey}' saved: {_workspace.ToVaultRelative(targetPath)}";
        if (unknownVars.Count > 0)
        {
            result += $"\n   [warning] not a recognized variable for '{typeKey}' and will be left literal: " +
                      string.Join(", ", unknownVars.Select(v => "{{" + v + "}}"));
        }

        return result;
    }

    public async Task<string> SetupAgentWorkflowAsync(
        string project,
        bool writeTemplates,
        bool patchConfig,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var created = new List<string>();
        var skipped = new List<string>();

        // Root folders
        foreach (var root in new[] { _workspace.ProjectsRoot, _workspace.KnowledgeRoot })
        {
            var rel = _workspace.ToVaultRelative(root) + "/";
            if (_fileSystem.DirectoryExists(root))
            {
                skipped.Add(rel);
            }
            else
            {
                _fileSystem.CreateDirectory(root);
                created.Add(rel);
            }
        }

        // Templates — runs before the project scaffold below so that, on first use, the files
        // already exist on disk when the scaffold step tries to register them in Templater's
        // own folder-template settings (Templater can't point at an embedded resource).
        if (writeTemplates)
        {
            var (templatesCreated, templatesSkipped) = await _workspace.EnsureEngineeringTemplatesOnDiskAsync().WaitAsync(cancellationToken);
            created.AddRange(templatesCreated);
            skipped.AddRange(templatesSkipped);
        }

        // Project scaffold
        if (!string.IsNullOrWhiteSpace(project))
        {
            if (ProjectWorkspaceService.ValidateProjectName(project) is { } nameError)
            {
                return nameError;
            }

            var scaffolded = await _workspace.EnsureProjectScaffoldAsync(project).WaitAsync(cancellationToken);
            if (scaffolded.Count > 0)
            {
                created.AddRange(scaffolded);
            }
            else
            {
                skipped.Add($"{_workspace.ToVaultRelative(_workspace.GetProjectFolder(project))}/ (already scaffolded)");
            }
        }

        // Config reference block
        if (patchConfig)
        {
            var configPath = Path.Combine(_config.VaultPath, ".kioku", "config.yml");
            var patchResult = await AppendConfigReferenceAsync(configPath, cancellationToken);
            (patchResult.Created ? created : skipped).Add(patchResult.Message);
        }

        var sb = new StringBuilder("[ok] Agent workflow setup complete.\n");
        sb.AppendLine($"\nCreated ({created.Count}):");
        sb.AppendLine(created.Count > 0 ? string.Join("\n", created.Select(c => $"  - {c}")) : "  (nothing — everything already existed)");
        if (skipped.Count > 0)
        {
            sb.AppendLine($"\nSkipped, already present ({skipped.Count}):");
            sb.AppendLine(string.Join("\n", skipped.Select(s => $"  - {s}")));
        }

        sb.AppendLine("\nEdit the templates under the templates folder ('kioku/' subfolder) in Obsidian to customize document bodies.");
        return sb.ToString();
    }

    // Private helpers

    private async Task<string> CreateGeneralKnowledgeAsync(
        string title,
        string content,
        string tags,
        VaultMutationPreconditions? preconditions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "[error] The 'title' parameter cannot be empty.";
        }

        _fileSystem.CreateDirectory(_workspace.KnowledgeRoot);
        var filePath = Path.Combine(_workspace.KnowledgeRoot, NoteHelpers.SanitizeFileName(title) + ".md");
        if (_fileSystem.FileExists(filePath) && string.IsNullOrWhiteSpace(preconditions?.MutationId))
        {
            return $"[error] Note already exists: '{_workspace.ToVaultRelative(filePath)}'. Use edit_note to modify it.";
        }

        var body = NoteHelpers.ExpandTemplateVariables(
            await _workspace.ResolveTemplateAsync("knowledge").WaitAsync(cancellationToken),
            new Dictionary<string, string> { ["content"] = content },
            noteTitle: title);
        var relFolder = _workspace.KnowledgeRootRelative;
        var mergedTags = NoteHelpers.MergeTagsWithInheritance(
            NoteHelpers.ParseTags(tags).Prepend("knowledge"),
            _vaultConfig.GetInheritedTags(relFolder),
            _vaultConfig.ExcludeFromTags);
        var frontmatter = NoteHelpers.BuildFrontmatter(
            mergedTags,
            type: "knowledge",
            status: "active",
            date: DateOnly.FromDateTime(DateTime.Now),
                domain: _vaultConfig.GetDomainForFolder(relFolder),
                cssClasses: ["kioku-knowledge"],
                updated: _vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(DateTime.Today) : null);

        await WriteTextAsync(
            filePath,
            frontmatter + "\n" + body,
            requireAbsent: true,
            preconditions,
            cancellationToken: cancellationToken);
        await _vault.SynchronizeFileReindexAsync(filePath).WaitAsync(cancellationToken);

        var vaultRelPath = _workspace.ToVaultRelative(filePath);
        var evalResult = await _bridge.EvaluateTemplaterInPlaceAsync(body, vaultRelPath, cancellationToken);
        if (evalResult.Applied)
        {
            await _vault.SynchronizeFileReindexAsync(filePath).WaitAsync(cancellationToken);
        }

        var result = $"[ok] Knowledge note created: {vaultRelPath}";
        return evalResult.Warning is null ? result : $"{result}\n   [warning] {evalResult.Warning}";
    }

    private async Task<string> CreateDocAsync(
        string project,
        string subfolderKey,
        string fileName,
        string type,
        string status,
        string baseTag,
        string userTags,
        string templateKey,
        string title,
        Dictionary<string, string> variables,
        Dictionary<string, string>? extraFields = null,
        IEnumerable<string>? aliases = null,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default)
    {
        if (ProjectWorkspaceService.ValidateProjectName(project) is { } nameError)
        {
            return nameError;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return "[error] The 'title' parameter cannot be empty.";
        }

        var scaffolded = await _workspace.EnsureProjectScaffoldAsync(project).WaitAsync(cancellationToken);

        var folder = _workspace.GetSubfolder(project, subfolderKey);
        var filePath = Path.Combine(folder, fileName + ".md");
        if (_fileSystem.FileExists(filePath) && string.IsNullOrWhiteSpace(preconditions?.MutationId))
        {
            return $"[error] Note already exists: '{_workspace.ToVaultRelative(filePath)}'. Use edit_note to modify it.";
        }

        var projectLink = $"[[{ProjectWorkspaceService.ProjectLeafName(project)}]]";
        variables["project"] = project;
        variables["project_link"] = projectLink;
        var body = NoteHelpers.ExpandTemplateVariables(
            await _workspace.ResolveTemplateAsync(templateKey).WaitAsync(cancellationToken), variables, noteTitle: title);

        var relFolder = _workspace.ToVaultRelative(folder);
        var mergedTags = NoteHelpers.MergeTagsWithInheritance(
            NoteHelpers.ParseTags(userTags).Prepend(baseTag),
            _vaultConfig.GetInheritedTags(relFolder),
            _vaultConfig.ExcludeFromTags);

        var fields = new Dictionary<string, string>
        {
            ["project"] = project,
            ["project_link"] = $"\"{projectLink}\"",
        };
        if (extraFields is not null)
        {
            foreach (var (k, v) in extraFields)
            {
                fields[k] = v;
            }
        }

        var frontmatter = NoteHelpers.BuildFrontmatter(
            mergedTags,
            type: type,
            status: status,
            date: DateOnly.FromDateTime(DateTime.Now),
            domain: _vaultConfig.GetDomainForFolder(relFolder),
            aliases: aliases,
            cssClasses: [$"kioku-{baseTag}"],
            updated: _vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(DateTime.Today) : null,
            extraFields: fields);

        await WriteTextAsync(
            filePath,
            frontmatter + "\n" + body,
            requireAbsent: true,
            preconditions,
            cancellationToken: cancellationToken);
        await _vault.SynchronizeFileReindexAsync(filePath).WaitAsync(cancellationToken);

        var vaultRelPath = _workspace.ToVaultRelative(filePath);
        var evalResult = await _bridge.EvaluateTemplaterInPlaceAsync(body, vaultRelPath, cancellationToken);
        if (evalResult.Applied)
        {
            await _vault.SynchronizeFileReindexAsync(filePath).WaitAsync(cancellationToken);
        }

        var sb = new StringBuilder($"[ok] {char.ToUpperInvariant(type[0]) + type[1..]} note created: {vaultRelPath}");
        if (scaffolded.Count > 0)
        {
            sb.Append($"\n   Scaffolded project '{project}' ({scaffolded.Count} new folder(s)/note(s)).");
        }

        if (evalResult.Warning is not null)
        {
            sb.Append($"\n   [warning] {evalResult.Warning}");
        }

        return sb.ToString();
    }

    private static string? ValidateStatus(string type, string status)
    {
        var allowed = AllowedStatuses[type];
        return allowed.Contains(status, StringComparer.OrdinalIgnoreCase)
            ? null
            : $"[error] Invalid status '{status}' for a {type}. Valid options: {string.Join(", ", allowed)}.";
    }

    private static HashSet<string>? ParseTypeFilter(string types)
    {
        if (string.IsNullOrWhiteSpace(types))
        {
            return [.. ProjectWorkspaceService.SubfolderKeys];
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TypeAliases.TryGetValue(raw, out var key))
            {
                return null;
            }

            result.Add(key);
        }

        return result;
    }

    /// <summary>Returns the content of a markdown section (from its heading to the next same-or-higher-level heading).</summary>
    internal static string ExtractSection(string content, string heading)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var inSection = false;

        foreach (var line in lines)
        {
            if (inSection && line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (line.TrimEnd().Equals(heading, StringComparison.OrdinalIgnoreCase))
            {
                inSection = true;
                continue;
            }

            if (inSection)
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// First non-empty body line that is not a heading, callout/quote, or placeholder —
    /// used as a one-line summary in listings.
    /// </summary>
    private static string FirstBodyLine(string raw)
    {
        var body = raw[FrontmatterParser.GetBodyStart(raw)..];
        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#') && !trimmed.StartsWith('>') &&
                !trimmed.StartsWith("_(") && !trimmed.StartsWith("```"))
            {
                return trimmed.Length > 120 ? trimmed[..120] + "..." : trimmed;
            }
        }

        return string.Empty;
    }

    private static string RenderProjectOverview(string raw)
    {
        return raw
            .Replace("_(what this project is, its goals, and its current state)_", "_(pending in project MOC)_", StringComparison.Ordinal)
            .Replace("- Repository:\n- Environments:\n- Documentation:", "_(key links pending in project MOC)_", StringComparison.Ordinal);
    }

    private async Task<(bool Created, string Message)> AppendConfigReferenceAsync(
        string configPath, CancellationToken cancellationToken)
    {
        const string marker = "engineering:";
        var referenceBlock = $"""

            # --- Agent workflow (engineering tools) ---
            # Reference for the engineering tool group. All values below are the built-in
            # defaults — uncomment and edit only what you want to change, then restart the server.
            # folders:
            #   projects: "{_workspace.ProjectsRootRelative}"
            #   knowledge: "{_workspace.KnowledgeRootRelative}"
            # engineering:
            #   subfolders:
            #     decisions: "decisions"
            #     bugs: "bugs"
            #     plans: "plans"
            #     knowledge: "knowledge"
            #     sessions: "sessions"
            #     daily: "daily"
            #     tickets: "tickets"
            #     backlog: "backlog"
            """;

        _fileSystem.CreateDirectory(Path.GetDirectoryName(configPath)!);

        if (_fileSystem.FileExists(configPath))
        {
            var existing = await _fileSystem.ReadAllTextAsync(configPath, cancellationToken);
            if (existing.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return (false, ".kioku/config.yml (engineering section already documented)");
            }

            await WriteTextAsync(
                configPath,
                existing + referenceBlock + "\n",
                requireAbsent: false,
                cancellationToken: cancellationToken);
            return (true, ".kioku/config.yml (appended commented engineering reference)");
        }

        await WriteTextAsync(
            configPath,
            referenceBlock.TrimStart('\n') + "\n",
            requireAbsent: true,
            cancellationToken: cancellationToken);
        return (true, ".kioku/config.yml (created with commented engineering reference)");
    }

    private async Task WriteTextAsync(
        string path,
        string content,
        bool requireAbsent,
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default)
    {
        if (_mutations is null)
        {
            await _fileSystem.WriteAllTextAsync(path, content, cancellationToken);
            return;
        }

        if (requireAbsent)
        {
            await _mutations.CreateTextAsync(path, content, preconditions, cancellationToken);
        }
        else
        {
            await _mutations.WriteTextAsync(path, content, preconditions, cancellationToken);
        }
    }

    private async Task DeleteFileAsync(
        string path,
        VaultMutationPreconditions? preconditions,
        CancellationToken cancellationToken)
    {
        if (_mutations is null)
        {
            _fileSystem.DeleteFile(path);
            return;
        }

        await _mutations.DeleteAsync(path, preconditions, cancellationToken);
    }
}
