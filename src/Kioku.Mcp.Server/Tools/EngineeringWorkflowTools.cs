using System.ComponentModel;
using System.Text;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for per-project engineering knowledge: architecture decision records (ADRs),
/// bug logs, implementation plans, knowledge notes, backlog ideas, and project context
/// re-reading. Documents live in the vault so humans edit them from Obsidian and agents
/// re-read them via get_project_context.
/// </summary>
[McpServerToolType]
public sealed class EngineeringWorkflowTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    VaultConfigService vaultConfig,
    ProjectWorkspaceService workspace,
    ObsidianBridgeService bridge)
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

    // record_adr

    [McpServerTool, Description(
        "Records an architecture decision record (ADR) for a project as " +
        "{projects}/{project}/decisions/ADR-NNNN-{title}.md with sequential numbering. " +
        "Scaffolds the project folder structure on first use. " +
        "To supersede an old ADR later, change its status with update_frontmatter.")]
    public async Task<string> record_adr(
        [Description("Project name (folder under the projects root). Use list_projects to discover existing ones.")] string project,
        [Description("Short decision title, e.g. 'Use PostgreSQL for persistence'.")] string title,
        [Description("The context: what problem or forces led to this decision.")] string context,
        [Description("The decision taken, stated in full sentences.")] string decision,
        [Description("Consequences of the decision (positive and negative).")] string consequences,
        [Description("Alternatives that were considered and why they were rejected.")] string alternatives = "",
        [Description("ADR status: proposed, accepted, or superseded.")] string status = "accepted",
        [Description("Extra tags, comma-separated.")] string tags = "")
    {
        if (ValidateStatus("decision", status) is { } statusError)
        {
            return statusError;
        }

        if (ProjectWorkspaceService.ValidateProjectName(project) is { } nameError)
        {
            return nameError;
        }

        // Holds the per-project ADR lock across both number allocation and the file write in
        // CreateDocAsync, so concurrent record_adr calls never compute the same next number.
        using var adrLock = await workspace.AcquireAdrLockAsync(project);
        var number = workspace.GetNextAdrNumber(project);
        return await CreateDocAsync(
            project,
            subfolderKey: "decisions",
            fileName: $"ADR-{number:D4}-{NoteHelpers.SanitizeFileName(title)}",
            type: "decision",
            status: status,
            baseTag: "adr",
            userTags: tags,
            templateKey: "adr",
            title: title,
            variables: new Dictionary<string, string>
            {
                ["number"] = number.ToString("D4"),
                ["context"] = context,
                ["decision"] = decision,
                ["consequences"] = consequences,
                ["alternatives"] = string.IsNullOrWhiteSpace(alternatives) ? "_(none recorded)_" : alternatives,
            },
            extraFields: new Dictionary<string, string> { ["adr"] = $"\"{number:D4}\"" },
            aliases: [$"ADR-{number:D4}"]);
    }

    // log_bug

    [McpServerTool, Description(
        "Logs a bug and its solution for a project as {projects}/{project}/bugs/BUG-{date}-{title}.md. " +
        "Records the symptom, root cause, and fix so future agents don't re-debug solved problems. " +
        "Scaffolds the project folder structure on first use.")]
    public async Task<string> log_bug(
        [Description("Project name (folder under the projects root).")] string project,
        [Description("Short bug title, e.g. 'Index race on startup'.")] string title,
        [Description("Observed symptom: what failed and how it manifested.")] string symptom,
        [Description("The actual root cause found.")] string root_cause,
        [Description("The fix that was applied (or should be applied if still open).")] string fix,
        [Description("Bug status: open or fixed.")] string status = "fixed",
        [Description("Related source files, comma-separated (e.g. 'src/a.ts, src/b.ts').")] string related_files = "",
        [Description("Extra tags, comma-separated.")] string tags = "")
    {
        if (ValidateStatus("bug", status) is { } statusError)
        {
            return statusError;
        }

        var relatedList = string.IsNullOrWhiteSpace(related_files)
            ? "_(none recorded)_"
            : string.Join("\n", related_files
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(f => $"- `{f}`"));

        return await CreateDocAsync(
            project,
            subfolderKey: "bugs",
            fileName: $"BUG-{DateTime.Now:yyyy-MM-dd}-{NoteHelpers.SanitizeFileName(title)}",
            type: "bug",
            status: status,
            baseTag: "bug",
            userTags: tags,
            templateKey: "bug",
            title: title,
            variables: new Dictionary<string, string>
            {
                ["symptom"] = symptom,
                ["root_cause"] = root_cause,
                ["fix"] = fix,
                ["related_files"] = relatedList,
            });
    }

    // create_plan

    [McpServerTool, Description(
        "Creates an implementation plan for a project as {projects}/{project}/plans/PLAN-{date}-{title}.md. " +
        "Write steps as a markdown checkbox list (- [ ] step) so task tools can track them. " +
        "When the plan is completed, set status to 'done' with update_frontmatter. " +
        "Scaffolds the project folder structure on first use.")]
    public async Task<string> create_plan(
        [Description("Project name (folder under the projects root).")] string project,
        [Description("Short plan title, e.g. 'Add semantic search'.")] string title,
        [Description("What the plan achieves and why.")] string objective,
        [Description("The plan steps in markdown. Prefer a checkbox list: '- [ ] step one'.")] string steps,
        [Description("Plan status: draft, active, or done.")] string status = "draft",
        [Description("Optional ticket note name this plan implements; linked as a wikilink.")] string ticket = "",
        [Description("Extra tags, comma-separated.")] string tags = "")
    {
        if (ValidateStatus("plan", status) is { } statusError)
        {
            return statusError;
        }

        return await CreateDocAsync(
            project,
            subfolderKey: "plans",
            fileName: $"PLAN-{DateTime.Now:yyyy-MM-dd}-{NoteHelpers.SanitizeFileName(title)}",
            type: "plan",
            status: status,
            baseTag: "plan",
            userTags: tags,
            templateKey: "plan",
            title: title,
            variables: new Dictionary<string, string>
            {
                ["objective"] = objective,
                ["steps"] = steps,
                ["ticket"] = string.IsNullOrWhiteSpace(ticket) ? "_(none)_" : $"[[{ticket}]]",
            },
            extraFields: string.IsNullOrWhiteSpace(ticket)
                ? null
                : new Dictionary<string, string> { ["ticket"] = $"\"[[{ticket}]]\"" });
    }

    // add_knowledge

    [McpServerTool, Description(
        "Saves a knowledge note. With a project it goes to {projects}/{project}/knowledge/; " +
        "without one it goes to the general knowledge folder. " +
        "Use for lessons learned, how-things-work explanations, and setup guides (e.g. local deployment).")]
    public async Task<string> add_knowledge(
        [Description("Note title, used as the file name (wiki-friendly, no prefix).")] string title,
        [Description("The knowledge content in markdown.")] string content,
        [Description("Project name for project-specific knowledge. Leave empty for general knowledge.")] string project = "",
        [Description("Extra tags, comma-separated.")] string tags = "")
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "[error] The 'title' parameter cannot be empty.";
        }

        var variables = new Dictionary<string, string> { ["content"] = content };

        if (!string.IsNullOrWhiteSpace(project))
        {
            return await CreateDocAsync(
                project,
                subfolderKey: "knowledge",
                fileName: NoteHelpers.SanitizeFileName(title),
                type: "knowledge",
                status: "active",
                baseTag: "knowledge",
                userTags: tags,
                templateKey: "knowledge",
                title: title,
                variables: variables);
        }

        // General knowledge: {knowledge}/{Title}.md, no project frontmatter.
        Directory.CreateDirectory(workspace.KnowledgeRoot);
        var filePath = Path.Combine(workspace.KnowledgeRoot, NoteHelpers.SanitizeFileName(title) + ".md");
        if (File.Exists(filePath))
        {
            return $"[error] Note already exists: '{workspace.ToVaultRelative(filePath)}'. Use update_note_content or append_to_note to modify it.";
        }

        var body = NoteHelpers.ExpandTemplateVariables(
            await workspace.ResolveTemplateAsync("knowledge"), variables, noteTitle: title);
        var relFolder = workspace.KnowledgeRootRelative;
        var mergedTags = NoteHelpers.MergeTagsWithInheritance(
            NoteHelpers.ParseTags(tags).Prepend("knowledge"),
            vaultConfig.GetInheritedTags(relFolder),
            vaultConfig.ExcludeFromTags);
        var frontmatter = NoteHelpers.BuildFrontmatter(
            mergedTags,
            type: "knowledge",
            status: "active",
            date: DateOnly.FromDateTime(DateTime.Now),
            domain: vaultConfig.GetDomainForFolder(relFolder),
            cssClasses: ["kioku-knowledge"]);

        await File.WriteAllTextAsync(filePath, frontmatter + "\n" + body, Encoding.UTF8);
        await vault.SynchronizeFileReindexAsync(filePath);

        var vaultRelPath = workspace.ToVaultRelative(filePath);
        var evalResult = await bridge.EvaluateTemplaterInPlaceAsync(body, vaultRelPath);
        if (evalResult.Applied)
        {
            await vault.SynchronizeFileReindexAsync(filePath);
        }

        var result = $"[ok] Knowledge note created: {vaultRelPath}";
        return evalResult.Warning is null ? result : $"{result}\n   [warning] {evalResult.Warning}";
    }

    // add_backlog_item

    [McpServerTool, Description(
        "Adds a future improvement or idea to a project's backlog as {projects}/{project}/backlog/{title}.md " +
        "with status 'proposed'. Use for out-of-scope improvements worth remembering. " +
        "Later, set status to 'adopted' or 'discarded' with update_frontmatter.")]
    public async Task<string> add_backlog_item(
        [Description("Project name (folder under the projects root).")] string project,
        [Description("Short idea title.")] string title,
        [Description("What the improvement is and why it was deferred.")] string description,
        [Description("Extra tags, comma-separated.")] string tags = "")
    {
        return await CreateDocAsync(
            project,
            subfolderKey: "backlog",
            fileName: NoteHelpers.SanitizeFileName(title),
            type: "idea",
            status: "proposed",
            baseTag: "idea",
            userTags: tags,
            templateKey: "idea",
            title: title,
            variables: new Dictionary<string, string> { ["description"] = description });
    }

    // get_project_context

    [McpServerTool, Description(
        "Returns the current state of a project workspace: the project MOC note, summaries of " +
        "recent work sessions, and per-type listings (decisions, bugs, plans, tickets, backlog, " +
        "knowledge, daily). Reads files fresh from disk, so edits made in Obsidian moments ago " +
        "are always reflected. Call this before resuming work on a project.")]
    public async Task<string> get_project_context(
        [Description("Project name (folder under the projects root). Use list_projects to discover names.")] string project,
        [Description("Include the full content of every listed document (verbose).")] bool include_content = false,
        [Description("Comma-separated type filter (adr, bug, plan, ticket, idea, knowledge, session, daily). Empty = all.")] string types = "",
        [Description("Maximum documents listed per type.")] int limit = 20)
    {
        if (ProjectWorkspaceService.ValidateProjectName(project) is { } nameError)
        {
            return nameError;
        }

        var projectFolder = workspace.GetProjectFolder(project);
        if (!Directory.Exists(projectFolder))
        {
            return $"[error] Project '{project}' not found under '{workspace.ProjectsRootRelative}/'. " +
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
        sb.AppendLine($"**Folder:** {workspace.ToVaultRelative(projectFolder)}/");
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        // Project MOC verbatim: it is the human-curated overview. Named after the leaf segment,
        // not the full (possibly grouped) identifier — same convention as EnsureProjectScaffoldAsync.
        var mocPath = Path.Combine(projectFolder, $"{Path.GetFileName(projectFolder)}.md");
        if (File.Exists(mocPath))
        {
            sb.AppendLine("## Project overview (MOC)");
            sb.AppendLine();
            sb.AppendLine((await File.ReadAllTextAsync(mocPath, Encoding.UTF8)).Trim());
            sb.AppendLine();
        }

        // Recent session summaries: the handoff from previous agents.
        if (typeFilter.Contains("sessions"))
        {
            var sessions = workspace.EnumerateProjectDocs(project, "sessions").Take(limit).ToList();
            if (sessions.Count > 0)
            {
                sb.AppendLine($"## Recent sessions ({sessions.Count})");
                sb.AppendLine();
                foreach (var file in sessions)
                {
                    var raw = await File.ReadAllTextAsync(file.FullName, Encoding.UTF8);
                    var meta = FrontmatterParser.Parse(raw);
                    sb.AppendLine($"### [{meta.Status ?? "unknown"}] {Path.GetFileNameWithoutExtension(file.Name)} — {workspace.ToVaultRelative(file.FullName)}");
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

            var docs = workspace.EnumerateProjectDocs(project, key);
            sb.AppendLine($"## {heading} ({docs.Count})");
            if (docs.Count == 0)
            {
                sb.AppendLine("_(none)_");
                sb.AppendLine();
                continue;
            }

            foreach (var file in docs.Take(limit))
            {
                var raw = await File.ReadAllTextAsync(file.FullName, Encoding.UTF8);
                var meta = FrontmatterParser.Parse(raw);
                var relPath = workspace.ToVaultRelative(file.FullName);
                var dateStr = meta.Date?.ToString("yyyy-MM-dd") ?? file.LastWriteTimeUtc.ToString("yyyy-MM-dd");
                var summaryLine = FirstBodyLine(raw);

                sb.Append($"- [{meta.Status ?? "-"}] {Path.GetFileNameWithoutExtension(file.Name)} — {relPath} ({dateStr})");
                if (!string.IsNullOrWhiteSpace(summaryLine))
                {
                    sb.Append($" — {summaryLine}");
                }

                sb.AppendLine();

                if (include_content)
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

        if (include_content && fullContent.Length > 0)
        {
            sb.AppendLine("## Full document contents");
            sb.AppendLine();
            sb.Append(fullContent);
        }

        sb.AppendLine("_Read any single document with `read_note <path>`; change statuses with `update_frontmatter`._");
        return sb.ToString();
    }

    // list_projects

    [McpServerTool, Description(
        "Lists all project workspaces under the projects root with per-type document counts " +
        "and the last modification date. Projects can be grouped in plain folders (e.g. " +
        "'Atena/api.core', 'Atena/api.common') — pass the full identifier shown here as the " +
        "'project' parameter to other engineering tools. Use to discover project names.")]
    public Task<string> list_projects()
    {
        if (!Directory.Exists(workspace.ProjectsRoot))
        {
            return Task.FromResult(
                $"[info] No projects folder found at '{workspace.ProjectsRootRelative}/'. " +
                "Use setup_agent_workflow to create the structure.");
        }

        var projects = workspace.DiscoverProjects();
        if (projects.Count == 0)
        {
            return Task.FromResult(
                $"[info] No projects yet under '{workspace.ProjectsRootRelative}/'. " +
                "Use setup_agent_workflow with a project name, or any record tool (record_adr, log_bug, ...) to create one.");
        }

        var sb = new StringBuilder($"[ok] {projects.Count} project(s) under '{workspace.ProjectsRootRelative}/':\n\n");
        foreach (var project in projects)
        {
            var counts = ProjectWorkspaceService.SubfolderKeys
                .Select(key => (key, count: workspace.EnumerateProjectDocs(project, key).Count))
                .Where(t => t.count > 0)
                .Select(t => $"{t.key}: {t.count}")
                .ToList();

            var projectDir = workspace.GetProjectFolder(project);
            var lastModified = Directory.EnumerateFiles(projectDir, "*.md", SearchOption.AllDirectories)
                .Select(f => File.GetLastWriteTimeUtc(f))
                .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(projectDir))
                .Max();

            sb.Append($"- **{project}**");
            sb.Append(counts.Count > 0 ? $" — {string.Join(", ", counts)}" : " — empty");
            sb.AppendLine($" (last modified {lastModified:yyyy-MM-dd})");
        }

        return Task.FromResult(sb.ToString());
    }

    // list_engineering_templates

    [McpServerTool, Description(
        "Lists the engineering doc types (adr, bug, plan, knowledge, idea, session, daily, " +
        "ticket, project-moc), whether each has a vault override or falls back to the embedded " +
        "default, its path, and the {{variables}} it supports. Use before editing a template " +
        "with set_engineering_template.")]
    public async Task<string> list_engineering_templates()
    {
        var sb = new StringBuilder($"[ok] {ProjectWorkspaceService.TemplateKeys.Length} engineering template(s):\n\n");

        foreach (var typeKey in ProjectWorkspaceService.TemplateKeys)
        {
            var overridePath = workspace.GetVaultTemplatePath(typeKey);
            var isOverride = overridePath is not null && File.Exists(overridePath);
            var vars = ProjectWorkspaceService.SupportedVariablesFor(typeKey);

            sb.Append($"  **{typeKey}** — ");
            sb.Append(isOverride
                ? $"override at {workspace.ToVaultRelative(overridePath!)}"
                : "using embedded default");
            sb.AppendLine($" — variables: {string.Join(", ", vars.Select(v => "{{" + v + "}}"))}");
        }

        return await Task.FromResult(sb.ToString());
    }

    // get_engineering_template

    [McpServerTool, Description(
        "Reads the current effective body template for an engineering doc type (vault override " +
        "if one exists, otherwise the embedded default), plus the {{variables}} it supports. " +
        "Read this before proposing an edit with set_engineering_template.")]
    public async Task<string> get_engineering_template(
        [Description("Doc type: adr, bug, plan, knowledge, idea, session, daily, ticket, or project-moc.")] string type_key)
    {
        if (!ProjectWorkspaceService.TemplateKeys.Contains(type_key, StringComparer.OrdinalIgnoreCase))
        {
            return $"[error] Unknown template type '{type_key}'. Valid types: {string.Join(", ", ProjectWorkspaceService.TemplateKeys)}.";
        }

        var overridePath = workspace.GetVaultTemplatePath(type_key);
        var isOverride = overridePath is not null && File.Exists(overridePath);
        var content = await workspace.ResolveTemplateAsync(type_key);
        var vars = ProjectWorkspaceService.SupportedVariablesFor(type_key);

        var sb = new StringBuilder($"[ok] Template '{type_key}' ({(isOverride ? $"override: {workspace.ToVaultRelative(overridePath!)}" : "embedded default")}):\n\n");
        sb.AppendLine($"Supported variables: {string.Join(", ", vars.Select(v => "{{" + v + "}}"))}");
        sb.AppendLine();
        sb.AppendLine("```markdown");
        sb.AppendLine(content);
        sb.AppendLine("```");

        return sb.ToString();
    }

    // set_engineering_template

    [McpServerTool, Description(
        "Creates or updates the vault override template for an engineering doc type at " +
        "{templates}/kioku/{type_key}.md (always overwrites, unlike create_template). " +
        "Pass reset_to_default=true to delete the override and revert to the embedded default. " +
        "Never triggers Templater evaluation: this writes the template itself, which is only " +
        "evaluated later when a note is generated from it.")]
    public async Task<string> set_engineering_template(
        [Description("Doc type: adr, bug, plan, knowledge, idea, session, daily, ticket, or project-moc.")] string type_key,
        [Description("New template body content. Ignored when reset_to_default=true.")] string content = "",
        [Description("Delete the vault override and revert to the embedded default instead of writing.")] bool reset_to_default = false)
    {
        if (!ProjectWorkspaceService.TemplateKeys.Contains(type_key, StringComparer.OrdinalIgnoreCase))
        {
            return $"[error] Unknown template type '{type_key}'. Valid types: {string.Join(", ", ProjectWorkspaceService.TemplateKeys)}.";
        }

        if (reset_to_default)
        {
            var existing = workspace.GetVaultTemplatePath(type_key);
            if (existing is not null && File.Exists(existing))
            {
                File.Delete(existing);
                return $"[ok] Reverted '{type_key}' to the embedded default (removed {workspace.ToVaultRelative(existing)}).";
            }

            return $"[ok] '{type_key}' already uses the embedded default (no override to remove).";
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return "[error] The 'content' parameter cannot be empty unless reset_to_default=true.";
        }

        var targetDir = Path.Combine(workspace.ResolveTemplatesFolderOrDefault(), "kioku");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, $"{type_key}.md");

        await File.WriteAllTextAsync(targetPath, content, Encoding.UTF8);

        var recognized = new HashSet<string>(ProjectWorkspaceService.SupportedVariablesFor(type_key), StringComparer.OrdinalIgnoreCase);
        var unknownVars = ProjectWorkspaceService.ExtractTemplateVariableNames(content)
            .Where(v => !recognized.Contains(v))
            .ToList();

        var result = $"[ok] Template '{type_key}' saved: {workspace.ToVaultRelative(targetPath)}";
        if (unknownVars.Count > 0)
        {
            result += $"\n   [warning] not a recognized variable for '{type_key}' and will be left literal: " +
                      string.Join(", ", unknownVars.Select(v => "{{" + v + "}}"));
        }

        return result;
    }

    // setup_agent_workflow

    [McpServerTool, Description(
        "Sets up the agent workflow structure in the vault: creates the projects and knowledge " +
        "root folders, copies the default document templates (adr, bug, plan, knowledge, idea, " +
        "session, daily, ticket, project-moc) into {templates}/kioku/ so the user can edit them " +
        "in Obsidian, and documents the configuration in .kioku/config.yml. " +
        "Fully idempotent: never overwrites existing files or human edits.")]
    public async Task<string> setup_agent_workflow(
        [Description("Optional project to scaffold (creates its folder structure and MOC note).")] string project = "",
        [Description("Copy the default templates into the vault's templates folder (skips existing files).")] bool write_templates = true,
        [Description("Append a commented reference block to .kioku/config.yml if not present.")] bool patch_config = true)
    {
        var created = new List<string>();
        var skipped = new List<string>();

        // Root folders
        foreach (var root in new[] { workspace.ProjectsRoot, workspace.KnowledgeRoot })
        {
            var rel = workspace.ToVaultRelative(root) + "/";
            if (Directory.Exists(root))
            {
                skipped.Add(rel);
            }
            else
            {
                Directory.CreateDirectory(root);
                created.Add(rel);
            }
        }

        // Templates — runs before the project scaffold below so that, on first use, the files
        // already exist on disk when the scaffold step tries to register them in Templater's
        // own folder-template settings (Templater can't point at an embedded resource).
        if (write_templates)
        {
            var (templatesCreated, templatesSkipped) = await workspace.EnsureEngineeringTemplatesOnDiskAsync();
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

            var scaffolded = await workspace.EnsureProjectScaffoldAsync(project);
            if (scaffolded.Count > 0)
            {
                created.AddRange(scaffolded);
            }
            else
            {
                skipped.Add($"{workspace.ToVaultRelative(workspace.GetProjectFolder(project))}/ (already scaffolded)");
            }
        }

        // Config reference block
        if (patch_config)
        {
            var configPath = Path.Combine(config.VaultPath, ".kioku", "config.yml");
            var patchResult = await AppendConfigReferenceAsync(configPath);
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
        IEnumerable<string>? aliases = null)
    {
        if (ProjectWorkspaceService.ValidateProjectName(project) is { } nameError)
        {
            return nameError;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return "[error] The 'title' parameter cannot be empty.";
        }

        var scaffolded = await workspace.EnsureProjectScaffoldAsync(project);

        var folder = workspace.GetSubfolder(project, subfolderKey);
        var filePath = Path.Combine(folder, fileName + ".md");
        if (File.Exists(filePath))
        {
            return $"[error] Note already exists: '{workspace.ToVaultRelative(filePath)}'. Use update_note_content to modify it.";
        }

        var projectLink = $"[[{ProjectWorkspaceService.ProjectLeafName(project)}]]";
        variables["project"] = project;
        variables["project_link"] = projectLink;
        var body = NoteHelpers.ExpandTemplateVariables(
            await workspace.ResolveTemplateAsync(templateKey), variables, noteTitle: title);

        var relFolder = workspace.ToVaultRelative(folder);
        var mergedTags = NoteHelpers.MergeTagsWithInheritance(
            NoteHelpers.ParseTags(userTags).Prepend(baseTag),
            vaultConfig.GetInheritedTags(relFolder),
            vaultConfig.ExcludeFromTags);

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
            domain: vaultConfig.GetDomainForFolder(relFolder),
            aliases: aliases,
            cssClasses: [$"kioku-{baseTag}"],
            extraFields: fields);

        await File.WriteAllTextAsync(filePath, frontmatter + "\n" + body, Encoding.UTF8);
        await vault.SynchronizeFileReindexAsync(filePath);

        var vaultRelPath = workspace.ToVaultRelative(filePath);
        var evalResult = await bridge.EvaluateTemplaterInPlaceAsync(body, vaultRelPath);
        if (evalResult.Applied)
        {
            await vault.SynchronizeFileReindexAsync(filePath);
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

    private async Task<(bool Created, string Message)> AppendConfigReferenceAsync(string configPath)
    {
        const string marker = "engineering:";
        var referenceBlock = $"""

            # --- Agent workflow (engineering tools) ---
            # Reference for the engineering tool group. All values below are the built-in
            # defaults — uncomment and edit only what you want to change, then restart the server.
            # folders:
            #   projects: "{workspace.ProjectsRootRelative}"
            #   knowledge: "{workspace.KnowledgeRootRelative}"
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

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        if (File.Exists(configPath))
        {
            var existing = await File.ReadAllTextAsync(configPath, Encoding.UTF8);
            if (existing.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return (false, ".kioku/config.yml (engineering section already documented)");
            }

            await File.AppendAllTextAsync(configPath, referenceBlock + "\n", Encoding.UTF8);
            return (true, ".kioku/config.yml (appended commented engineering reference)");
        }

        await File.WriteAllTextAsync(configPath, referenceBlock.TrimStart('\n') + "\n", Encoding.UTF8);
        return (true, ".kioku/config.yml (created with commented engineering reference)");
    }
}
