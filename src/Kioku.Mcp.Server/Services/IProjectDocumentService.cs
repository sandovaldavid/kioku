namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Application boundary for per-project engineering document workflows: architecture decision
/// records (ADRs), bug logs, implementation plans, knowledge notes, backlog ideas, project
/// context re-reading, project discovery, and engineering template management.
/// MCP adapters depend on this contract instead of constructing workflow services directly.
/// </summary>
public interface IProjectDocumentService
{
    Task<string> CreateProjectDocAsync(
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
        CancellationToken cancellationToken = default);

    Task<string> RecordAdrAsync(
        string project,
        string title,
        string context,
        string decision,
        string consequences,
        string alternatives,
        string status,
        string tags,
        CancellationToken cancellationToken = default);

    Task<string> LogBugAsync(
        string project,
        string title,
        string symptom,
        string rootCause,
        string fix,
        string status,
        string relatedFiles,
        string tags,
        CancellationToken cancellationToken = default);

    Task<string> CreatePlanAsync(
        string project,
        string title,
        string objective,
        string steps,
        string status,
        string ticket,
        string tags,
        CancellationToken cancellationToken = default);

    Task<string> AddKnowledgeAsync(
        string title,
        string content,
        string project,
        string tags,
        CancellationToken cancellationToken = default);

    Task<string> AddBacklogItemAsync(
        string project,
        string title,
        string description,
        string tags,
        CancellationToken cancellationToken = default);

    Task<string> GetProjectContextAsync(
        string project,
        bool includeContent,
        string types,
        int limit,
        CancellationToken cancellationToken = default);

    Task<string> ListProjectsAsync(CancellationToken cancellationToken = default);

    Task<string> ListEngineeringTemplatesAsync(CancellationToken cancellationToken = default);

    Task<string> GetEngineeringTemplateAsync(
        string typeKey,
        CancellationToken cancellationToken = default);

    Task<string> SetEngineeringTemplateAsync(
        string typeKey,
        string content,
        bool resetToDefault,
        CancellationToken cancellationToken = default);

    Task<string> SetupAgentWorkflowAsync(
        string project,
        bool writeTemplates,
        bool patchConfig,
        CancellationToken cancellationToken = default);
}
