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
        Assert.Contains("get_recent_activity", result);
        Assert.Contains("search_notes_semantic", result);
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
        Assert.Contains("revert_all_uncommitted", result);
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

        Assert.Contains("generate_digest", result);
        Assert.Contains("period='week'", result);
        Assert.Contains("list_overdue_tasks", result);
        Assert.Contains("find_unlinked_notes", result);
        Assert.Contains("suggest_links", result);
    }

    [Fact]
    public void LiteratureReview_IncludesTopicAndCitationInstruction()
    {
        var result = KiokuPrompts.literature_review("distributed consensus");

        Assert.Contains("distributed consensus", result);
        Assert.Contains("search_notes_hybrid", result);
        Assert.Contains("get_literature_gap", result);
        Assert.Contains("[[wikilink]]", result);
        Assert.Contains("export_citations", result);
    }
}
