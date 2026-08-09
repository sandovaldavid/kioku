using System.ComponentModel;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for per-project engineering knowledge: architecture decision records (ADRs),
/// bug logs, specifications, implementation plans, knowledge notes, backlog ideas, and project
/// context re-reading. Documents live in the vault so humans edit them from Obsidian and agents
/// re-read them via get_project_context.
/// </summary>
[McpServerToolType]
public sealed class EngineeringWorkflowTools
{
    private readonly IProjectDocumentService _documents;
    private readonly EngineeringSpecService? _specs;

    public EngineeringWorkflowTools(IProjectDocumentService documents)
    {
        _documents = documents;
    }

    public EngineeringWorkflowTools(
        IProjectDocumentService documents,
        ProjectWorkspaceService workspace,
        VaultConfigService vaultConfig,
        VaultIndexService vault,
        ObsidianBridgeService bridge,
        IVaultMutationService mutations)
    {
        _documents = documents;
        _specs = new EngineeringSpecService(workspace, vaultConfig, vault, bridge, mutations);
    }

    [McpServerTool, Description(
        "Creates an engineering document for a project. doc_type is adr, bug, plan, backlog, or " +
        "knowledge; knowledge may omit project to create a general knowledge note. Use the focused " +
        "create_engineering_spec tool for first-class specifications.")]
    public Task<string> create_project_doc(
        [Description("Document type: adr, bug, plan, backlog, or knowledge.")] string doc_type,
        [Description("Project name; omit only for general knowledge.")] string project = "",
        [Description("Short document title.")] string title = "",
        [Description("Status. ADR: proposed/accepted/superseded; bug: open/fixed; plan: draft/active/done; backlog: proposed/adopted/discarded.")] string status = "",
        [Description("Extra tags, comma-separated.")] string tags = "",
        [Description("ADR context.")] string context = "",
        [Description("ADR decision.")] string decision = "",
        [Description("ADR consequences.")] string consequences = "",
        [Description("ADR alternatives.")] string alternatives = "",
        [Description("Bug symptom.")] string symptom = "",
        [Description("Bug root cause.")] string root_cause = "",
        [Description("Bug fix.")] string fix = "",
        [Description("Bug-related source files, comma-separated.")] string related_files = "",
        [Description("Plan objective.")] string objective = "",
        [Description("Plan steps in markdown.")] string steps = "",
        [Description("Optional plan ticket note name.")] string ticket = "",
        [Description("Knowledge content in markdown.")] string content = "",
        [Description("Backlog idea description.")] string description = "",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the target, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the target path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "",
        CancellationToken cancellationToken = default) =>
        _documents.CreateProjectDocAsync(
            doc_type, project, title, status, tags, context, decision, consequences, alternatives,
            symptom, root_cause, fix, related_files, objective, steps, ticket, content, description,
            preconditions: VaultMutationPreconditions.FromToolArguments(
                expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id),
            cancellationToken: cancellationToken);

    public Task<string> record_adr(
        string project, string title, string context, string decision, string consequences,
        string alternatives = "", string status = "accepted", string tags = "",
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default) =>
        _documents.RecordAdrAsync(
            project, title, context, decision, consequences, alternatives, status, tags,
            preconditions: preconditions,
            cancellationToken: cancellationToken);

    [Description(
        "Logs a bug and its solution for a project as {projects}/{project}/bugs/BUG-{date}-{title}.md. " +
        "Records the symptom, root cause, and fix so future agents don't re-debug solved problems. " +
        "Scaffolds project folders on first use.")]
    public Task<string> log_bug(
        [Description("Project name (folder under the projects root).")] string project,
        [Description("Short bug title, e.g. 'Index race on startup'.")] string title,
        [Description("Observed symptom: what failed and how it manifested.")] string symptom,
        [Description("The actual root cause found.")] string root_cause,
        [Description("The fix that was applied (or should be applied if still open).")] string fix,
        [Description("Bug status: open or fixed.")] string status = "fixed",
        [Description("Related source files, comma-separated (e.g. 'src/a.ts, src/b.ts').")] string related_files = "",
        [Description("Extra tags, comma-separated.")] string tags = "",
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default) =>
        _documents.LogBugAsync(
            project, title, symptom, root_cause, fix, status, related_files, tags,
            preconditions: preconditions,
            cancellationToken: cancellationToken);

