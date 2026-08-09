namespace Kioku.Mcp.Server.Services;

public sealed partial class ProjectWorkspaceService
{
    static ProjectWorkspaceService()
    {
        CoreSubfolderKeys =
            ["decisions", "bugs", "specs", "plans", "knowledge", "sessions", "backlog"];

        SubfolderKeys =
            ["decisions", "bugs", "specs", "plans", "knowledge", "sessions", "daily", "tickets", "backlog"];

        TemplateKeys =
            ["adr", "bug", "spec", "plan", "knowledge", "idea", "session", "daily", "ticket", "project-moc"];

        TemplateVariables = new Dictionary<string, string[]>
        {
            ["adr"] = ["project", "project_link", "number", "context", "decision", "consequences", "alternatives"],
            ["bug"] = ["project", "project_link", "symptom", "root_cause", "fix", "related_files"],
            ["spec"] =
            [
                "project", "project_link", "objective", "context", "requirements", "non_goals",
                "architecture", "components", "data_flow", "error_handling", "security_privacy",
                "compatibility", "testing_strategy", "decisions", "open_questions", "related",
                "source_issue",
            ],
            ["plan"] = ["project", "project_link", "objective", "steps", "ticket", "spec"],
            ["knowledge"] = ["project", "project_link", "content"],
            ["idea"] = ["project", "project_link", "description"],
            ["session"] = ["project", "project_link", "goal", "agent"],
            ["daily"] = ["project", "project_link"],
            ["ticket"] = ["project", "project_link"],
            ["project-moc"] = ["project", "project_folder", "decisions_folder", "plans_folder", "bugs_folder", "backlog_folder"],
        };

        SubfolderTemplatePairs =
        [
            ("decisions", "adr"), ("bugs", "bug"), ("specs", "spec"), ("plans", "plan"),
            ("knowledge", "knowledge"), ("sessions", "session"), ("daily", "daily"),
            ("tickets", "ticket"), ("backlog", "idea"),
        ];
    }
}
