using System.Text;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Rewrites inbound wikilinks after a note is moved or renamed.
/// Locates [[target]], [[target|alias]], [[target#heading]], [[target#^block]] and
/// ![[target]] (embed) occurrences and replaces only the target portion, leaving
/// aliases/fragments and everything else in the note byte-for-byte unchanged.
/// Skips frontmatter (via bodyStart) and fenced code blocks (``` or ~~~).
/// </summary>
public static class WikilinkRewriter
{
    /// <param name="OldShortName">Note name (no extension) before the operation.</param>
    /// <param name="NewShortName">Note name (no extension) after the operation.</param>
    /// <param name="OldFullPath">Vault-relative path (no extension, forward slashes) before the operation.</param>
    /// <param name="NewFullPath">Vault-relative path (no extension, forward slashes) after the operation.</param>
    /// <param name="RewriteShortNameLinks">
    /// Whether bare-name links (e.g. [[Note]]) should be rewritten. False for move_note,
    /// since moving a note doesn't change its short name and Obsidian resolves bare-name
    /// links across folders.
    /// </param>
    /// <param name="ShortNameAmbiguous">
    /// True when another note shares OldShortName. Bare-name links are then left untouched
    /// (they can't be safely disambiguated) and reported instead of rewritten.
    /// </param>
    public sealed record RewritePlan(
        string OldShortName,
        string NewShortName,
        string OldFullPath,
        string NewFullPath,
        bool RewriteShortNameLinks,
        bool ShortNameAmbiguous);

    /// <param name="NewContent">Content with matching links rewritten.</param>
    /// <param name="ReplacedCount">Number of links rewritten.</param>
    /// <param name="AmbiguousMatches">
    /// Raw link bodies (the text between [[ and ]]) that matched OldShortName but were left
    /// untouched because ShortNameAmbiguous was true.
    /// </param>
    public sealed record RewriteResult(
        string NewContent,
        int ReplacedCount,
        IReadOnlyList<string> AmbiguousMatches);

    public static RewriteResult Rewrite(string content, RewritePlan plan, int bodyStart = 0)
    {
        var frontmatter = content[..bodyStart];
        var body = content[bodyStart..];

        var sb = new StringBuilder(content.Length);
        sb.Append(frontmatter);

        var ambiguous = new List<string>();
        var replaced = 0;
        var inCodeBlock = false;

        foreach (var (line, terminator) in SplitLinesPreserving(body))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                sb.Append(line).Append(terminator);
                continue;
            }

            if (inCodeBlock || !line.Contains("[[", StringComparison.Ordinal))
            {
                sb.Append(line).Append(terminator);
                continue;
            }

            sb.Append(RewriteLineLinks(line, plan, ambiguous, ref replaced)).Append(terminator);
        }

        return new RewriteResult(sb.ToString(), replaced, ambiguous);
    }

    // Private helpers

    private static string RewriteLineLinks(string line, RewritePlan plan, List<string> ambiguous, ref int replaced)
    {
        var sb = new StringBuilder(line.Length);
        var pos = 0;

        while (pos < line.Length)
        {
            var open = line.IndexOf("[[", pos, StringComparison.Ordinal);
            if (open < 0)
            {
                sb.Append(line, pos, line.Length - pos);
                break;
            }

            var close = line.IndexOf("]]", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                sb.Append(line, pos, line.Length - pos);
                break;
            }

            sb.Append(line, pos, open - pos);
            var inner = line.Substring(open + 2, close - open - 2);
            sb.Append("[[").Append(RewriteInner(inner, plan, ambiguous, ref replaced)).Append("]]");

            pos = close + 2;
        }

        return sb.ToString();
    }

    private static string RewriteInner(string inner, RewritePlan plan, List<string> ambiguous, ref int replaced)
    {
        var pipeIdx = inner.IndexOf('|');
        var beforePipe = pipeIdx >= 0 ? inner[..pipeIdx] : inner;
        var alias = pipeIdx >= 0 ? inner[pipeIdx..] : string.Empty;

        var hashIdx = beforePipe.IndexOf('#');
        var target = hashIdx >= 0 ? beforePipe[..hashIdx] : beforePipe;
        var fragment = hashIdx >= 0 ? beforePipe[hashIdx..] : string.Empty;

        var trimmedTarget = target.Trim();
        if (trimmedTarget.Length == 0)
        {
            return inner;
        }

        var normalizedTarget = trimmedTarget.Replace('\\', '/');

        if (normalizedTarget.Contains('/'))
        {
            if (!string.Equals(normalizedTarget, plan.OldFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return inner;
            }

            replaced++;
            return plan.NewFullPath + fragment + alias;
        }

        if (!string.Equals(normalizedTarget, plan.OldShortName, StringComparison.OrdinalIgnoreCase))
        {
            return inner;
        }

        if (plan.ShortNameAmbiguous)
        {
            ambiguous.Add(inner);
            return inner;
        }

        if (!plan.RewriteShortNameLinks)
        {
            return inner;
        }

        replaced++;
        return plan.NewShortName + fragment + alias;
    }

    private static IEnumerable<(string Line, string Terminator)> SplitLinesPreserving(string text)
    {
        var pos = 0;
        while (pos < text.Length)
        {
            var newlineIdx = text.IndexOfAny(['\n', '\r'], pos);
            if (newlineIdx < 0)
            {
                yield return (text[pos..], string.Empty);
                yield break;
            }

            var line = text[pos..newlineIdx];
            string terminator;
            if (text[newlineIdx] == '\r' && newlineIdx + 1 < text.Length && text[newlineIdx + 1] == '\n')
            {
                terminator = "\r\n";
                pos = newlineIdx + 2;
            }
            else
            {
                terminator = text[newlineIdx].ToString();
                pos = newlineIdx + 1;
            }

            yield return (line, terminator);
        }
    }
}
