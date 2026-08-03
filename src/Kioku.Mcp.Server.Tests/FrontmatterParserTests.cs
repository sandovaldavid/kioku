using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class FrontmatterParserTests
{
    [Fact]
    public void Parse_NoFrontmatter_ReturnsEmpty()
    {
        var content = "# Hello World\n\nSome content here.";
        var result = FrontmatterParser.Parse(content);

        Assert.Same(Kioku.Mcp.Server.Domain.NoteMetadata.Empty, result);
    }

    [Fact]
    public void Parse_EmptyFrontmatter_ReturnsEmpty()
    {
        var content = "---\n---\n# Hello World";
        var result = FrontmatterParser.Parse(content);

        Assert.Empty(result.Tags);
        Assert.Empty(result.Aliases);
        Assert.Null(result.Status);
        Assert.Null(result.NoteType);
        Assert.Null(result.Date);
    }

    [Fact]
    public void Parse_TagsAsList_ParsesCorrectly()
    {
        var content = """
            ---
            tags:
              - project
              - ai
              - research
            ---
            # Hello
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.Equal(3, result.Tags.Count);
        Assert.Contains("project", result.Tags);
        Assert.Contains("ai", result.Tags);
        Assert.Contains("research", result.Tags);
    }

    [Fact]
    public void Parse_TagsInline_ParsesCorrectly()
    {
        var content = """
            ---
            tags: [project, ai, research]
            ---
            # Hello
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.Equal(3, result.Tags.Count);
        Assert.Contains("project", result.Tags);
        Assert.Contains("ai", result.Tags);
        Assert.Contains("research", result.Tags);
    }

    [Fact]
    public void Parse_TagsCommaSeparated_ParsesCorrectly()
    {
        var content = """
            ---
            tags: project, ai, research
            ---
            # Hello
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.Equal(3, result.Tags.Count);
        Assert.Contains("project", result.Tags);
    }

    [Fact]
    public void Parse_TagsWithHashStripsHash()
    {
        var content = """
            ---
            tags:
              - "#project"
              - "#ai"
            ---
            # Hello
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.Contains("project", result.Tags);
        Assert.Contains("ai", result.Tags);
        Assert.DoesNotContain("#project", result.Tags);
    }

    [Fact]
    public void Parse_AliasesAsList_ParsesCorrectly()
    {
        var content = """
            ---
            aliases:
              - My Note
              - Alternative Name
            ---
            # Hello
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.Equal(2, result.Aliases.Count);
        Assert.Contains("My Note", result.Aliases);
        Assert.Contains("Alternative Name", result.Aliases);
    }

    [Fact]
    public void Parse_AliasesInline_ParsesCorrectly()
    {
        var content = """
            ---
            aliases: [My Note, Alternative Name]
            ---
            # Hello
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.Equal(2, result.Aliases.Count);
        Assert.Contains("My Note", result.Aliases);
    }

    [Fact]
    public void Parse_CreatedField_ParsesAsDate()
    {
        var content = """
            ---
            created: 2024-03-15
            ---
            # Hello
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.NotNull(result.Date);
        Assert.Equal(new DateOnly(2024, 3, 15), result.Date);
    }

    [Fact]
    public void Parse_ModifiedField_ParsesAsUpdated()
    {
        var content = """
            ---
            modified: 2024-06-20
            ---
            # Hello
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.NotNull(result.Updated);
        Assert.Equal(new DateOnly(2024, 6, 20), result.Updated);
    }

    [Fact]
    public void Parse_ExtraFields_PreservedInDictionary()
    {
        var content = """
            ---
            citekey: smith2024
            rating: 5
            custom_field: custom value
            ---
            # Hello
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.Equal(3, result.ExtraFields.Count);
        Assert.Equal("smith2024", result.ExtraFields["citekey"]);
        Assert.Equal("5", result.ExtraFields["rating"]);
        Assert.Equal("custom value", result.ExtraFields["custom_field"]);
    }

    [Fact]
    public void Parse_ComplexFrontmatter_ParsesAll()
    {
        var content = """
            ---
            tags:
              - ai
              - research
            aliases:
              - My Research Note
            status: published
            type: literature
            domain: academic
            date: 2024-01-15
            updated: 2024-06-20
            citekey: smith2024
            ---
            # Research Note
            Body content here.
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.Equal(2, result.Tags.Count);
        Assert.Single(result.Aliases);
        Assert.Equal("published", result.Status);
        Assert.Equal("literature", result.NoteType);
        Assert.Equal("academic", result.Domain);
        Assert.Equal(new DateOnly(2024, 1, 15), result.Date);
        Assert.Equal(new DateOnly(2024, 6, 20), result.Updated);
        Assert.Equal("smith2024", result.ExtraFields["citekey"]);
    }

    [Fact]
    public void Parse_QuotedValues_StripsQuotes()
    {
        var content = """
            ---
            status: "draft"
            type: 'project'
            ---
            # Hello
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.Equal("draft", result.Status);
        Assert.Equal("project", result.NoteType);
    }

    [Fact]
    public void Parse_NoClosingDelimiter_ReturnsEmpty()
    {
        var content = """
            ---
            tags:
              - project
            # No closing delimiter
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.Same(Kioku.Mcp.Server.Domain.NoteMetadata.Empty, result);
    }

    [Fact]
    public void Parse_CaseInsensitiveKeys()
    {
        var content = """
            ---
            Tags:
              - project
            STATUS: draft
            Type: note
            ---
            # Hello
            """;

        var result = FrontmatterParser.Parse(content);

        Assert.Contains("project", result.Tags);
        Assert.Equal("draft", result.Status);
        Assert.Equal("note", result.NoteType);
    }

    [Fact]
    public void GetBodyStart_WithFrontmatter_ReturnsCorrectIndex()
    {
        var content = "---\ntags:\n  - project\n---\n# Hello World";

        var bodyStart = FrontmatterParser.GetBodyStart(content);

        Assert.True(bodyStart > 0);
        Assert.Equal("# Hello World", content[bodyStart..]);
    }

    [Fact]
    public void GetBodyStart_NoFrontmatter_ReturnsZero()
    {
        var content = "# Hello World\n\nSome content.";

        var bodyStart = FrontmatterParser.GetBodyStart(content);

        Assert.Equal(0, bodyStart);
    }

    [Fact]
    public void GetBodyStart_WindowsLineEndings_ReturnsCorrectIndex()
    {
        var content = "---\r\ntags:\r\n  - project\r\n---\r\n# Hello World";

        var bodyStart = FrontmatterParser.GetBodyStart(content);

        Assert.Equal("# Hello World", content[bodyStart..]);
    }
}
