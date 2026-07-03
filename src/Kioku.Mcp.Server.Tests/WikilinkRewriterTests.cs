using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class WikilinkRewriterTests
{
    private static readonly WikilinkRewriter.RewritePlan RenamePlan = new(
        OldShortName: "Note One",
        NewShortName: "Renamed Note",
        OldFullPath: "Note One",
        NewFullPath: "Renamed Note",
        RewriteShortNameLinks: true,
        ShortNameAmbiguous: false);

    private static readonly WikilinkRewriter.RewritePlan MovePlan = new(
        OldShortName: "Project Alpha",
        NewShortName: "Project Alpha",
        OldFullPath: "Projects/Project Alpha",
        NewFullPath: "Archive/2024/Project Alpha",
        RewriteShortNameLinks: false,
        ShortNameAmbiguous: false);

    [Fact]
    public void Rewrite_BareNameLink_ReplacesTarget()
    {
        var result = WikilinkRewriter.Rewrite("See [[Note One]] for details.", RenamePlan);

        Assert.Equal("See [[Renamed Note]] for details.", result.NewContent);
        Assert.Equal(1, result.ReplacedCount);
    }

    [Fact]
    public void Rewrite_LinkWithAlias_PreservesAlias()
    {
        var result = WikilinkRewriter.Rewrite("See [[Note One|the note]] for details.", RenamePlan);

        Assert.Equal("See [[Renamed Note|the note]] for details.", result.NewContent);
        Assert.Equal(1, result.ReplacedCount);
    }

    [Fact]
    public void Rewrite_LinkWithHeading_PreservesHeading()
    {
        var result = WikilinkRewriter.Rewrite("See [[Note One#Section]] for details.", RenamePlan);

        Assert.Equal("See [[Renamed Note#Section]] for details.", result.NewContent);
    }

    [Fact]
    public void Rewrite_LinkWithHeadingAndAlias_PreservesBoth()
    {
        var result = WikilinkRewriter.Rewrite("See [[Note One#Section|display text]] here.", RenamePlan);

        Assert.Equal("See [[Renamed Note#Section|display text]] here.", result.NewContent);
    }

    [Fact]
    public void Rewrite_LinkWithBlockRef_PreservesBlockRef()
    {
        var result = WikilinkRewriter.Rewrite("See [[Note One#^abc123]] for details.", RenamePlan);

        Assert.Equal("See [[Renamed Note#^abc123]] for details.", result.NewContent);
    }

    [Fact]
    public void Rewrite_EmbedLink_PreservesEmbedPrefix()
    {
        var result = WikilinkRewriter.Rewrite("![[Note One]]", RenamePlan);

        Assert.Equal("![[Renamed Note]]", result.NewContent);
        Assert.Equal(1, result.ReplacedCount);
    }

    [Fact]
    public void Rewrite_LinkInFencedCodeBlock_LeavesUntouched()
    {
        var content = "Before\n```\n[[Note One]]\n```\nAfter";

        var result = WikilinkRewriter.Rewrite(content, RenamePlan);

        Assert.Equal(content, result.NewContent);
        Assert.Equal(0, result.ReplacedCount);
    }

    [Fact]
    public void Rewrite_LinkInTildeCodeBlock_LeavesUntouched()
    {
        var content = "Before\n~~~\n[[Note One]]\n~~~\nAfter";

        var result = WikilinkRewriter.Rewrite(content, RenamePlan);

        Assert.Equal(content, result.NewContent);
        Assert.Equal(0, result.ReplacedCount);
    }

    [Fact]
    public void Rewrite_FullPathLink_ReplacesWithNewFullPath()
    {
        var result = WikilinkRewriter.Rewrite("See [[Projects/Project Alpha]] for details.", MovePlan);

        Assert.Equal("See [[Archive/2024/Project Alpha]] for details.", result.NewContent);
        Assert.Equal(1, result.ReplacedCount);
    }

    [Fact]
    public void Rewrite_BareNameLink_WhenRewriteShortNameLinksFalse_LeavesUntouched()
    {
        var content = "See [[Project Alpha]] for details.";

        var result = WikilinkRewriter.Rewrite(content, MovePlan);

        Assert.Equal(content, result.NewContent);
        Assert.Equal(0, result.ReplacedCount);
    }

    [Fact]
    public void Rewrite_BareNameLink_WhenAmbiguous_ReportsAndLeavesUntouched()
    {
        var ambiguousPlan = RenamePlan with { ShortNameAmbiguous = true };
        var content = "See [[Note One]] for details.";

        var result = WikilinkRewriter.Rewrite(content, ambiguousPlan);

        Assert.Equal(content, result.NewContent);
        Assert.Equal(0, result.ReplacedCount);
        Assert.Equal(["Note One"], result.AmbiguousMatches);
    }

    [Fact]
    public void Rewrite_UnrelatedLink_LeftUntouched()
    {
        var content = "See [[Some Other Note]] for details.";

        var result = WikilinkRewriter.Rewrite(content, RenamePlan);

        Assert.Equal(content, result.NewContent);
        Assert.Equal(0, result.ReplacedCount);
    }

    [Fact]
    public void Rewrite_MultipleLinksSameLine_ReplacesAll()
    {
        var content = "[[Note One]] and [[Note One|alias]] and [[Note One#h]]";

        var result = WikilinkRewriter.Rewrite(content, RenamePlan);

        Assert.Equal("[[Renamed Note]] and [[Renamed Note|alias]] and [[Renamed Note#h]]", result.NewContent);
        Assert.Equal(3, result.ReplacedCount);
    }

    [Fact]
    public void Rewrite_FrontmatterBeforeBodyStart_IsNeverTouched()
    {
        var content = "---\naliases:\n  - Note One\n---\nBody text without links.";
        var bodyStart = FrontmatterParser.GetBodyStart(content);

        var result = WikilinkRewriter.Rewrite(content, RenamePlan, bodyStart);

        Assert.Equal(content, result.NewContent);
        Assert.Equal(0, result.ReplacedCount);
    }

    [Fact]
    public void Rewrite_PreservesCrlfLineEndings()
    {
        var content = "Line one [[Note One]]\r\nLine two\r\n";

        var result = WikilinkRewriter.Rewrite(content, RenamePlan);

        Assert.Equal("Line one [[Renamed Note]]\r\nLine two\r\n", result.NewContent);
    }

    [Fact]
    public void Rewrite_NoTrailingNewline_PreservesExactly()
    {
        var content = "[[Note One]]";

        var result = WikilinkRewriter.Rewrite(content, RenamePlan);

        Assert.Equal("[[Renamed Note]]", result.NewContent);
    }
}
