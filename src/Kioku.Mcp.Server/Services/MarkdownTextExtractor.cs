using System.Text.RegularExpressions;
using Markdig;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Extracts clean text from Obsidian Markdown notes.
/// </summary>
public static partial class MarkdownTextExtractor
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

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

        var body = rawContent[bodyStart..];
        body = ObsidianFencedCodePattern().Replace(body, string.Empty);
        body = ObsidianCommentPattern().Replace(body, string.Empty);
        body = ObsidianCalloutMarkerPattern().Replace(body, string.Empty);
        body = ObsidianBlockIdPattern().Replace(body, string.Empty);
        var plainText = Markdown.ToPlainText(body, Pipeline, null);
        return ObsidianWikilinkPattern().Replace(plainText, match =>
            match.Groups["alias"].Success
                ? match.Groups["alias"].Value
                : match.Groups["target"].Value).Trim();
    }

    /// <summary>
    /// Extracts all [[target]] wikilinks from the content of a note.
    /// Skips fenced code blocks (``` or ~~~) and inline code spans (`...`) entirely, since
    /// wikilinks written there are example syntax, not real outgoing links.
    /// </summary>
    public static IReadOnlyList<string> ExtractWikilinks(string content)
    {
        var links = new List<string>();
        var span = content.AsSpan();
        int pos = 0;
        bool inFence = false;

        while (pos < span.Length)
        {
            int lineEnd = span[pos..].IndexOfAny('\n', '\r');
            var line = lineEnd < 0 ? span[pos..] : span.Slice(pos, lineEnd);

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```".AsSpan()) || trimmed.StartsWith("~~~".AsSpan()))
            {
                inFence = !inFence;
            }
            else if (!inFence)
            {
                ExtractWikilinksFromLine(line, links);
            }

            pos += lineEnd < 0 ? span.Length - pos : lineEnd + 1;
            if (pos < span.Length && span[pos - 1] == '\r' && span[pos] == '\n')
            {
                pos++;
            }
        }

        return links;
    }

    private static void ExtractWikilinksFromLine(ReadOnlySpan<char> line, List<string> links)
    {
        int pos = 0;
        while (pos < line.Length)
        {
            // Skip inline code spans entirely: wikilinks there are example syntax.
            if (line[pos] == '`')
            {
                int closeTick = line[(pos + 1)..].IndexOf('`');
                pos += closeTick >= 0 ? closeTick + 2 : 1;
                continue;
            }

            if (line[pos] == '[' && pos + 1 < line.Length && line[pos + 1] == '[')
            {
                int absOpen = pos + 2;
                int close = line[absOpen..].IndexOf("]]".AsSpan(), StringComparison.Ordinal);
                if (close < 0)
                {
                    break;
                }

                var link = line.Slice(absOpen, close);
                int pipeIdx = link.IndexOf('|');
                var target = pipeIdx >= 0 ? link[..pipeIdx] : link;

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
                continue;
            }

            pos++;
        }
    }

    [GeneratedRegex(@"(?<!\w)!?\[\[(?<target>[^\]|]+)(?:\|(?<alias>[^\]]+))?\]\]")]
    private static partial Regex ObsidianWikilinkPattern();

    [GeneratedRegex(@"(?ms)^[ \t]{0,3}(?<fence>`{3,}|~{3,})[^\r\n]*(?:\r\n|\r|\n).*?^[ \t]{0,3}\k<fence>[ \t]*$")]
    private static partial Regex ObsidianFencedCodePattern();

    [GeneratedRegex(@"(?s)%%.*?%%")]
    private static partial Regex ObsidianCommentPattern();

    [GeneratedRegex(@"(?im)^[ \t]{0,3}>[ \t]*\[![A-Za-z0-9_-]+\][ \t]*")]
    private static partial Regex ObsidianCalloutMarkerPattern();

    [GeneratedRegex(@"(?m)(?:^|[ \t])\^[A-Za-z0-9_-]+[ \t]*(?=\r?$)")]
    private static partial Regex ObsidianBlockIdPattern();
}
