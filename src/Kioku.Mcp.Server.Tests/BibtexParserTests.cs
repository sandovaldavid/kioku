using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class BibtexParserTests
{
    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Parse_SampleLibrary_ParsesAllThreeEntriesAndSkipsCommentBlock()
    {
        var result = BibtexParser.Parse(ReadFixture("sample-library.bib"));

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal(
            ["garcia2021distributed", "turing1950computing", "nakamoto2008bitcoin"],
            result.Entries.Select(e => e.CiteKey));
    }

    [Fact]
    public void Parse_LatexAccentEscapes_NormalizedToUnicode()
    {
        var result = BibtexParser.Parse(ReadFixture("sample-library.bib"));
        var entry = result.Entries.Single(e => e.CiteKey == "garcia2021distributed");

        Assert.Equal("García, María and Müller, Hans", entry.Fields["author"]);
    }

    [Fact]
    public void Parse_DoubleDashInTitle_NormalizedToEnDash()
    {
        var result = BibtexParser.Parse(ReadFixture("sample-library.bib"));
        var entry = result.Entries.Single(e => e.CiteKey == "garcia2021distributed");

        Assert.Equal("Distributed Consensus in Practice – A Field Study", entry.Fields["title"]);
        Assert.Equal("201–230", entry.Fields["pages"]);
    }

    [Fact]
    public void Parse_NestedBraces_StripsProtectiveBracesFromValue()
    {
        var result = BibtexParser.Parse(ReadFixture("sample-library.bib"));
        var entry = result.Entries.Single(e => e.CiteKey == "turing1950computing");

        Assert.Equal("Computing Machinery and Intelligence", entry.Fields["title"]);
    }

    [Fact]
    public void Parse_QuotedFieldValues_ParsedLikeBracedValues()
    {
        var result = BibtexParser.Parse(ReadFixture("sample-library.bib"));
        var entry = result.Entries.Single(e => e.CiteKey == "nakamoto2008bitcoin");

        Assert.Equal("Nakamoto, Satoshi", entry.Fields["author"]);
        Assert.Equal("Bitcoin: A Peer-to-Peer Electronic Cash System", entry.Fields["title"]);
    }

    [Fact]
    public void Parse_BareTokenFieldValue_ParsedWithoutDelimiters()
    {
        var result = BibtexParser.Parse(ReadFixture("sample-library.bib"));
        var entry = result.Entries.Single(e => e.CiteKey == "nakamoto2008bitcoin");

        Assert.Equal("2008", entry.Fields["year"]);
    }

    [Fact]
    public void Parse_MalformedEntries_ReportsErrorsWithoutLosingTheGoodEntry()
    {
        var result = BibtexParser.Parse(ReadFixture("malformed.bib"));

        var good = Assert.Single(result.Entries);
        Assert.Equal("good2022entry", good.CiteKey);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsNoEntriesAndNoErrors()
    {
        var result = BibtexParser.Parse("");

        Assert.Empty(result.Entries);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_TrailingCommaBeforeClosingBrace_DoesNotBreakParsing()
    {
        // The 'garcia2021distributed' entry in the fixture ends its last field with a
        // trailing comma before '}' — a common BibTeX/Zotero export convention.
        var result = BibtexParser.Parse(ReadFixture("sample-library.bib"));

        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("{\\'e}", "é")]
    [InlineData("\\'e", "é")]
    [InlineData("{\\~n}", "ñ")]
    [InlineData("{\\c{c}}", "ç")]
    [InlineData("{\\ss}", "ß")]
    [InlineData("caf{\\'e} au lait", "café au lait")]
    public void NormalizeLatexEscapes_CommonAccents_ConvertsToUnicode(string input, string expected)
    {
        Assert.Equal(expected, BibtexParser.NormalizeLatexEscapes(input));
    }

    [Fact]
    public void NormalizeLatexEscapes_UnrecognizedBraces_AreStripped()
    {
        Assert.Equal("The ACM Handbook", BibtexParser.NormalizeLatexEscapes("The {ACM} Handbook"));
    }
}
