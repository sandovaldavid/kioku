using Kioku.Mcp.Server.Prompts;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class KiokuPromptsTests
{
    [Fact]
    public void ResearchDigest_DefaultFolder_ScopesToWholeVault()
    {
        var result = KiokuPrompts.research_digest();

        Assert.Contains("the whole vault", result);
        Assert.Contains("get_work_context", result);
        Assert.Contains("mode='semantic'", result);
    }

    [Fact]
    public void ResearchDigest_WithFolder_MentionsFolder()
    {
        var result = KiokuPrompts.research_digest("Literature");

        Assert.Contains("'Literature' folder", result);
    }

    [Fact]
    public void ProcessInbox_MentionsProcessInboxToolWithBothApplyModes()
    {
        var result = KiokuPrompts.process_inbox();

        Assert.Contains("process_inbox", result);
        Assert.Contains("apply=false", result);
        Assert.Contains("apply=true", result);
        Assert.Contains("git diff", result);
    }

    [Fact]
    public void ProcessInbox_WithInboxArgument_ForwardsItToTheToolCall()
    {
        var result = KiokuPrompts.process_inbox("Captures");

        Assert.Contains("inbox_folder='Captures'", result);
    }

    [Fact]
    public void WeeklyReview_MentionsAllFourSourceTools()
    {
        var result = KiokuPrompts.weekly_review();

        Assert.Contains("get_work_context", result);
        Assert.Contains("overdue_only=true", result);
        Assert.Contains("audit_vault", result);
        Assert.Contains("suggest_links", result);
    }

    [Fact]
    public void LiteratureReview_IncludesTopicAndCitationInstruction()
    {
        var result = KiokuPrompts.literature_review("distributed consensus");

        Assert.Contains("distributed consensus", result);
        Assert.Contains("mode='hybrid'", result);
        Assert.Contains("audit_citations", result);
        Assert.Contains("[[wikilink]]", result);
        Assert.Contains("export_citations", result);
    }

    [Fact]
    public void ResumeProject_LoadsContextAndStartsSession()
    {
        var result = KiokuPrompts.resume_project("kioku");

        Assert.Contains("get_project_context", result);
        Assert.Contains("project='kioku'", result);
        Assert.Contains("start_work_session", result);
        Assert.Contains("end_work_session", result);
    }

    [Fact]
    public void ProjectTask_OrchestratesContextExecutionAndHandoff()
    {
        var result = KiokuPrompts.project_task("kioku", "implement semantic search");

        Assert.Contains("implement semantic search", result);
        Assert.Contains("get_project_context", result);
        Assert.Contains("start_work_session", result);
        Assert.Contains("create_project_doc", result);
        Assert.Contains("list_tasks", result);
        Assert.Contains("end_work_session", result);
        Assert.Contains("do not execute", result);
    }

    [Fact]
    public void RecordDecision_ChecksPriorAdrsAndHandlesSupersede()
    {
        var result = KiokuPrompts.record_decision("kioku", "database choice");

        Assert.Contains("database choice", result);
        Assert.Contains("types='adr'", result);
        Assert.Contains("create_project_doc", result);
        Assert.Contains("update_frontmatter", result);
        Assert.Contains("superseded", result);
        Assert.Contains("suggest_links", result);
    }

    [Fact]
    public void LogBugfix_GathersFieldsAndLogsBug()
    {
        var result = KiokuPrompts.log_bugfix("kioku");

        Assert.Contains("create_project_doc", result);
        Assert.Contains("project='kioku'", result);
        Assert.Contains("root", result);
        Assert.Contains("suggest_links", result);
    }

    [Fact]
    public void PlanFeature_ChecksPriorArtAndCreatesPlan()
    {
        var result = KiokuPrompts.plan_feature("kioku", "semantic search");

        Assert.Contains("semantic search", result);
        Assert.Contains("get_project_context", result);
        Assert.Contains("mode='hybrid'", result);
        Assert.Contains("create_project_doc", result);
        Assert.Contains("checkbox", result);
        Assert.Contains("suggest_links", result);
    }

    [Fact]
    public void WorkOnTicket_ReadsStructuresAndLinksPlan()
    {
        var result = KiokuPrompts.work_on_ticket("kioku", "add-export");

        Assert.Contains("add-export", result);
        Assert.Contains("read_note", result);
        Assert.Contains("create_project_doc", result);
        Assert.Contains("ticket='add-export'", result);
        Assert.Contains("update_frontmatter", result);
    }

    [Fact]
    public void WriteDaily_ReadsPreviousDailyAndSessions()
    {
        var result = KiokuPrompts.write_daily("kioku");

        Assert.Contains("get_project_context", result);
        Assert.Contains("types='daily,session'", result);
        Assert.Contains("create_note", result);
        Assert.Contains("project link", result);
        Assert.Contains("create_note", result);
    }
}
