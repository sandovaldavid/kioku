global using EngineeringWorkflowTools = Kioku.Mcp.Server.Tests.ProjectDocumentTestHarness;

using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Services;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Test-only facade that preserves the engineering-tool integration-test vocabulary while
/// exercising <see cref="IProjectDocumentService"/> directly. MCP adapter contracts are covered
/// separately by architecture and metadata tests.
/// </summary>
internal sealed class ProjectDocumentTestHarness
{
    private readonly IProjectDocumentService _documents;

    public ProjectDocumentTestHarness(IProjectDocumentService documents)
    {
        _documents = documents;
    }

    public ProjectDocumentTestHarness(
        VaultIndexService vault,
        KiokuConfiguration config,
        VaultConfigService vaultConfig,
        ProjectWorkspaceService workspace,
        ObsidianBridgeService bridge)
        : this(new ProjectDocumentService(
            vault, config, vaultConfig, workspace, bridge, new ProjectDocumentFileSystem()))
    {
    }

    public Task<string> create_project_doc(
        string doc_type,
        string project = "",
        string title = "",
        string status = "",
        string tags = "",
        string context = "",
        string decision = "",
        string consequences = "",
        string alternatives = "",
        string symptom = "",
        string root_cause = "",
        string fix = "",
        string related_files = "",
        string objective = "",
        string steps = "",
        string ticket = "",
        string content = "",
        string description = "",
        CancellationToken cancellationToken = default) =>
        _documents.CreateProjectDocAsync(
            doc_type, project, title, status, tags, context, decision, consequences, alternatives,
            symptom, root_cause, fix, related_files, objective, steps, ticket, content, description,
            cancellationToken);

    public Task<string> record_adr(
        string project, string title, string context, string decision, string consequences,
        string alternatives = "", string status = "accepted", string tags = "",
        CancellationToken cancellationToken = default) =>
        _documents.RecordAdrAsync(project, title, context, decision, consequences, alternatives, status, tags, cancellationToken);

    public Task<string> log_bug(
        string project, string title, string symptom, string root_cause, string fix,
        string status = "fixed", string related_files = "", string tags = "",
        CancellationToken cancellationToken = default) =>
        _documents.LogBugAsync(project, title, symptom, root_cause, fix, status, related_files, tags, cancellationToken);

    public Task<string> create_plan(
        string project, string title, string objective, string steps,
        string status = "draft", string ticket = "", string tags = "",
        CancellationToken cancellationToken = default) =>
        _documents.CreatePlanAsync(project, title, objective, steps, status, ticket, tags, cancellationToken);

    public Task<string> add_knowledge(
        string title, string content, string project = "", string tags = "",
        CancellationToken cancellationToken = default) =>
        _documents.AddKnowledgeAsync(title, content, project, tags, cancellationToken);

    public Task<string> add_backlog_item(
        string project, string title, string description, string tags = "",
        CancellationToken cancellationToken = default) =>
        _documents.AddBacklogItemAsync(project, title, description, tags, cancellationToken);

    public Task<string> get_project_context(
        string project, bool include_content = false, string types = "", int limit = 20,
        CancellationToken cancellationToken = default) =>
        _documents.GetProjectContextAsync(project, include_content, types, limit, cancellationToken);

    public Task<string> list_projects(CancellationToken cancellationToken = default) =>
        _documents.ListProjectsAsync(cancellationToken);

    public Task<string> list_engineering_templates(CancellationToken cancellationToken = default) =>
        _documents.ListEngineeringTemplatesAsync(cancellationToken);

    public Task<string> get_engineering_template(
        string type_key, CancellationToken cancellationToken = default) =>
        _documents.GetEngineeringTemplateAsync(type_key, cancellationToken);

    public Task<string> set_engineering_template(
        string type_key, string content = "", bool reset_to_default = false,
        CancellationToken cancellationToken = default) =>
        _documents.SetEngineeringTemplateAsync(type_key, content, reset_to_default, cancellationToken);

    public Task<string> setup_agent_workflow(
        string project = "", bool write_templates = true, bool patch_config = true,
        CancellationToken cancellationToken = default) =>
        _documents.SetupAgentWorkflowAsync(project, write_templates, patch_config, cancellationToken);

    internal static string ExtractSection(string content, string heading) =>
        ProjectDocumentService.ExtractSection(content, heading);
}
