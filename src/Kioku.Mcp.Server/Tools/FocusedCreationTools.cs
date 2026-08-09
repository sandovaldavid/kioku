using System.ComponentModel;
using Kioku.Mcp.Server.Domain;
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
    IProjectDocumentService documents,
    IVaultMutationService mutations,
    ProjectWorkspaceService? workspace = null)
{
    private readonly ProjectWorkspaceService _workspace =
        workspace ?? new ProjectWorkspaceService(config, vaultConfig, bridge, mutations);

    private readonly EngineeringWorkflowTools _engineering = new(documents);

    private readonly EngineeringSpecService _specs = new(
        workspace ?? new ProjectWorkspaceService(config, vaultConfig, bridge, mutations),
        vaultConfig,
        vault,
        bridge,
        mutations);

    private readonly NoteCommandTools _notes =
        new(
            vault,
            config,
            vaultConfig,
            new ZettelkastenTools(vault, embedding, hybrid, config, vaultConfig, bridge, mutations),
            metrics,
            pathPolicy,
            mutations);

    [McpServerTool, Description("Records an architecture decision for a project with focused ADR fields only.")]
    public Task<string> record_adr(
        [Description("Project name.")] string project,
        [Description("Short ADR title.")] string title,
        [Description("Context and forces behind the decision.")] string context,
        [Description("The chosen decision.")] string decision,
        [Description("Consequences of the decision.")] string consequences,
        [Description("Alternatives considered.")] string alternatives = "",
        [Description("ADR status: proposed, accepted, or superseded.")] string status = "accepted",
        [Description("Extra tags, comma-separated.")] string tags = "",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the target path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "") =>
        _engineering.record_adr(
            project,
            title,
            context,
            decision,
            consequences,
            alternatives,
            status,
            tags,
            preconditions: VaultMutationPreconditions.FromToolArguments(
                expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id));

    [McpServerTool, Description("Records a project bug, its root cause, and its fix.")]
    public Task<string> record_bug(
        [Description("Project name.")] string project,
        [Description("Short bug title.")] string title,
        [Description("Observed symptom.")] string symptom,
        [Description("Confirmed root cause.")] string root_cause,
        [Description("Applied or proposed fix.")] string fix,
        [Description("Bug status: open or fixed.")] string status = "fixed",
        [Description("Related source files, comma-separated.")] string related_files = "",
        [Description("Extra tags, comma-separated.")] string tags = "",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the target path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "") =>
        _engineering.log_bug(
            project,
            title,
            symptom,
            root_cause,
            fix,
            status,
            related_files,
            tags,
            preconditions: VaultMutationPreconditions.FromToolArguments(
                expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id));

    [McpServerTool, Description(
        "Creates a first-class engineering specification in the project's core specs folder. " +
        "Spec status is one of draft, approved, superseded, or discarded.")]
    public Task<string> create_engineering_spec(
        [Description("Project name.")] string project,
        [Description("Short specification title.")] string title,
        [Description("What is being built and the intended behavior.")] string objective,
        [Description("Requirements in markdown.")] string requirements,
        [Description("Spec status: draft, approved, superseded, or discarded.")] string status = "draft",
        [Description("Source issue or request reference, e.g. '#408'.")] string source_issue = "",
        [Description("Extra tags, comma-separated.")] string tags = "",
        [Description("Relevant background and constraints.")] string context = "",
        [Description("Explicit non-goals in markdown.")] string non_goals = "",
        [Description("Architecture/design in markdown.")] string architecture = "",
        [Description("Components involved in markdown.")] string components = "",
        [Description("Data flow in markdown.")] string data_flow = "",
        [Description("Error handling requirements in markdown.")] string error_handling = "",
        [Description("Security and privacy requirements in markdown.")] string security_privacy = "",
        [Description("Compatibility constraints in markdown.")] string compatibility = "",
        [Description("Testing strategy in markdown.")] string testing_strategy = "",
        [Description("Decisions already made in markdown.")] string decisions = "",
        [Description("Open questions in markdown.")] string open_questions = "",
        [Description("Related issue, PR, ADR, or note references in markdown.")] string related = "",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the target path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "") =>
        _specs.CreateSpecAsync(
            project,
            title,
            objective,
            requirements,
            status,
            source_issue,
            tags,
            context,
            non_goals,
            architecture,
            components,
            data_flow,
            error_handling,
            security_privacy,
            compatibility,
            testing_strategy,
            decisions,
            open_questions,
            related,
            preconditions: VaultMutationPreconditions.FromToolArguments(
                expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id));

    [McpServerTool, Description(
        "Creates an implementation plan for a project. When spec is supplied, the plan is linked " +
        "to that same-project first-class spec. Draft specs are allowed with a warning; superseded " +
        "or discarded specs are rejected as historical/non-actionable.")]
    public Task<string> create_implementation_plan(
        [Description("Project name.")] string project,
        [Description("Short plan title.")] string title,
        [Description("What the plan achieves and why.")] string objective,
        [Description("Implementation steps in markdown.")] string steps,
        [Description("Plan status: draft, active, or done.")] string status = "draft",
        [Description("Optional linked ticket note.")] string ticket = "",
        [Description("Optional linked spec basename or wikilink from the same project.")] string spec = "",
        [Description("Extra tags, comma-separated.")] string tags = "",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the target path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "")
    {
        var preconditions = VaultMutationPreconditions.FromToolArguments(
            expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id);

        return string.IsNullOrWhiteSpace(spec)
            ? _engineering.create_plan(project, title, objective, steps, status, ticket, tags, preconditions)
            : _specs.CreatePlanFromSpecAsync(project, title, objective, steps, spec, status, ticket, tags, preconditions);
    }

    [McpServerTool, Description("Saves project-specific or general reusable knowledge.")]
    public Task<string> save_project_knowledge(
        [Description("Knowledge note title.")] string title,
        [Description("Knowledge content in markdown.")] string content,
        [Description("Project name; leave empty for general knowledge.")] string project = "",
        [Description("Extra tags, comma-separated.")] string tags = "",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the target path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "") =>
        _engineering.add_knowledge(
            title,
            content,
            project,
            tags,
            preconditions: VaultMutationPreconditions.FromToolArguments(
                expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id));

    [McpServerTool, Description("Adds a deferred improvement or idea to a project's backlog.")]
    public Task<string> add_backlog_item(
        [Description("Project name.")] string project,
        [Description("Short backlog item title.")] string title,
        [Description("What the improvement is and why it was deferred.")] string description,
        [Description("Extra tags, comma-separated.")] string tags = "",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the target path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "") =>
        _engineering.add_backlog_item(
            project,
            title,
            description,
            tags,
            preconditions: VaultMutationPreconditions.FromToolArguments(
                expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id));

    [McpServerTool, Description("Creates a regular markdown note with optional frontmatter defaults and template rendering.")]
    public Task<string> create_regular_note(
        [Description("Note name or vault-relative path.")] string name,
        [Description("Markdown body.")] string content = "",
        [Description("Comma-separated tags.")] string tags = "",
        [Description("Frontmatter type; empty uses configured defaults.")] string type = "",
        [Description("Frontmatter status; empty uses configured defaults.")] string status = "",
        [Description("Optional target folder.")] string folder = "",
        [Description("Optional vault-relative template path.")] string template = "",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the note, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "") =>
        _notes.create_note(
            name, content, "note", tags, type, status, folder, template,
            expected_revision: expected_revision,
            expected_hash: expected_hash,
            claim_id: claim_id,
            fence_generation: fence_generation,
            resource_key: resource_key,
            mutation_id: mutation_id);

    [McpServerTool, Description("Creates a timestamped Zettelkasten note.")]
    public Task<string> create_zettel(
        [Description("Zettel title.")] string title,
        [Description("Markdown content.")] string content,
        [Description("Comma-separated tags.")] string tags = "",
        [Description("Optional target folder.")] string folder = "",
        [Description("Automatically add related wikilinks.")] bool link_related = true,
        [Description("Maximum related notes to link.")] int max_links = 5,
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the note, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "") =>
        _notes.create_note(
            title, content, "zettel", tags, folder: folder, link_related: link_related, max_links: max_links,
            expected_revision: expected_revision,
            expected_hash: expected_hash,
            claim_id: claim_id,
            fence_generation: fence_generation,
            resource_key: resource_key,
            mutation_id: mutation_id);

    [McpServerTool, Description("Creates a literature note from bibliographic metadata.")]
    public Task<string> create_literature_note(
        [Description("Work title.")] string title,
        [Description("Author or authors.")] string author,
        [Description("Publication year.")] string year,
        [Description("Source or URL.")] string source = "",
        [Description("Summary in markdown.")] string summary = "",
        [Description("Comma-separated tags.")] string tags = "",
        [Description("Target folder; defaults to Literature.")] string folder = "Literature",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the note, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "") =>
        _notes.create_note(
            title, kind: "literature", tags: tags, folder: folder, author: author, year: year,
            source: source, summary: summary,
            expected_revision: expected_revision,
            expected_hash: expected_hash,
            claim_id: claim_id,
            fence_generation: fence_generation,
            resource_key: resource_key,
            mutation_id: mutation_id);

    [McpServerTool, Description("Generates a Map of Content for a vault folder.")]
    public Task<string> create_moc(
        [Description("Vault-relative folder to map.")] string folder,
        [Description("Optional output note name.")] string output_name = "",
        [Description("Optional output folder.")] string output_folder = "",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the note, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the output note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "") =>
        _notes.create_note(
            kind: "moc", folder: folder, output_name: output_name, output_folder: output_folder,
            expected_revision: expected_revision,
            expected_hash: expected_hash,
            claim_id: claim_id,
            fence_generation: fence_generation,
            resource_key: resource_key,
            mutation_id: mutation_id);

    [McpServerTool, Description("Creates a Folder Notes-compatible README note for a vault folder.")]
    public Task<string> create_folder_readme(
        [Description("Vault-relative folder, at most two levels deep.")] string folder,
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the note, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "") =>
        _notes.create_note(
            kind: "folder-readme", folder: folder,
            expected_revision: expected_revision,
            expected_hash: expected_hash,
            claim_id: claim_id,
            fence_generation: fence_generation,
            resource_key: resource_key,
            mutation_id: mutation_id);
}
