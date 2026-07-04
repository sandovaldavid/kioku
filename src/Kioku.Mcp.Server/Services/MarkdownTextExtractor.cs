using System.Buffers;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Extracts clean text from Obsidian Markdown notes.
/// Removes frontmatter, Markdown syntax, and wikilinks to produce
/// indexable text for the search engine.
/// No external dependencies — manual parsing with Span&lt;char&gt;.
/// </summary>
public static class MarkdownTextExtractor
{
    private static readonly SearchValues<char> AlphaNum = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
    /// <summary>
    /// Extracts plain text from a Markdown note.
    /// </summary>
    /// <param name="rawContent">Complete content of the .md file</param>
    /// <param name="bodyStart">Index where the body starts (after the frontmatter).
    /// Use FrontmatterParser.GetBodyStart() to obtain it.</param>
    /// <returns>Clean text ready for indexing and search.</returns>
    public static string Extract(string rawContent, int bodyStart = 0)
    {
        if (rawContent.Length == 0)
        {
            return string.Empty;
        }

        var body = rawContent.AsSpan(bodyStart);
        var sb = new System.Text.StringBuilder(body.Length);

        int pos = 0;
        while (pos < body.Length)
        {
            int lineEnd = body[pos..].IndexOfAny('\n', '\r');
            var line = lineEnd < 0 ? body[pos..] : body.Slice(pos, lineEnd);

            ProcessLine(line, sb);

            pos += lineEnd < 0 ? body.Length - pos : lineEnd + 1;
            if (pos < body.Length && body[pos - 1] == '\r' && body[pos] == '\n')
            {
                pos++;
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Extracts all [[target]] wikilinks from the content of a note.
    /// </summary>
    public static IReadOnlyList<string> ExtractWikilinks(string content)
    {
        var links = new List<string>();
        var span = content.AsSpan();
        int pos = 0;

        while (pos < span.Length - 3)
        {
            int open = span[pos..].IndexOf("[[".AsSpan(), StringComparison.Ordinal);
            if (open < 0)
            {
                break;
            }

            int absOpen = pos + open + 2;
            int close = span[absOpen..].IndexOf("]]".AsSpan(), StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            var link = span.Slice(absOpen, close);
            // Support for [[note|alias]] — we only extract the target
            int pipeIdx = link.IndexOf('|');
            var target = pipeIdx >= 0 ? link[..pipeIdx] : link;

            // Support for [[note#header]] — we only extract the file
            int hashIdx = target.IndexOf('#');
            if (hashIdx >= 0)
            {
                target = target[..hashIdx];
            }

            var targetStr = target.Trim().ToString();
            if (!string.IsNullOrWhiteSpace(targetStr))
            {
                links.Add(targetStr);
            }

            pos = absOpen + close + 2;
        }

        return links;
    }

    // Private helpers

    private static void ProcessLine(ReadOnlySpan<char> line, System.Text.StringBuilder sb)
    {
        if (line.IsEmpty)
        {
            sb.AppendLine();
            return;
        }

        // Skip code blocks (``` ... ```)
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("```".AsSpan()) || trimmed.StartsWith("~~~".AsSpan()))
        {
            // We do not add the opening/closing lines of code blocks
            return;
        }

        // Skip Markdown table lines (| col1 | col2 |)
        if (trimmed.StartsWith("|".AsSpan()))
        {
            // Extract only the cell text
            ExtractTableCellText(trimmed, sb);
            sb.AppendLine();
            return;
        }

        // Process the line removing Markdown syntax
        CleanLine(line, sb);
        sb.AppendLine();
    }

    private static void CleanLine(ReadOnlySpan<char> line, System.Text.StringBuilder sb)
    {
        int pos = 0;

        // Remove header prefixes (# ## ### etc.)
        if (line.TrimStart().StartsWith("#".AsSpan()))
        {
            while (pos < line.Length && (line[pos] == '#' || line[pos] == ' '))
            {
                pos++;
            }
        }

        // Remove list prefixes (- * + 1.)
        var lineAfterHeading = line[pos..].TrimStart();
        if (lineAfterHeading.Length > 0 && (lineAfterHeading[0] == '-' || lineAfterHeading[0] == '*' || lineAfterHeading[0] == '+'))
        {
            var afterBullet = lineAfterHeading[1..].TrimStart();
            ProcessInlineMarkdown(afterBullet, sb);
            return;
        }

        ProcessInlineMarkdown(line[pos..], sb);
    }

    private static void ProcessInlineMarkdown(ReadOnlySpan<char> text, System.Text.StringBuilder sb)
    {
        int pos = 0;
        while (pos < text.Length)
        {
            char c = text[pos];

            // Wikilinks: [[target|alias]] → alias (or target if no alias)
            if (c == '[' && pos + 1 < text.Length && text[pos + 1] == '[')
            {
                int close = text[(pos + 2)..].IndexOf("]]".AsSpan(), StringComparison.Ordinal);
                if (close >= 0)
                {
                    var inner = text.Slice(pos + 2, close);
                    int pipe = inner.IndexOf('|');
                    sb.Append(pipe >= 0 ? inner[(pipe + 1)..] : inner);
                    pos += close + 4;
                    continue;
                }
            }

            // Markdown links: [text](url) → text
            if (c == '[' && pos + 1 < text.Length)
            {
                int closeBracket = text[(pos + 1)..].IndexOf(']');
                if (closeBracket >= 0)
                {
                    int parenOpen = pos + 1 + closeBracket + 1;
                    if (parenOpen < text.Length && text[parenOpen] == '(')
                    {
                        int parenClose = text[parenOpen..].IndexOf(')');
                        if (parenClose >= 0)
                        {
                            sb.Append(text.Slice(pos + 1, closeBracket));
                            pos = parenOpen + parenClose + 1;
                            continue;
                        }
                    }
                }
            }

            // Bold/italic: **text** or *text* or __text__ or _text_ → text
            if ((c == '*' || c == '_') && pos + 1 < text.Length)
            {
                bool isDouble = pos + 1 < text.Length && text[pos + 1] == c;
                var marker = isDouble ? text.Slice(pos, 2) : text.Slice(pos, 1);
                int closeMarker = text[(pos + marker.Length)..].IndexOf(marker, StringComparison.Ordinal);
                if (closeMarker >= 0)
                {
                    sb.Append(text.Slice(pos + marker.Length, closeMarker));
                    pos += marker.Length + closeMarker + marker.Length;
                    continue;
                }
            }

            // Inline code: `code` → delete
            if (c == '`')
            {
                int close = text[(pos + 1)..].IndexOf('`');
                if (close >= 0)
                {
                    // Include the code text so it is indexable
                    sb.Append(text.Slice(pos + 1, close));
                    pos += close + 2;
                    continue;
                }
            }

            sb.Append(c);
            pos++;
        }
    }

    private static void ExtractTableCellText(ReadOnlySpan<char> line, System.Text.StringBuilder sb)
    {
        // Skip table separator lines (| --- | --- |)
        // A separator line contains '-' but no alphanumeric characters
        bool hasDash = line.Contains('-');
        bool hasAlphaNum = false;
        foreach (char ch in line)
        {
            if (char.IsLetterOrDigit(ch)) { hasAlphaNum = true; break; }
        }
        if (hasDash && !hasAlphaNum)
        {
            return;
        }

        bool firstCell = true;
        int pos = 0;

        // Skip leading '|' so pos points to the start of the first cell
        if (line.Length > 0 && line[0] == '|')
        {
            pos = 1;
        }

        while (pos < line.Length)
        {
            int nextPipe = line[pos..].IndexOf('|');
            if (nextPipe < 0)
            {
                break;
            }

            var cell = line.Slice(pos, nextPipe);
            var cellTrimmed = cell.Trim();
            bool isSeparator = !cellTrimmed.IsEmpty && cellTrimmed.IndexOf('-') >= 0
                && !cellTrimmed.ContainsAny(AlphaNum);
            if (!cellTrimmed.IsEmpty && !isSeparator)
            {
                if (!firstCell)
                {
                    sb.Append(' ');
                }

                ProcessInlineMarkdown(cellTrimmed, sb);
                firstCell = false;
            }

            pos = pos + nextPipe + 1;
        }
    }
}
