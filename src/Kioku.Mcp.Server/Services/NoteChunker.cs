using System.Text;
using Kioku.Mcp.Server.Domain;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Kioku.Mcp.Server.Services;

/// <summary>A section of a note ready to be embedded, with its heading breadcrumb.</summary>
/// <param name="HeadingPath">
/// "{NoteName} &gt; {Heading} &gt; {Sub-heading}..." (or just "{NoteName}" for a headless
/// section) for the slow/chunked path, or "" for the whole-note fast path — where the note
/// name is already the first line of <see cref="NoteChunker.BuildWholeNoteText"/>, so no
/// separate prefix is needed.
/// </param>
public sealed record NoteChunk(string HeadingPath, string Text);

/// <summary>
/// Splits a note into embeddable chunks. Most notes are short enough to embed whole (the
/// "fast path", byte-for-byte identical to embedding the note directly); only notes whose
/// text exceeds <see cref="DefaultMaxChars"/> get split by heading into smaller, more
/// topically focused sections ("parent-document retrieval": each section is embedded on
/// its own, with a deterministic breadcrumb prefix carrying the note/heading context that
/// would otherwise be lost once the section is embedded in isolation).
/// </summary>
public static class NoteChunker
{
    /// <summary>
    /// Conservative ceiling under nomic-embed-text's real 2048-token limit (confirmed via
    /// Ollama: exceeding it returns a hard "input length exceeds the context length" error,
    /// not a silent truncation). Mixed Spanish/English vault content fragments to roughly
    /// 3-3.5 chars/token, plus the "search_document: " task prefix — this is a heuristic to
    /// validate against real Ollama with scripts/Kioku.Eval, not a hard guarantee for every
    /// embedding model.
    /// </summary>
    public const int DefaultMaxChars = 4000;

    /// <summary>
    /// The exact text embedded for a whole, unchunked note: name + metadata (tags, aliases,
    /// status, type, domain, dates, extra fields) + full plain text.
    /// </summary>
    public static string BuildWholeNoteText(Note note)
    {
        var sb = new StringBuilder();
        sb.AppendLine(note.Name);

        var m = note.Metadata;
        if (m.Tags.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Tags: {string.Join(", ", m.Tags)}");
        }

        if (m.Aliases.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Aliases: {string.Join(", ", m.Aliases)}");
        }

        if (m.Status is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Status: {m.Status}");
        }

        if (m.NoteType is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Type: {m.NoteType}");
        }

        if (m.Domain is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Domain: {m.Domain}");
        }

        if (m.Date.HasValue)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Date: {m.Date:yyyy-MM-dd}");
        }