    [Description(
        "Creates an implementation plan for a project as {projects}/{project}/plans/PLAN-{date}-{title}.md. " +
        "Write steps as a markdown checkbox list (- [ ] step) so task tools can track them. " +
        "When the plan is completed, set status to 'done' with update_frontmatter. " +
        "Scaffolds project folders on first use.")]
    public Task<string> create_plan(
        [Description("Project name (folder under the projects root).")] string project,
        [Description("Short plan title, e.g. 'Add semantic search'.")] string title,
        [Description("What the plan achieves and why.")] string objective,
        [Description("The plan steps in markdown. Prefer a checkbox list: '- [ ] step one'.")] string steps,
        [Description("Plan status: draft, active, or done.")] string status = "draft",
        [Description("Optional ticket note name this plan implements; linked as a wikilink.")] string ticket = "",
        [Description("Extra tags, comma-separated.")] string tags = "",
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default) =>
        _documents.CreatePlanAsync(
            project, title, objective, steps, status, ticket, tags,
            preconditions: preconditions,
            cancellationToken: cancellationToken);

    [Description(
        "Saves a knowledge note. With a project it goes to {projects}/{project}/knowledge/; " +
        "without one it goes to the general knowledge folder. " +
        "Use for lessons learned, how-things-work explanations, and setup guides (e.g. local deployment).")]
    public Task<string> add_knowledge(
        [Description("Note title, used as the file name (wiki-friendly, no prefix).")] string title,
        [Description("The knowledge content in markdown.")] string content,
        [Description("Project name for project-specific knowledge. Leave empty for general knowledge.")] string project = "",
        [Description("Extra tags, comma-separated.")] string tags = "",
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default) =>
        _documents.AddKnowledgeAsync(
            title, content, project, tags,
            preconditions: preconditions,
            cancellationToken: cancellationToken);

    [Description(
        "Adds a future improvement or idea to a project's backlog as {projects}/{project}/backlog/{title}.md " +
        "with status 'proposed'. Use for out-of-scope improvements worth remembering. " +
        "Later, set status to 'adopted' or 'discarded' with update_frontmatter.")]
    public Task<string> add_backlog_item(
        [Description("Project name (folder under the projects root).")] string project,
        [Description("Short idea title.")] string title,
        [Description("What the improvement is and why it was deferred.")] string description,
        [Description("Extra tags, comma-separated.")] string tags = "",
        VaultMutationPreconditions? preconditions = null,
        CancellationToken cancellationToken = default) =>
        _documents.AddBacklogItemAsync(
            project, title, description, tags,
            preconditions: preconditions,
            cancellationToken: cancellationToken);

