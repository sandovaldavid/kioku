using System.ComponentModel;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// Focused MCP creation surface. These tools intentionally expose only parameters relevant
/// to one user intent while delegating to the compatibility implementations.
/// </summary>
[McpServerToolType]
public sealed class FocusedCreationTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    VaultConfigService vaultConfig,
    ObsidianBridgeService bridge,
    EmbeddingService embedding,
    HybridSearchService hybrid,
    MetricsService metrics,
    VaultPathPolicy pathPolicy,
    IProjectDocumentService documents)
{
    private readonly EngineeringWorkflowTools _engineering = new(documents);

    private readonly NoteCommandTools _notes =
        new(
            vault,
            config,
            vaultConfig,
            new ZettelkastenTools(vault, embedding, hybrid, config, vaultConfig, bridge),
            metrics,
            pathPolicy);

    [McpServerTool, Description("Records an architecture decision for a project with focused ADR fields only.")]
    public Task<string> record_adr(
        [Description("Project name.")] string project,
        [Description("Short ADR title.")] string title,
        [Description("Context and forces behind the decision.")] string context,
        [Description("The chosen decision.")] string decision,
        [Description("Consequences of the decision.")] string consequences,
        [Description("Alternatives considered.")] string alternatives = "",
        [Description("ADR status: proposed, accepted, or superseded.")] string status = "accepted",
        [Description("Extra tags, comma-separated.")] string tags = "") =>
        _engineering.record_adr(project, title, context, decision, consequences, alternatives, status, tags);

    [McpServerTool, Description("Records a project bug, its root cause, and its fix.")]
    public Task<string> record_bug(
        [Description("Project name.")] string project,
        [Description("Short bug title.")] string title,
        [Description("Observed symptom.")] string symptom,
        [Description("Confirmed root cause.")] string root_cause,
        [Description("Applied or proposed fix.")] string fix,
        [Description("Bug status: open or fixed.")] string status = "fixed",
        [Description("Related source files, comma-separated.")] string related_files = "",
        [Description("Extra tags, comma-separated.")] string tags = "") =>
        _engineering.log_bug(project, title, symptom, root_cause, fix, status, related_files, tags);

    [McpServerTool, Description("Creates an implementation plan for a project.")]
    public Task<string> create_implementation_plan(
        [Description("Project name.")] string project,
        [Description("Short plan title.")] string title,
        [Description("What the plan achieves and why.")] string objective,
        [Description("Implementation steps in markdown.")] string steps,
        [Description("Plan status: draft, active, or done.")] string status = "draft",
        [Description("Optional linked ticket note.")] string ticket = "",
        [Description("Extra tags, comma-separated.")] string tags = "") =>
        _engineering.create_plan(project, title, objective, steps, status, ticket, tags);

    [McpServerTool, Description("Saves project-specific or general reusable knowledge.")]
    public Task<string> save_project_knowledge(
        [Description("Knowledge note title.")] string title,
        [Description("Knowledge content in markdown.")] string content,
        [Description("Project name; leave empty for general knowledge.")] string project = "",
        [Description("Extra tags, comma-separated.")] string tags = "") =>
        _engineering.add_knowledge(title, content, project, tags);

    [McpServerTool, Description("Adds a deferred improvement or idea to a project's backlog.")]
    public Task<string> add_backlog_item(
        [Description("Project name.")] string project,
        [Description("Short backlog item title.")] string title,
        [Description("What the improvement is and why it was deferred.")] string description,
        [Description("Extra tags, comma-separated.")] string tags = "") =>
        _engineering.add_backlog_item(project, title, description, tags);

    [McpServerTool, Description("Creates a regular markdown note with optional frontmatter defaults and template rendering.")]
    public Task<string> create_regular_note(
        [Description("Note name or vault-relative path.")] string name,
        [Description("Markdown body.")] string content = "",
        [Description("Comma-separated tags.")] string tags = "",
        [Description("Frontmatter type; empty uses configured defaults.")] string type = "",
        [Description("Frontmatter status; empty uses configured defaults.")] string status = "",
        [Description("Optional target folder.")] string folder = "",
        [Description("Optional vault-relative template path.")] string template = "") =>
        _notes.create_note(name, content, "note", tags, type, status, folder, template);

    [McpServerTool, Description("Creates a timestamped Zettelkasten note.")]
    public Task<string> create_zettel(
        [Description("Zettel title.")] string title,
        [Description("Markdown content.")] string content,
        [Description("Comma-separated tags.")] string tags = "",
        [Description("Optional target folder.")] string folder = "",
        [Description("Automatically add related wikilinks.")] bool link_related = true,
        [Description("Maximum related notes to link.")] int max_links = 5) =>
        _notes.create_note(title, content, "zettel", tags, folder: folder, link_related: link_related, max_links: max_links);

    [McpServerTool, Description("Creates a literature note from bibliographic metadata.")]
    public Task<string> create_literature_note(
        [Description("Work title.")] string title,
        [Description("Author or authors.")] string author,
        [Description("Publication year.")] string year,
        [Description("Source or URL.")] string source = "",
        [Description("Summary in markdown.")] string summary = "",
        [Description("Comma-separated tags.")] string tags = "",
        [Description("Target folder; defaults to Literature.")] string folder = "Literature") =>
        _notes.create_note(title, kind: "literature", tags: tags, folder: folder, author: author, year: year, source: source, summary: summary);

    [McpServerTool, Description("Generates a Map of Content for a vault folder.")]
    public Task<string> create_moc(
        [Description("Vault-relative folder to map.")] string folder,
        [Description("Optional output note name.")] string output_name = "",
        [Description("Optional output folder.")] string output_folder = "") =>
        _notes.create_note(kind: "moc", folder: folder, output_name: output_name, output_folder: output_folder);

    [McpServerTool, Description("Creates a Folder Notes-compatible README note for a vault folder.")]
    public Task<string> create_folder_readme(
        [Description("Vault-relative folder, at most two levels deep.")] string folder) =>
        _notes.create_note(kind: "folder-readme", folder: folder);
}
