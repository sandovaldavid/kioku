using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class MarkdownTextExtractorTests
{
    [Fact]
    public void Extract_EmptyContent_ReturnsEmpty()
    {
        var result = MarkdownTextExtractor.Extract("");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Extract_PlainText_ReturnsAsIs()
    {
        var result = MarkdownTextExtractor.Extract("Hello World");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Extract_Heading_RemovesHashPrefix()
    {
        var result = MarkdownTextExtractor.Extract("# Hello World");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Extract_MultipleHeadings_RemovesAllHashPrefixes()
    {
        var content = "# Title\n\n## Section\n\n### Subsection";
        var result = MarkdownTextExtractor.Extract(content);
        Assert.Contains("Title", result);
        Assert.Contains("Section", result);
        Assert.Contains("Subsection", result);
    }

    [Fact]
    public void Extract_Bold_RemovesMarkers()
    {
        var result = MarkdownTextExtractor.Extract("This is **bold** text");
        Assert.Equal("This is bold text", result);
    }

    [Fact]
    public void Extract_Italic_RemovesMarkers()
    {
        var result = MarkdownTextExtractor.Extract("This is *italic* text");
        Assert.Equal("This is italic text", result);
    }

    [Fact]
    public void Extract_InlineCode_KeepsContent()
    {
        var result = MarkdownTextExtractor.Extract("Use `code` here");
        Assert.Contains("code", result);
    }

    [Fact]
    public void Extract_Wikilink_ExtractsAlias()
    {
        var result = MarkdownTextExtractor.Extract("See [[My Note|the note]] for details");
        Assert.Contains("the note", result);
        Assert.DoesNotContain("[[", result);
        Assert.DoesNotContain("]]", result);
    }

    [Fact]
    public void Extract_WikilinkNoAlias_ExtractsTarget()
    {
        var result = MarkdownTextExtractor.Extract("See [[My Note]] for details");
        Assert.Contains("My Note", result);
        Assert.DoesNotContain("[[", result);
    }

    [Fact]
    public void Extract_MarkdownLink_ExtractsText()
    {
        var result = MarkdownTextExtractor.Extract("Click [here](https://example.com) now");
        Assert.Contains("here", result);
        Assert.DoesNotContain("https://example.com", result);
    }

    [Fact]
    public void Extract_ListItems_RemovesBulletPrefix()
    {
        var content = "- Item one\n- Item two\n- Item three";
        var result = MarkdownTextExtractor.Extract(content);
        Assert.Contains("Item one", result);
        Assert.Contains("Item two", result);
        Assert.Contains("Item three", result);
    }

    [Fact]
    public void Extract_WithFrontmatter_SkipsWhenBodyStartProvided()
    {
        var content = "---\ntags:\n  - project\n---\n# Hello World";
        var bodyStart = FrontmatterParser.GetBodyStart(content);
        var result = MarkdownTextExtractor.Extract(content, bodyStart);
        Assert.Contains("Hello World", result);
        Assert.DoesNotContain("tags", result);
        Assert.DoesNotContain("project", result);
    }

    [Fact]
    public void Extract_Table_ExtractsCellText()
    {
        var content = "| Name | Age |\n| --- | --- |\n| Alice | 30 |";
        var result = MarkdownTextExtractor.Extract(content);
        Assert.Contains("Name", result);
        Assert.Contains("Age", result);
        Assert.Contains("Alice", result);
        Assert.Contains("30", result);
    }

    [Fact]
    public void Extract_MultipleParagraphs_PreservesContent()
    {
        var content = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";
        var result = MarkdownTextExtractor.Extract(content);
        Assert.Contains("First paragraph", result);
        Assert.Contains("Second paragraph", result);
        Assert.Contains("Third paragraph", result);
    }

    [Fact]
    public void ExtractEmbedIndexesTargetWithoutEmbedMarker()
    {
        var result = MarkdownTextExtractor.Extract("Embedded note: ![[Other Note]]");

        Assert.Contains("Other Note", result);
        Assert.DoesNotContain("![[", result);
    }

    [Fact]
    public void ExtractEmbedWithAliasAndHeadingIndexesAlias()
    {
        var result = MarkdownTextExtractor.Extract("See ![[Project#Overview|the overview]] here");

        Assert.Contains("the overview", result);
        Assert.DoesNotContain("Project", result);
        Assert.DoesNotContain("![[", result);
    }

    [Fact]
    public void ExtractCalloutRemovesMarkerButKeepsContent()
    {
        var result = MarkdownTextExtractor.Extract("> [!NOTE] Important\n> Keep this text");

        Assert.Contains("Important", result);
        Assert.Contains("Keep this text", result);
        Assert.DoesNotContain("[!NOTE]", result);
    }

    [Fact]
    public void ExtractObsidianCommentDoesNotEnterIndex()
    {
        var result = MarkdownTextExtractor.Extract("Visible %% private implementation detail %% text");

        Assert.Contains("Visible", result);
        Assert.Contains("text", result);
        Assert.DoesNotContain("private implementation detail", result);
    }

    [Fact]
    public void ExtractFencedCodeBlockDoesNotEnterIndex()
    {
        var result = MarkdownTextExtractor.Extract("Visible\n\n```csharp\nvar hidden = true;\n```\n\nAfter");

        Assert.Contains("Visible", result);
        Assert.Contains("After", result);
        Assert.DoesNotContain("hidden", result);
    }

    [Fact]
    public void ExtractBlockIdDoesNotEnterIndex()
    {
        var result = MarkdownTextExtractor.Extract("Paragraph text ^block-id");

        Assert.Contains("Paragraph text", result);
        Assert.DoesNotContain("block-id", result);
    }

    [Fact]
    public void ExtractWikilinksAdjacentToTextPreservesTargets()
    {
        var result = MarkdownTextExtractor.Extract("texto[[Nota]] texto![[Embed]]");

        Assert.Contains("Nota", result);
        Assert.Contains("Embed", result);
        Assert.DoesNotContain("![[", result);
    }

    [Fact]
    public void ExtractLongerClosingFenceDoesNotEnterIndex()
    {
        var result = MarkdownTextExtractor.Extract("Visible\n\n```csharp\nvar hidden = true;\n````\n\nAfter");

        Assert.Contains("Visible", result);
        Assert.Contains("After", result);
        Assert.DoesNotContain("hidden", result);
    }

    // ExtractWikilinks tests

    [Fact]
    public void ExtractWikilinks_NoLinks_ReturnsEmpty()
    {
        var result = MarkdownTextExtractor.ExtractWikilinks("No links here");
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractWikilinks_SimpleLink_ExtractsTarget()
    {
        var result = MarkdownTextExtractor.ExtractWikilinks("See [[My Note]] for details");
        Assert.Single(result);
        Assert.Equal("My Note", result[0]);
    }

    [Fact]
    public void ExtractWikilinks_LinkWithAlias_ExtractsTargetOnly()
    {
        var result = MarkdownTextExtractor.ExtractWikilinks("See [[My Note|the note]] here");
        Assert.Single(result);
        Assert.Equal("My Note", result[0]);
    }

    [Fact]
    public void ExtractWikilinks_LinkWithHeader_PreservesTargetForResolver()
    {
        var result = MarkdownTextExtractor.ExtractWikilinks("See [[My Note#Section]] here");
        Assert.Single(result);
        Assert.Equal("My Note#Section", result[0]);
    }

    [Fact]
    public void ExtractWikilinks_LinkWithBlockReference_PreservesTargetForResolver()
    {
        var result = MarkdownTextExtractor.ExtractWikilinks("See [[My Note#^block-id]] here");
        Assert.Single(result);
        Assert.Equal("My Note#^block-id", result[0]);
    }

    [Fact]
    public void ExtractWikilinks_LiteralHashInTarget_PreservesTarget()
    {
        var result = MarkdownTextExtractor.ExtractWikilinks("See [[filename-with-#-character]] here");
        Assert.Single(result);
        Assert.Equal("filename-with-#-character", result[0]);
    }

    [Fact]
    public void ExtractWikilinks_MultipleLinks_ExtractsAll()
    {
        var content = "See [[Note A]] and [[Note B]] and [[Note C|alias]]";
        var result = MarkdownTextExtractor.ExtractWikilinks(content);
        Assert.Equal(3, result.Count);
        Assert.Contains("Note A", result);
        Assert.Contains("Note B", result);
        Assert.Contains("Note C", result);
    }

    [Fact]
    public void ExtractWikilinks_EmptyLink_IgnoresIt()
    {
        var result = MarkdownTextExtractor.ExtractWikilinks("See [[]] here");
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractWikilinks_UnclosedLink_IgnoresIt()
    {
        var result = MarkdownTextExtractor.ExtractWikilinks("See [[unclosed link");
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractWikilinkReferences_UnclosedLink_ReportsMalformed()
    {
        var result = MarkdownTextExtractor.ExtractWikilinkReferences("See [[unclosed link");
        Assert.Single(result);
        Assert.True(result[0].IsMalformed);
        Assert.Contains("[[unclosed link", result[0].Raw);
    }

    [Fact]
    public void ExtractWikilinks_LinkInsideFencedCodeBlock_IsIgnored()
    {
        var content = "Real link: [[Real Note]]\n\n```\nExample syntax: [[Fake Note]]\n```\n";
        var result = MarkdownTextExtractor.ExtractWikilinks(content);
        Assert.Single(result);
        Assert.Equal("Real Note", result[0]);
    }

    [Fact]
    public void ExtractWikilinks_LinkInsideTildeFencedCodeBlock_IsIgnored()
    {
        var content = "~~~\n[[Fake Note]]\n~~~\n[[Real Note]]";
        var result = MarkdownTextExtractor.ExtractWikilinks(content);
        Assert.Single(result);
        Assert.Equal("Real Note", result[0]);
    }

    [Fact]
    public void ExtractWikilinks_LinkInsideInlineCode_IsIgnored()
    {
        var content = "Wikilinks look like `[[Note Name]]` — see [[Real Note]] for an example.";
        var result = MarkdownTextExtractor.ExtractWikilinks(content);
        Assert.Single(result);
        Assert.Equal("Real Note", result[0]);
    }
}