    [McpServerTool, Description(
        "Returns the current state of a project workspace: the project MOC note, summaries of " +
        "recent work sessions, and per-type listings (decisions, bugs, specs, plans, tickets, backlog, " +
        "knowledge, daily). Reads fresh from disk. Approved specs are current requirements; draft " +
        "specs are in progress; superseded/discarded specs are explicitly historical.")]
    public async Task<string> get_project_context(
        [Description("Project name (folder under the projects root). Use list_projects to discover names.")] string project,
        [Description("Include the full content of every listed document (verbose).")] bool include_content = false,
        [Description("Comma-separated type filter (adr, decision(s), bug(s), spec(s), plan(s), ticket(s), idea/backlog, knowledge, session(s), daily). Empty = all.")] string types = "",
        [Description("Maximum documents listed per type.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (_specs is null)
        {
            return await _documents.GetProjectContextAsync(project, include_content, types, limit, cancellationToken);
        }

        var requested = types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var explicitFilter = !string.IsNullOrWhiteSpace(types);
        var includesSpecs = !explicitFilter || requested.Any(IsSpecAlias);
        if (!includesSpecs)
        {
            return await _documents.GetProjectContextAsync(project, include_content, types, limit, cancellationToken);
        }

        var remaining = requested.Where(value => !IsSpecAlias(value)).ToArray();
        if (explicitFilter && remaining.Length == 0)
        {
            return await _specs.BuildSpecsOnlyContextAsync(project, include_content, limit, cancellationToken);
        }

        var baseTypes = explicitFilter ? string.Join(',', remaining) : string.Empty;
        var baseContext = await _documents.GetProjectContextAsync(project, include_content, baseTypes, limit, cancellationToken);
        if (baseContext.StartsWith("[error]", StringComparison.OrdinalIgnoreCase))
        {
            return baseContext;
        }

        var specsSection = await _specs.BuildSpecsSectionAsync(project, include_content, limit, cancellationToken);
        if (specsSection.StartsWith("[error]", StringComparison.OrdinalIgnoreCase))
        {
            return specsSection;
        }

        return EngineeringSpecService.InjectSpecsSection(baseContext, specsSection);
    }

    [McpServerTool, Description(
        "Lists all project workspaces under the projects root with per-type document counts " +
        "and the last modification date. Projects can be grouped in plain folders (e.g. " +
        "'Atena/api.core', 'Atena/api.common') — pass the full identifier shown here as the " +
        "'project' parameter to other engineering tools. Use to discover project names.")]
    public Task<string> list_projects(CancellationToken cancellationToken = default) =>
        _documents.ListProjectsAsync(cancellationToken);

    [Description(
        "Lists the engineering doc types (adr, bug, spec, plan, knowledge, idea, session, daily, " +
        "ticket, project-moc), whether each has a vault override or falls back to the embedded " +
        "default, its path, and the {{variables}} it supports. Use manage_templates with " +
        "scope='engineering' before editing a template.")]
    public Task<string> list_engineering_templates(CancellationToken cancellationToken = default) =>
        _documents.ListEngineeringTemplatesAsync(cancellationToken);

    [Description(
        "Reads the current effective body template for an engineering doc type (vault override " +
        "if one exists, otherwise the embedded default), plus the {{variables}} it supports. " +
        "Read this before proposing an edit with set_engineering_template.")]
    public Task<string> get_engineering_template(
        [Description("Doc type: adr, bug, spec, plan, knowledge, idea, session, daily, ticket, or project-moc.")] string type_key,
        CancellationToken cancellationToken = default) =>
        _documents.GetEngineeringTemplateAsync(type_key, cancellationToken);

    [Description(
        "Creates or updates the vault override template for an engineering doc type at " +
        "{templates}/kioku/{type_key}.md (always overwrites, unlike create_template). " +
        "Pass reset_to_default=true to delete the override and revert to the embedded default. " +
        "Never triggers Templater evaluation: this writes the template itself, which is only " +
        "evaluated later when a note is generated from it. Prefer manage_templates with " +
        "scope='engineering' for MCP access.")]
    public Task<string> set_engineering_template(
        [Description("Doc type: adr, bug, spec, plan, knowledge, idea, session, daily, ticket, or project-moc.")] string type_key,
        [Description("New template body content. Ignored when reset_to_default=true.")] string content = "",
        [Description("Delete the vault override and revert to the embedded default instead of writing.")] bool reset_to_default = false,
        CancellationToken cancellationToken = default) =>
        _documents.SetEngineeringTemplateAsync(
            type_key,
            content,
            reset_to_default,
            preconditions: null,
            cancellationToken: cancellationToken);

    [McpServerTool, Description(
        "Sets up the agent workflow structure in the vault: creates the projects and knowledge " +
        "root folders, copies the default document templates (adr, bug, spec, plan, knowledge, idea, " +
        "session, daily, ticket, project-moc) into {templates}/kioku/ so the user can edit them " +
        "in Obsidian, and documents the configuration in .kioku/config.yml. " +
        "Fully idempotent: never overwrites existing files or human edits.")]
    public Task<string> setup_agent_workflow(
        [Description("Optional project to scaffold (creates its folder structure and MOC note).")] string project = "",
        [Description("Copy the default templates into the vault's templates folder (skips existing files).")] bool write_templates = true,
        [Description("Append a commented reference block to .kioku/config.yml if not present.")] bool patch_config = true,
        CancellationToken cancellationToken = default) =>
        _documents.SetupAgentWorkflowAsync(project, write_templates, patch_config, cancellationToken);

    internal static string ExtractSection(string content, string heading) =>
        ProjectDocumentService.ExtractSection(content, heading);

    private static bool IsSpecAlias(string value) =>
        value.Equals("spec", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("specs", StringComparison.OrdinalIgnoreCase);
}