        if (m.Updated.HasValue)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Updated: {m.Updated:yyyy-MM-dd}");
        }

        foreach (var (k, v) in m.ExtraFields)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"{k}: {v}");
        }

        if (!string.IsNullOrWhiteSpace(note.PlainText))
        {
            sb.AppendLine();
            sb.Append(note.PlainText);
        }

        return sb.ToString();
    }

    /// <summary>Splits a note into one or more embeddable chunks.</summary>
    public static IReadOnlyList<NoteChunk> Chunk(Note note, int maxChars = DefaultMaxChars)
    {
        var wholeNoteText = BuildWholeNoteText(note);
        if (wholeNoteText.Length <= maxChars)
        {
            return [new NoteChunk("", wholeNoteText)];
        }

        var bodyStart = FrontmatterParser.GetBodyStart(note.RawContent);
        var body = note.RawContent[bodyStart..];
        var headings = Markdown.Parse(body).Descendants<HeadingBlock>().OrderBy(h => h.Span.Start).ToList();

        var sections = headings.Count == 0
            ? SplitIntoWindows(note.Name, MarkdownTextExtractor.Extract(body, 0), maxChars)
            : BuildHeadingSections(note.Name, body, headings, maxChars);

        var merged = MergeSmallSections(sections, maxChars);
        return merged.Count > 0 ? merged : [new NoteChunk("", wholeNoteText)];
    }

    private static List<NoteChunk> BuildHeadingSections(
        string noteName, string body, List<HeadingBlock> headings, int maxChars)
    {
        var sections = new List<NoteChunk>();

        var preambleEnd = headings[0].Span.Start;
        if (preambleEnd > 0)
        {
            AddSection(sections, noteName, body[..preambleEnd], maxChars);
        }

        var stack = new List<(int Level, string Title)>();
        for (int i = 0; i < headings.Count; i++)
        {
            var heading = headings[i];
            while (stack.Count > 0 && stack[^1].Level >= heading.Level)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            stack.Add((heading.Level, ExtractHeadingTitle(heading)));

            var crumbs = stack.Select(s => s.Title).ToList();
            if (crumbs.Count > 0 && crumbs[0].Equals(noteName, StringComparison.OrdinalIgnoreCase))
            {
                crumbs.RemoveAt(0);
            }

            var headingPath = crumbs.Count > 0
                ? $"{noteName} > {string.Join(" > ", crumbs)}"
                : noteName;

            var sectionStart = Math.Min(heading.Span.End + 1, body.Length);
            var sectionEnd = i + 1 < headings.Count ? headings[i + 1].Span.Start : body.Length;
            if (sectionEnd > sectionStart)
            {
                AddSection(sections, headingPath, body[sectionStart..sectionEnd], maxChars);
            }
        }

        return sections;
    }

    private static void AddSection(List<NoteChunk> sections, string headingPath, string rawSection, int maxChars)
    {
        var text = MarkdownTextExtractor.Extract(rawSection, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.Length <= maxChars)
        {
            sections.Add(new NoteChunk(headingPath, text));
        }
        else
        {
            sections.AddRange(SplitIntoWindows(headingPath, text, maxChars));
        }
    }

    /// <summary>
    /// Splits into roughly EQUAL-sized windows (not greedy maxChars-sized windows with a
    /// small leftover tail) so an oversized section never leaves a tiny orphan remainder
    /// that <see cref="MergeSmallSections"/> could then fuse into a wholly unrelated,
    /// differently-headed section right after it.
    /// </summary>
    private static List<NoteChunk> SplitIntoWindows(string headingPath, string text, int maxChars)
    {
        var windows = new List<NoteChunk>();
        var windowCount = Math.Max(1, (int)Math.Ceiling(text.Length / (double)maxChars));
        var windowSize = (int)Math.Ceiling(text.Length / (double)windowCount);

        for (int pos = 0; pos < text.Length; pos += windowSize)
        {
            var window = text.Substring(pos, Math.Min(windowSize, text.Length - pos));
            if (!string.IsNullOrWhiteSpace(window))
            {
                windows.Add(new NoteChunk(headingPath, window));
            }
        }

        return windows;
    }

    /// <summary>
    /// Greedily coalesces consecutive small sections so a note with many short headings
    /// (e.g. a glossary of one-line definitions) doesn't explode into excessive tiny
    /// chunks/embedding calls. The first section's heading path wins for the merged group.
    /// </summary>
    private static List<NoteChunk> MergeSmallSections(List<NoteChunk> sections, int maxChars)
    {
        if (sections.Count == 0)
        {
            return sections;
        }

        var minMergedChars = maxChars / 4;
        var merged = new List<NoteChunk>();
        var pendingHeadingPath = sections[0].HeadingPath;
        var pendingText = new StringBuilder(sections[0].Text);

        for (int i = 1; i < sections.Count; i++)
        {
            var section = sections[i];
            var combinedLength = pendingText.Length + 2 + section.Text.Length;

            // Keep coalescing while the running buffer is still small and the merge
            // wouldn't push it over the embedding size ceiling.
            if (pendingText.Length < minMergedChars && combinedLength <= maxChars)
            {
                pendingText.Append('\n').Append('\n').Append(section.Text);
            }
            else
            {
                merged.Add(new NoteChunk(pendingHeadingPath, pendingText.ToString()));
                pendingHeadingPath = section.HeadingPath;
                pendingText = new StringBuilder(section.Text);
            }
        }

        merged.Add(new NoteChunk(pendingHeadingPath, pendingText.ToString()));
        return merged;
    }

    private static string ExtractHeadingTitle(HeadingBlock heading)
    {
        var sb = new StringBuilder();
        WalkInline(heading.Inline?.FirstChild, sb);
        return sb.ToString().Trim();
    }

    private static void WalkInline(Inline? inline, StringBuilder sb)
    {
        for (var i = inline; i is not null; i = i.NextSibling)
        {
            switch (i)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case ContainerInline container:
                    WalkInline(container.FirstChild, sb);
                    break;
            }
        }
    }
}
