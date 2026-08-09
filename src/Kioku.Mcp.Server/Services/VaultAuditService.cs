using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Domain;
using ModelContextProtocol.Protocol;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Builds the read-only vault health report exposed by <c>audit_vault</c>.
/// Template-placeholder classification is audit-only and never changes indexing or resolution.
/// </summary>
internal static class VaultAuditService
{
    private sealed record LinkIssue(
        string Note,
        string Link,
        string Identity,
        VaultLinkResolutionStatus Status,
        bool IsTemplatePlaceholder = false,
        string? Reason = null);

    private sealed record LinkAuditPage(
        int TotalOccurrences,
        int UniqueEdges,
        int UniqueTargets,
        int Returned,
        int Offset,
        int Limit,
        bool HasMore,
        IReadOnlyList<LinkIssue> Findings);

    public static async Task<CallToolResult> CreateAsync(
        VaultIndexService vault,
        KiokuConfiguration config,
        VaultConfigService vaultConfig,
        int staleDays,
        int offset,
        int limit)
    {
        if (offset < 0)
        {
            return CreateAuditError("'offset' must be 0 or greater.");
        }

        if (limit <= 0)
        {
            return CreateAuditError("'limit' must be greater than 0.");
        }

        var maxAuditResults = Math.Max(50, config.MaxSearchResults);
        var cappedLimit = Math.Min(limit, maxAuditResults);
        var generatedAt = DateTime.UtcNow;
        var notes = vault.GetAllNotes().ToList();
        var cutoff = generatedAt.AddDays(-staleDays);

        var noTags = notes.Where(note => note.Metadata.Tags.Count == 0).ToList();
        var noDates = notes.Where(note => !note.Metadata.Date.HasValue).ToList();
        var emptyNotes = notes.Where(note => string.IsNullOrWhiteSpace(note.PlainText)).ToList();
        var stale = notes.Where(note => note.LastModified < cutoff).ToList();

        var templateSources = await AuditTemplateSourcePolicy.CreateAsync(config, vaultConfig);
        var linkIssues = ScanLinkIssues(vault, notes, templateSources);
        var broken = CreateLinkAuditPage(
            linkIssues.Where(issue => !issue.IsTemplatePlaceholder),
            VaultLinkResolutionStatus.Missing,
            offset,
            cappedLimit);
        var ambiguous = CreateLinkAuditPage(
            linkIssues.Where(issue => !issue.IsTemplatePlaceholder),
            VaultLinkResolutionStatus.Ambiguous,
            offset,
            cappedLimit);
        var malformed = CreateLinkAuditPage(
            linkIssues.Where(issue => !issue.IsTemplatePlaceholder),
            VaultLinkResolutionStatus.Malformed,
            offset,
            cappedLimit);
        var templatePlaceholders = CreateLinkAuditPage(
            linkIssues.Where(issue => issue.IsTemplatePlaceholder),
            VaultLinkResolutionStatus.Malformed,
            offset,
            cappedLimit);

        var text = BuildTextReport(
            generatedAt,
            staleDays,
            notes,
            noTags,
            noDates,
            emptyNotes,
            stale,
            broken,
            ambiguous,
            malformed,
            templatePlaceholders);

        var envelope = new
        {
            success = true,
            data = new
            {
                generated_at_utc = generatedAt.ToString("O", CultureInfo.InvariantCulture),
                total_notes = notes.Count,
                stale_days = staleDays,
                counts = new
                {
                    notes_without_tags = noTags.Count,
                    notes_without_date = noDates.Count,
                    empty_notes = emptyNotes.Count,
                    stale_notes = stale.Count,
                    broken_occurrences = broken.TotalOccurrences,
                    unique_broken_edges = broken.UniqueEdges,
                    unique_broken_targets = broken.UniqueTargets,
                    ambiguous_occurrences = ambiguous.TotalOccurrences,
                    unique_ambiguous_edges = ambiguous.UniqueEdges,
                    unique_ambiguous_targets = ambiguous.UniqueTargets,
                    malformed_occurrences = malformed.TotalOccurrences,
                    unique_malformed_edges = malformed.UniqueEdges,
                    unique_malformed_targets = malformed.UniqueTargets,
                    template_placeholder_occurrences = templatePlaceholders.TotalOccurrences,
                    unique_template_placeholder_edges = templatePlaceholders.UniqueEdges,
                    unique_template_placeholder_targets = templatePlaceholders.UniqueTargets,
                },
                links = new
                {
                    broken = ToStructuredLinkPage(broken),
                    ambiguous = ToStructuredLinkPage(ambiguous),
                    malformed = ToStructuredLinkPage(malformed),
                    template_placeholders = ToStructuredLinkPage(
                        templatePlaceholders,
                        statusOverride: "template_placeholder"),
                },
            },
            error = (object?)null,
            pagination = (object?)null,
            warnings = Array.Empty<string>(),
        };

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(envelope),
            IsError = false,
        };
    }

    private static List<LinkIssue> ScanLinkIssues(
        VaultIndexService vault,
        IReadOnlyCollection<Note> notes,
        AuditTemplateSourcePolicy templateSources)
    {
        var issues = new List<LinkIssue>();
        foreach (var note in notes)
        {
            foreach (var reference in MarkdownTextExtractor.ExtractWikilinkReferences(note.RawContent))
            {
                if (reference.IsMalformed)
                {
                    var templatePlaceholder = IsRecognizedTemplatePlaceholder(
                        note,
                        reference,
                        templateSources);
                    issues.Add(new LinkIssue(
                        note.VaultRelativePath,
                        reference.Raw,
                        NormalizeLinkIdentity(reference.Raw),
                        VaultLinkResolutionStatus.Malformed,
                        templatePlaceholder,
                        templatePlaceholder ? "empty_target_in_template" : null));
                    continue;
                }

                var resolution = vault.ResolveLinkResult(note, reference.Target);
                if (resolution.Status == VaultLinkResolutionStatus.Resolved)
                {
                    continue;
                }

                issues.Add(new LinkIssue(
                    note.VaultRelativePath,
                    reference.Target,
                    NormalizeLinkIdentity(resolution.Target),
                    resolution.Status));
            }
        }

        return issues;
    }

    private static bool IsRecognizedTemplatePlaceholder(
        Note note,
        MarkdownTextExtractor.WikilinkReference reference,
        AuditTemplateSourcePolicy templateSources)
    {
        // `[[]]` and `![[]]` both produce a closed reference with empty Target and Raw values.
        // An unclosed link instead retains its raw syntax, so it must remain malformed.
        return reference.IsMalformed &&
               string.IsNullOrWhiteSpace(reference.Target) &&
               string.IsNullOrWhiteSpace(reference.Raw) &&
               templateSources.IsTemplate(note);
    }

    private static LinkAuditPage CreateLinkAuditPage(
        IEnumerable<LinkIssue> allIssues,
        VaultLinkResolutionStatus status,
        int offset,
        int limit)
    {
        var issues = allIssues
            .Where(issue => issue.Status == status)
            .OrderBy(issue => issue.Note, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.Identity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(issue => issue.Link, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var uniqueEdges = issues
            .Select(issue => $"{issue.Note}\0{issue.Identity}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var uniqueTargets = issues
            .Select(issue => issue.Identity)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var findings = issues.Skip(offset).Take(limit).ToList();

        return new LinkAuditPage(
            issues.Count,
            uniqueEdges,
            uniqueTargets,
            findings.Count,
            offset,
            limit,
            offset + findings.Count < issues.Count,
            findings);
    }

    private static string NormalizeLinkIdentity(string target)
    {
        var normalized = target.Replace('\\', '/').Trim();
        return normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^3]
            : normalized;
    }

    private static object ToStructuredLinkPage(LinkAuditPage page, string? statusOverride = null) => new
    {
        total_occurrences = page.TotalOccurrences,
        unique_edges = page.UniqueEdges,
        unique_targets = page.UniqueTargets,
        returned = page.Returned,
        offset = page.Offset,
        limit = page.Limit,
        has_more = page.HasMore,
        findings = page.Findings.Select(issue => new
        {
            source = issue.Note,
            target = issue.Link,
            target_identity = issue.Identity,
            status = statusOverride ?? issue.Status.ToString().ToLowerInvariant(),
            reason = issue.Reason,
        }),
    };

    private static string BuildTextReport(
        DateTime generatedAt,
        int staleDays,
        List<Note> notes,
        List<Note> noTags,
        List<Note> noDates,
        List<Note> emptyNotes,
        List<Note> stale,
        LinkAuditPage broken,
        LinkAuditPage ambiguous,
        LinkAuditPage malformed,
        LinkAuditPage templatePlaceholders)
    {
        var sb = new StringBuilder("# Kioku — Vault Audit Report\n\n");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Generated:** {generatedAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Total notes:** {notes.Count}\n");

        AppendSection(sb, $"Notes without tags ({noTags.Count})", noTags.Select(note => note.VaultRelativePath));
        AppendSection(sb, $"Notes without date in frontmatter ({noDates.Count})", noDates.Select(note => note.VaultRelativePath));
        AppendSection(sb, $"Empty notes ({emptyNotes.Count})", emptyNotes.Select(note => note.VaultRelativePath));
        AppendLinkSection(sb, "Broken wikilinks", broken, issue => $"{issue.Note}: [[{issue.Link}]]");
        AppendLinkSection(sb, "Ambiguous wikilinks", ambiguous, issue => $"{issue.Note}: [[{issue.Link}]]");
        AppendLinkSection(sb, "Malformed wikilinks", malformed, issue => $"{issue.Note}: {issue.Link}");
        AppendLinkSection(
            sb,
            "Template placeholders",
            templatePlaceholders,
            issue => $"{issue.Note}: empty wikilink/embed placeholder");
        AppendSection(
            sb,
            $"Stale notes (not updated in {staleDays}+ days) ({stale.Count})",
            stale.OrderBy(note => note.LastModified)
                .Select(note => $"{note.VaultRelativePath} (last modified: {note.LastModified:yyyy-MM-dd})"));

        sb.AppendLine("\n---");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"**Summary:** {noTags.Count} untagged · {emptyNotes.Count} empty · " +
            $"{broken.TotalOccurrences} broken occurrences ({broken.UniqueEdges} unique edges, {broken.UniqueTargets} unique targets) · " +
            $"{ambiguous.TotalOccurrences} ambiguous occurrences ({ambiguous.UniqueEdges} unique edges, {ambiguous.UniqueTargets} unique targets) · " +
            $"{malformed.TotalOccurrences} malformed occurrences ({malformed.UniqueEdges} unique edges, {malformed.UniqueTargets} unique targets) · " +
            $"{templatePlaceholders.TotalOccurrences} template placeholders skipped from malformed · " +
            $"{stale.Count} stale");

        return sb.ToString();
    }

    private static void AppendLinkSection(
        StringBuilder sb,
        string title,
        LinkAuditPage page,
        Func<LinkIssue, string> format)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"## {title} ({page.TotalOccurrences})");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"_unique edges: {page.UniqueEdges} · unique targets: {page.UniqueTargets} · " +
            $"returned: {page.Returned} · offset: {page.Offset} · limit: {page.Limit} · " +
            $"has_more: {page.HasMore.ToString().ToLowerInvariant()}_");

        if (page.TotalOccurrences == 0)
        {
            sb.AppendLine("_(none)_");
        }
        else if (page.Findings.Count == 0)
        {
            sb.AppendLine("_(requested page is empty)_");
        }
        else
        {
            foreach (var issue in page.Findings)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {format(issue)}");
            }

            if (page.HasMore)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"- _(... and {page.TotalOccurrences - page.Offset - page.Returned} more)_");
            }
        }

        sb.AppendLine();
    }

    private static void AppendSection(StringBuilder sb, string title, IEnumerable<string> items)
    {
        var list = items.ToList();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## {title}");
        if (list.Count == 0)
        {
            sb.AppendLine("_(none)_");
        }
        else
        {
            foreach (var item in list.Take(50))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {item}");
            }

            if (list.Count > 50)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- _(... and {list.Count - 50} more)_");
            }
        }

        sb.AppendLine();
    }

    private static CallToolResult CreateAuditError(string message)
    {
        var text = KiokuError.InvalidArgument(message);
        var envelope = new
        {
            success = false,
            data = (object?)null,
            error = new { code = "INVALID_ARGUMENT", message },
            pagination = (object?)null,
            warnings = Array.Empty<string>(),
        };

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(envelope),
            IsError = true,
        };
    }
}
