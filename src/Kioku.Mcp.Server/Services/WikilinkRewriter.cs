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

    /// <summary>Action selected by the caller for one raw wikilink target.</summary>
    public enum TargetRewriteAction
    {
        LeaveUnchanged,
        Rewrite,
        Ambiguous,
    }

    /// <summary>
    /// Resolver-aware decision for one raw target (the part before a display alias).
    /// ReplacementTarget contains the complete replacement target, including any preserved
    /// heading/block fragment. The rewriter itself remains filesystem-agnostic.
    /// </summary>
    public readonly record struct TargetRewriteDecision(
        TargetRewriteAction Action,
        string? ReplacementTarget = null)
    {
        public static TargetRewriteDecision Leave => new(TargetRewriteAction.LeaveUnchanged);
        public static TargetRewriteDecision Ambiguous => new(TargetRewriteAction.Ambiguous);
        public static TargetRewriteDecision ReplaceWith(string target) =>
            new(TargetRewriteAction.Rewrite, target);
    }

    /// <param name="NewContent">Content with matching links rewritten.</param>
    /// <param name="ReplacedCount">Number of links rewritten.</param>
    /// <param name="AmbiguousMatches">
    /// Raw link bodies (the text between [[ and ]]) that matched an ambiguous target and were
    /// left untouched.
    /// </param>
    public sealed record RewriteResult(
        string NewContent,
        int ReplacedCount,
        IReadOnlyList<string> AmbiguousMatches);

    /// <summary>
    /// Historical syntax-only rewrite path kept for isolated string transformation tests.
    /// Production mutation callers should use the resolver-aware overload so a literal '#'
    /// filename cannot be confused with a heading or block fragment.
    /// </summary>
    public static RewriteResult Rewrite(string content, RewritePlan plan, int bodyStart = 0) =>
        Rewrite(content, plan, target => DecideLegacyTarget(target, plan), bodyStart);

    /// <summary>
    /// Rewrites wikilinks using a caller-provided decision for every complete raw target.
    /// The callback receives the target before a display alias but with every '#' preserved,
    /// so canonical resolution can distinguish literal hash filenames from real fragments.
    /// </summary>
    public static RewriteResult Rewrite(
        string content,
        RewritePlan plan,
        Func<string, TargetRewriteDecision> decideTarget,
        int bodyStart = 0)
    {
        ArgumentNullException.ThrowIfNull(decideTarget);

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

            sb.Append(RewriteLineLinks(line, decideTarget, ambiguous, ref replaced)).Append(terminator);
        }

        return new RewriteResult(sb.ToString(), replaced, ambiguous);
    }

    // Private helpers

    private static string RewriteLineLinks(
        string line,
        Func<string, TargetRewriteDecision> decideTarget,
        List<string> ambiguous,
        ref int replaced)
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
            sb.Append("[[").Append(RewriteInner(inner, decideTarget, ambiguous, ref replaced)).Append("]]");

            pos = close + 2;
        }

        return sb.ToString();
    }

    private static string RewriteInner(
        string inner,
        Func<string, TargetRewriteDecision> decideTarget,
        List<string> ambiguous,
        ref int replaced)
    {
        var pipeIdx = inner.IndexOf('|');
        var rawTarget = pipeIdx >= 0 ? inner[..pipeIdx] : inner;
        var alias = pipeIdx >= 0 ? inner[pipeIdx..] : string.Empty;

        if (string.IsNullOrWhiteSpace(rawTarget))
        {
            return inner;
        }

        var decision = decideTarget(rawTarget);
        switch (decision.Action)
        {
            case TargetRewriteAction.LeaveUnchanged:
                return inner;

            case TargetRewriteAction.Ambiguous:
                ambiguous.Add(inner);
                return inner;

            case TargetRewriteAction.Rewrite:
                if (string.IsNullOrWhiteSpace(decision.ReplacementTarget))
                {
                    return inner;
                }

                replaced++;
                return decision.ReplacementTarget + alias;

            default:
                return inner;
        }
    }

    private static TargetRewriteDecision DecideLegacyTarget(string rawTarget, RewritePlan plan)
    {
        var beforePipe = rawTarget;
        var hashIdx = beforePipe.IndexOf('#');
        var target = hashIdx >= 0 ? beforePipe[..hashIdx] : beforePipe;
        var fragment = hashIdx >= 0 ? beforePipe[hashIdx..] : string.Empty;

        var trimmedTarget = target.Trim();
        if (trimmedTarget.Length == 0)
        {
            return TargetRewriteDecision.Leave;
        }

        var normalizedTarget = trimmedTarget.Replace('\\', '/');
        if (normalizedTarget.Contains('/'))
        {
            return string.Equals(normalizedTarget, plan.OldFullPath, StringComparison.OrdinalIgnoreCase)
                ? TargetRewriteDecision.ReplaceWith(plan.NewFullPath + fragment)
                : TargetRewriteDecision.Leave;
        }

        if (!string.Equals(normalizedTarget, plan.OldShortName, StringComparison.OrdinalIgnoreCase))
        {
            return TargetRewriteDecision.Leave;
        }

        if (plan.ShortNameAmbiguous)
        {
            return TargetRewriteDecision.Ambiguous;
        }

        return plan.RewriteShortNameLinks
            ? TargetRewriteDecision.ReplaceWith(plan.NewShortName + fragment)
            : TargetRewriteDecision.Leave;
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
