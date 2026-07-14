using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Tests for NoteChunker's heading-aware split: the fast path (short notes embedded whole,
/// byte-for-byte identical to today), heading breadcrumbs (including the level-stack pop
/// between sibling headings and the note-name collapse), oversized-section windowing, and
/// the small-section merge pass.
/// </summary>
public class NoteChunkerTests
{
    private static Note MakeNote(string name, string rawContent)
    {
        var bodyStart = FrontmatterParser.GetBodyStart(rawContent);
        return new Note
        {
            FilePath = $"{name}.md",
            VaultRelativePath = $"{name}.md",
            Name = name,
            RawContent = rawContent,
            PlainText = MarkdownTextExtractor.Extract(rawContent, bodyStart),
            ContentHash = "hash",
        };
    }

    [Fact]
    public void Chunk_ShortNote_ReturnsSingleChunkIdenticalToWholeNoteText()
    {
        var note = MakeNote("Short Note", "# Short Note\n\nJust a little text.");

        var chunks = NoteChunker.Chunk(note);

        Assert.Single(chunks);
        Assert.Equal("", chunks[0].HeadingPath);
        Assert.Equal(NoteChunker.BuildWholeNoteText(note), chunks[0].Text);
    }

    [Fact]
    public void Chunk_NoteOverThreshold_SplitsByHeadingWithBreadcrumbsAndCollapsesDuplicateNoteName()
    {
        var rawContent = """
            # Project X

            Intro paragraph before any sub-heading, describing the note at a high level.

            ## Architecture

            Architecture details go here, several words to fill this section nicely.

            ### Database

            Database specific notes, enough text to be a real section on its own here.

            ## Deployment

            Deployment notes, enough words to be a real section on its own as well.
            """;
        var note = MakeNote("Project X", rawContent);

        var chunks = NoteChunker.Chunk(note, maxChars: 150);

        Assert.Equal(4, chunks.Count);
        Assert.Equal("Project X", chunks[0].HeadingPath);
        Assert.Contains("Intro paragraph", chunks[0].Text);
        Assert.Equal("Project X > Architecture", chunks[1].HeadingPath);
        Assert.Contains("Architecture details", chunks[1].Text);
        Assert.Equal("Project X > Architecture > Database", chunks[2].HeadingPath);
        Assert.Contains("Database specific notes", chunks[2].Text);
        // A sibling H2 under the same H1 must pop the H3 (and the previous H2) off the stack.
        Assert.Equal("Project X > Deployment", chunks[3].HeadingPath);
        Assert.Contains("Deployment notes", chunks[3].Text);
    }

    [Fact]
    public void Chunk_SiblingHeadingsAtSameLevel_PopStackIndependently()
    {
        var rawContent = """
            # Glossary

            ## Networking

            ### DNS
            Resolves names to addresses, enough filler text to form its own section here.
            ### TCP
            Reliable ordered delivery, enough filler text to form its own section here too.

            ## Storage

            ### Cache
            Fast volatile storage, enough filler text to form its own section right here.
            """;
        var note = MakeNote("Glossary", rawContent);

        var chunks = NoteChunker.Chunk(note, maxChars: 50);

        var headingPaths = chunks.Select(c => c.HeadingPath).ToList();
        Assert.Contains("Glossary > Networking > DNS", headingPaths);
        Assert.Contains("Glossary > Networking > TCP", headingPaths);
        Assert.Contains("Glossary > Storage > Cache", headingPaths);
        // TCP (sibling of DNS under Networking) must not inherit DNS in its path.
        Assert.DoesNotContain("Glossary > Networking > DNS > TCP", headingPaths);
        // Cache (under a new H2 "Storage") must not inherit the previous H2/H3 branch.
        Assert.DoesNotContain("Glossary > Networking > Storage > Cache", headingPaths);
    }

    [Fact]
    public void Chunk_SectionLargerThanMaxChars_SplitsIntoMultipleWindowsWithSameHeadingPath()
    {
        var bigSection = string.Concat(Enumerable.Repeat("word ", 200)); // ~1000 chars
        var rawContent = $"# Big Note\n\n## Huge Section\n\n{bigSection}";
        var note = MakeNote("Big Note", rawContent);

        var chunks = NoteChunker.Chunk(note, maxChars: 100);

        var hugeSectionChunks = chunks.Where(c => c.HeadingPath == "Big Note > Huge Section").ToList();
        Assert.True(hugeSectionChunks.Count > 1, "expected the oversized section to be split into multiple windows");
        Assert.All(hugeSectionChunks, c => Assert.True(c.Text.Length <= 100));
    }

    [Fact]
    public void Chunk_ManyTinyAdjacentSections_AreMergedIntoFewerChunks()
    {
        var headings = Enumerable.Range(1, 10).Select(i => $"### Term {i}\nShort definition {i}.");
        var rawContent = "# Glossary\n\n" + string.Join("\n\n", headings);
        var note = MakeNote("Glossary", rawContent);

        var chunks = NoteChunker.Chunk(note, maxChars: 400);

        Assert.True(chunks.Count < 10, $"expected tiny sections to be merged, got {chunks.Count} chunks");
        // Nothing should be lost by merging.
        Assert.All(Enumerable.Range(1, 10), i => Assert.Contains(chunks, c => c.Text.Contains($"Short definition {i}.")));
    }

    [Fact]
    public void Chunk_NoteWithNoHeadingsOverThreshold_FallsBackToWindowsCarryingNoteName()
    {
        var body = string.Concat(Enumerable.Repeat("word ", 100)); // ~500 chars, no headings
        var note = MakeNote("Plain Note", body);

        var chunks = NoteChunker.Chunk(note, maxChars: 100);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.Equal("Plain Note", c.HeadingPath));
    }

    [Fact]
    public void Chunk_SameNoteTwice_IsDeterministic()
    {
        var rawContent = """
            # Deterministic

            ## Section A
            Some content for section A that is reasonably descriptive.

            ## Section B
            Some content for section B that is reasonably descriptive too.
            """;
        var note = MakeNote("Deterministic", rawContent);

        var first = NoteChunker.Chunk(note, maxChars: 50);
        var second = NoteChunker.Chunk(note, maxChars: 50);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Chunk_RealLongFixtureNote_IsolatesTheBuriedFactIntoItsOwnChunk()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "EvalVault", "Referencias", "Historia de la Computacion.md");
        var rawContent = File.ReadAllText(path);
        var note = MakeNote("Historia de la Computacion", rawContent);

        var chunks = NoteChunker.Chunk(note);

        Assert.True(chunks.Count > 1, "expected the long fixture note to be split into multiple chunks");
        var factChunk = Assert.Single(chunks, c => c.Text.Contains("Kioku-7"));
        Assert.Contains("Dato final del curso", factChunk.HeadingPath);
        Assert.True(factChunk.Text.Length <= NoteChunker.DefaultMaxChars);
    }
}
