using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// YAML frontmatter parser for Obsidian notes.
/// Manual implementation with Span&lt;char&gt; — zero-allocation, no external dependencies.
/// Supports the most common Obsidian frontmatter formats.
/// </summary>
public static class FrontmatterParser
{
    private const string Delimiter = "---";

    /// <summary>
    /// Extracts the YAML frontmatter and metadata from a note.
    /// </summary>
    /// <param name="content">Complete content of the .md file</param>
    /// <returns>Parsed metadata, or NoteMetadata.Empty if there is no frontmatter.</returns>
    public static NoteMetadata Parse(string content)
    {
        var span = content.AsSpan();

        // Frontmatter must start exactly on the first line
        if (!span.StartsWith(Delimiter.AsSpan(), StringComparison.Ordinal))
        {
            return NoteMetadata.Empty;
        }

        // Find the closing delimiter of the frontmatter (second occurrence of "---")
        int start = Delimiter.Length;
        // Skip possible \r\n or \n after the first ---
        if (start < span.Length && span[start] == '\r')
        {
            start++;
        }

        if (start < span.Length && span[start] == '\n')
        {
            start++;
        }

        int closeIndex = FindClosingDelimiter(span, start);
        if (closeIndex < 0)
        {
            return NoteMetadata.Empty;
        }

        var frontmatterSpan = span[start..closeIndex];
        return ParseFields(frontmatterSpan);
    }

    /// <summary>
    /// Returns the index where the body of the note starts (after the frontmatter).
    /// Returns 0 if there is no frontmatter.
    /// </summary>
    public static int GetBodyStart(string content)
    {
        var span = content.AsSpan();
        if (!span.StartsWith(Delimiter.AsSpan(), StringComparison.Ordinal))
        {
            return 0;
        }

        int start = Delimiter.Length;
        if (start < span.Length && span[start] == '\r')
        {
            start++;
        }

        if (start < span.Length && span[start] == '\n')
        {
            start++;
        }

        int closeIndex = FindClosingDelimiter(span, start);
        if (closeIndex < 0)
        {
            return 0;
        }

        // Skip the closing "---" and the following newline
        int bodyStart = closeIndex + Delimiter.Length;
        if (bodyStart < span.Length && span[bodyStart] == '\r')
        {
            bodyStart++;
        }

        if (bodyStart < span.Length && span[bodyStart] == '\n')
        {
            bodyStart++;
        }

        return bodyStart;
    }

    // Private helpers

    private static int FindClosingDelimiter(ReadOnlySpan<char> span, int searchFrom)
    {
        int pos = searchFrom;
        while (pos < span.Length)
        {
            int lineEnd = span[pos..].IndexOfAny('\n', '\r');
            ReadOnlySpan<char> line = lineEnd < 0
                ? span[pos..]
                : span.Slice(pos, lineEnd);

            if (line.TrimEnd().SequenceEqual(Delimiter.AsSpan()))
            {
                return pos;
            }

            pos += lineEnd < 0 ? span.Length - pos : lineEnd + 1;
            // Skip double \r\n
            if (pos < span.Length && span[pos - 1] == '\r' && span[pos] == '\n')
            {
                pos++;
            }
        }
        return -1;
    }

    private static NoteMetadata ParseFields(ReadOnlySpan<char> frontmatter)
    {
        var aliases = new List<string>();
        var tags = new List<string>();
        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DateOnly? date = null;
        DateOnly? updated = null;
        string? status = null;
        string? noteType = null;

        int pos = 0;
        while (pos < frontmatter.Length)
        {
            int lineEnd = frontmatter[pos..].IndexOfAny('\n', '\r');
            ReadOnlySpan<char> line = lineEnd < 0
                ? frontmatter[pos..]
                : frontmatter.Slice(pos, lineEnd);

            ProcessLine(line, aliases, tags, extra, ref date, ref updated, ref status, ref noteType);

            pos += lineEnd < 0 ? frontmatter.Length - pos : lineEnd + 1;
            if (pos < frontmatter.Length && frontmatter[pos - 1] == '\r' && frontmatter[pos] == '\n')
            {
                pos++;
            }
        }

        return new NoteMetadata
        {
            Aliases = aliases,
            Tags = tags,
            Date = date,
            Updated = updated,
            Status = status,
            NoteType = noteType,
            ExtraFields = extra,
        };
    }

