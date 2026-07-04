using System.Text.RegularExpressions;

namespace Kioku.Mcp.Server.Services;

/// <summary>A single parsed BibTeX entry (e.g. @article{smith2023, ...}).</summary>
public sealed record BibtexEntry(
    string Type,
    string CiteKey,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>Result of parsing a BibTeX document — entries found, plus a per-entry error report.</summary>
public sealed record BibtexParseResult(
    IReadOnlyList<BibtexEntry> Entries,
    IReadOnlyList<string> Errors);

/// <summary>
/// Minimal, tolerant BibTeX parser: handles @article/@book/@inproceedings/... entries with
/// nested-brace field values, quoted values, line comments, and the most common LaTeX accent
/// escapes. A malformed entry is reported and skipped — it never aborts the whole import.
/// No external dependencies, manual scanning in the style of FrontmatterParser.
/// </summary>
public static partial class BibtexParser
{
    private static readonly HashSet<string> NonEntryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "comment", "string", "preamble",
    };

    public static BibtexParseResult Parse(string content)
    {
        var cleaned = StripLineComments(content);
        var entries = new List<BibtexEntry>();
        var errors = new List<string>();

        var pos = 0;
        while (true)
        {
            var at = cleaned.IndexOf('@', pos);
            if (at < 0)
            {
                break;
            }

            var scanPos = at + 1;
            var typeStart = scanPos;
            while (scanPos < cleaned.Length && char.IsLetter(cleaned[scanPos]))
            {
                scanPos++;
            }

            var type = cleaned[typeStart..scanPos];
            if (type.Length == 0)
            {
                pos = at + 1;
                continue;
            }

            SkipWhitespace(cleaned, ref scanPos);
            if (scanPos >= cleaned.Length || cleaned[scanPos] != '{')
            {
                errors.Add($"Expected '{{' after @{type} (position {at}).");
                pos = at + 1;
                continue;
            }

            if (NonEntryTypes.Contains(type))
            {
                if (!TrySkipBalancedBraces(cleaned, ref scanPos))
                {
                    errors.Add($"Unbalanced braces in @{type} block (position {at}).");
                }

                pos = scanPos;
                continue;
            }

            if (TryParseEntryBody(cleaned, type.ToLowerInvariant(), ref scanPos, out var entry, out var error))
            {
                entries.Add(entry!);
            }
            else
            {
                errors.Add($"{error} (entry starting at position {at}).");
            }

            pos = scanPos;
        }

        return new BibtexParseResult(entries, errors);
    }

    // Private helpers

    private static bool TryParseEntryBody(
        string content, string type, ref int pos, out BibtexEntry? entry, out string? error)
    {
        entry = null;
        error = null;

        // pos is at the entry's opening '{'.
        pos++;

        var keyStart = pos;
        while (pos < content.Length && content[pos] != ',' && content[pos] != '}')
        {
            pos++;
        }

        if (pos >= content.Length)
        {
            error = $"Unterminated @{type} entry";
            return false;
        }

        var citeKey = content[keyStart..pos].Trim();
        if (citeKey.Length == 0)
        {
            error = $"@{type} entry has no citekey";
            return false;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            SkipWhitespaceAndCommas(content, ref pos);
            if (pos >= content.Length)
            {
                error = $"Unterminated entry '{citeKey}'";
                return false;
            }

            if (content[pos] == '}')
            {
                pos++;
                break;
            }

            var nameStart = pos;
            while (pos < content.Length && content[pos] != '=' && !char.IsWhiteSpace(content[pos]) && content[pos] != '}')
            {
                pos++;
            }

            var fieldName = content[nameStart..pos].Trim().ToLowerInvariant();
            if (fieldName.Length == 0)
            {
                error = $"Malformed field name in entry '{citeKey}'";
                return false;
            }

            SkipWhitespace(content, ref pos);
            if (pos >= content.Length || content[pos] != '=')
            {
                error = $"Expected '=' after field '{fieldName}' in entry '{citeKey}'";
                return false;
            }

            pos++;
            SkipWhitespace(content, ref pos);

            if (!TryExtractFieldValue(content, ref pos, out var rawValue, out var valueError))
            {
                error = $"{valueError} (field '{fieldName}' in entry '{citeKey}')";
                return false;
            }

            fields[fieldName] = NormalizeLatexEscapes(rawValue!);
            SkipWhitespace(content, ref pos);
        }

        entry = new BibtexEntry(type, citeKey, fields);
        return true;
    }

    private static bool TryExtractFieldValue(string content, ref int pos, out string? value, out string? error)
    {
        value = null;
        error = null;

        if (pos >= content.Length)
        {
            error = "Unexpected end of input while reading field value";
            return false;
        }

        if (content[pos] == '{')
        {
            var start = pos;
            if (!TrySkipBalancedBraces(content, ref pos))
            {
                error = "Unbalanced braces in field value";
                return false;
            }

            value = content[(start + 1)..(pos - 1)];
            return true;
        }

        if (content[pos] == '"')
        {
            var start = ++pos;
            while (pos < content.Length && content[pos] != '"')
            {
                pos++;
            }

            if (pos >= content.Length)
            {
                error = "Unterminated quoted field value";
                return false;
            }

            value = content[start..pos];
            pos++; // skip closing quote
            return true;
        }

        // Bare token (e.g. year = 2023, or a @string macro reference).
        var bareStart = pos;
        while (pos < content.Length && content[pos] != ',' && content[pos] != '}' && !char.IsWhiteSpace(content[pos]))
        {
            pos++;
        }

        value = content[bareStart..pos];
        return true;
    }

    private static bool TrySkipBalancedBraces(string content, ref int pos)
    {
        // pos is at the opening '{'.
        var depth = 0;
        do
        {
            if (pos >= content.Length)
            {
                return false;
            }

            if (content[pos] == '{')
            {
                depth++;
            }
            else if (content[pos] == '}')
            {
                depth--;
            }

            pos++;
        } while (depth > 0);

        return true;
    }

    private static void SkipWhitespace(string content, ref int pos)
    {
        while (pos < content.Length && char.IsWhiteSpace(content[pos]))
        {
            pos++;
        }
    }

    private static void SkipWhitespaceAndCommas(string content, ref int pos)
    {
        while (pos < content.Length && (char.IsWhiteSpace(content[pos]) || content[pos] == ','))
        {
            pos++;
        }
    }

    private static string StripLineComments(string content) =>
        LineCommentPattern().Replace(content, "");

    [GeneratedRegex(@"(?m)^[ \t]*%.*$")]
    private static partial Regex LineCommentPattern();

    // Common LaTeX accent escapes, in both braced ("{\'e}") and bare ("\'e") forms.
    private static readonly (string From, string To)[] LatexEscapes =
    [
        ("{\\'a}", "á"), ("\\'a", "á"), ("{\\'e}", "é"), ("\\'e", "é"),
        ("{\\'i}", "í"), ("\\'i", "í"), ("{\\'o}", "ó"), ("\\'o", "ó"),
        ("{\\'u}", "ú"), ("\\'u", "ú"),
        ("{\\`a}", "à"), ("\\`a", "à"), ("{\\`e}", "è"), ("\\`e", "è"),
        ("{\\`i}", "ì"), ("\\`i", "ì"), ("{\\`o}", "ò"), ("\\`o", "ò"),
        ("{\\`u}", "ù"), ("\\`u", "ù"),
        ("{\\^a}", "â"), ("\\^a", "â"), ("{\\^e}", "ê"), ("\\^e", "ê"),
        ("{\\^i}", "î"), ("\\^i", "î"), ("{\\^o}", "ô"), ("\\^o", "ô"),
        ("{\\^u}", "û"), ("\\^u", "û"),
        ("{\\\"a}", "ä"), ("\\\"a", "ä"), ("{\\\"e}", "ë"), ("\\\"e", "ë"),
        ("{\\\"i}", "ï"), ("\\\"i", "ï"), ("{\\\"o}", "ö"), ("\\\"o", "ö"),
        ("{\\\"u}", "ü"), ("\\\"u", "ü"),
        ("{\\~n}", "ñ"), ("\\~n", "ñ"), ("{\\~a}", "ã"), ("\\~a", "ã"), ("{\\~o}", "õ"), ("\\~o", "õ"),
        ("{\\c{c}}", "ç"), ("\\c{c}", "ç"), ("{\\c c}", "ç"),
        ("{\\ss}", "ß"), ("\\ss", "ß"),
        ("\\&", "&"), ("\\%", "%"), ("\\_", "_"), ("\\#", "#"),
    ];

    /// <summary>
    /// Normalizes the most common LaTeX accent escapes and dash conventions to plain Unicode.
    /// Any remaining braces (typically used only to protect capitalization, e.g. "{ACM}") are
    /// stripped, since they carry no meaning once rendered as plain text.
    /// </summary>
    public static string NormalizeLatexEscapes(string value)
    {
        var result = value;
        foreach (var (from, to) in LatexEscapes)
        {
            result = result.Replace(from, to, StringComparison.Ordinal);
        }

        result = result.Replace("---", "—").Replace("--", "–");
        result = result.Replace("{", "").Replace("}", "");

        return result.Trim();
    }
}