    private static void ProcessLine(
        ReadOnlySpan<char> line,
        List<string> aliases, List<string> tags,
        Dictionary<string, string> extra,
        ref DateOnly? date, ref DateOnly? updated,
        ref string? status, ref string? noteType)
    {
        // Skip list lines (elements of multiline arrays like "  - value")
        // — we process them inline when we find the root key
        line = line.TrimEnd();
        if (line.IsEmpty)
        {
            return;
        }

        int colonIdx = line.IndexOf(':');
        if (colonIdx <= 0)
        {
            return;
        }

        var key = line[..colonIdx].Trim();
        var value = colonIdx + 1 < line.Length
            ? line[(colonIdx + 1)..].Trim()
            : ReadOnlySpan<char>.Empty;

        // Values in inline list format: key: [a, b, c]
        if (value.StartsWith("[".AsSpan()) && value.EndsWith("]".AsSpan()))
        {
            var items = ParseInlineList(value[1..^1]);
            AssignListField(key, items, aliases, tags, extra);
            return;
        }

        var keyStr = key.ToString();
        var valueStr = value.IsEmpty ? string.Empty : value.Trim('"').Trim('\'').ToString();

        switch (keyStr.ToLowerInvariant())
        {
            case "aliases" or "alias":
                if (!string.IsNullOrEmpty(valueStr))
                {
                    aliases.Add(valueStr);
                }

                break;
            case "tags" or "tag":
                if (!string.IsNullOrEmpty(valueStr))
                {
                    ParseTagsIntoList(valueStr, tags);
                }

                break;
            case "date" or "created":
                date = TryParseDate(valueStr);
                break;
            case "updated" or "modified":
                updated = TryParseDate(valueStr);
                break;
            case "status":
                status = string.IsNullOrEmpty(valueStr) ? null : valueStr;
                break;
            case "type":
                noteType = string.IsNullOrEmpty(valueStr) ? null : valueStr;
                break;
            default:
                if (!string.IsNullOrEmpty(valueStr))
                {
                    extra[keyStr] = valueStr;
                }

                break;
        }
    }

    private static List<string> ParseInlineList(ReadOnlySpan<char> inner)
    {
        var result = new List<string>();
        int pos = 0;
        while (pos < inner.Length)
        {
            int comma = inner[pos..].IndexOf(',');
            ReadOnlySpan<char> item = comma < 0
                ? inner[pos..]
                : inner.Slice(pos, comma);

            var trimmed = item.Trim().Trim('"').Trim('\'');
            if (!trimmed.IsEmpty)
            {
                result.Add(trimmed.ToString());
            }

            pos += comma < 0 ? inner.Length - pos : comma + 1;
        }
        return result;
    }

    private static void AssignListField(
        ReadOnlySpan<char> key,
        List<string> items,
        List<string> aliases, List<string> tags,
        Dictionary<string, string> extra)
    {
        var keyStr = key.ToString().ToLowerInvariant();
        switch (keyStr)
        {
            case "aliases" or "alias": aliases.AddRange(items); break;
            case "tags" or "tag": tags.AddRange(items); break;
            default:
                if (items.Count > 0)
                {
                    extra[key.ToString()] = string.Join(", ", items);
                }

                break;
        }
    }

    private static void ParseTagsIntoList(string value, List<string> tags)
    {
        // Support for "tag1, tag2" or "tag1 tag2"
        foreach (var tag in value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            tags.Add(tag.Trim('#').Trim());
        }
    }

    private static DateOnly? TryParseDate(string value)
    {
        if (DateOnly.TryParse(value, out var result))
        {
            return result;
        }

        return null;
    }
}
