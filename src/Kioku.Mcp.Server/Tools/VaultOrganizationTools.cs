using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for vault organization and taxonomy management.
/// Bulk operations include a dry_run parameter that previews changes without modifying the vault.
/// </summary>
[McpServerToolType]
public sealed class VaultOrganizationTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    HybridSearchService hybrid,
    EmbeddingService embedding,
    VaultConfigService vaultConfig)
{
    // manage_tags

    [McpServerTool, Description(
        "Manages tags across the entire vault. operation must be 'normalize', 'rename', or 'merge'. " +
        "Rename uses old_tag/new_tag; merge uses source_tag/target_tag. " +
        "Use dry_run=true to preview changes without modifying files.")]
    public async Task<string> manage_tags(
        [Description("Operation to perform: normalize, rename, or merge.")] string operation,
        [Description("Tag to rename from when operation is 'rename'.")] string old_tag = "",
        [Description("Tag to rename to when operation is 'rename'.")] string new_tag = "",
        [Description("Tag to merge away when operation is 'merge'.")] string source_tag = "",
        [Description("Tag to keep when operation is 'merge'.")] string target_tag = "",
        [Description("If true, returns a preview without modifying any files.")] bool dry_run = false)
    {
        var normalizedOperation = operation?.Trim().ToLowerInvariant();
        if (normalizedOperation is not ("normalize" or "rename" or "merge"))
        {
            return $"[error] Unknown tag operation '{operation}'. Use 'normalize', 'rename', or 'merge'.";
        }

        if (normalizedOperation == "rename" &&
            (string.IsNullOrWhiteSpace(old_tag) || string.IsNullOrWhiteSpace(new_tag)))
        {
            return "[error] Both old_tag and new_tag are required for rename.";
        }

        if (normalizedOperation == "merge" &&
            (string.IsNullOrWhiteSpace(source_tag) || string.IsNullOrWhiteSpace(target_tag)))
        {
            return "[error] Both source_tag and target_tag are required for merge.";
        }

        if (normalizedOperation == "merge" && source_tag.Equals(target_tag, StringComparison.OrdinalIgnoreCase))
        {
            return "[error] source_tag and target_tag cannot be the same.";
        }

        var notes = vault.GetAllNotes().ToList();
        var affected = notes
            .Where(note => note.Metadata.Tags.Any(tag => TagWouldChange(
                tag, note.Metadata.Tags, normalizedOperation, old_tag, new_tag, source_tag, target_tag)))
            .ToList();

        if (affected.Count == 0)
        {
            return normalizedOperation switch
            {
                "normalize" => "[ok] All tags are already normalized. No changes needed.",
                "rename" => $"[info] Tag '#{old_tag}' not found in any note.",
                _ => $"[info] Tag '#{source_tag}' not found in any note. Nothing to merge.",
            };
        }

        if (dry_run)
        {
            var description = normalizedOperation switch
            {
                "normalize" => $"{CountTagChanges(affected, normalizedOperation, old_tag, new_tag, source_tag, target_tag)} tag(s) would be normalized",
                "rename" => $"#{old_tag} → #{new_tag} would affect {affected.Count} note(s)",
                _ => $"#{source_tag} → #{target_tag} would affect {affected.Count} note(s)",
            };
            var sb = new StringBuilder($"[info] dry_run=true — {description}:\n\n");
            foreach (var note in affected)
            {
                sb.AppendLine($"  {note.VaultRelativePath}");
            }
            return sb.ToString();
        }

        var (changedTags, updatedNotes) = await RewriteTagsAsync(
            affected, normalizedOperation, old_tag, new_tag, source_tag, target_tag);

        return normalizedOperation switch
        {
            "normalize" => $"[ok] Normalized {changedTags} tag(s) across {updatedNotes} note(s).",
            "rename" => $"[ok] Renamed #{old_tag} → #{new_tag} in {updatedNotes} note(s).",
            _ => $"[ok] Merged #{source_tag} → #{target_tag} in {updatedNotes} note(s).",
        };
    }

    // suggest_tags

    [McpServerTool, Description(
        "Reports a note's existing, folder-inherited, and excluded tag state, then suggests " +
        "relevant existing vault tags using keyword overlap.")]
    public Task<string> suggest_tags(
        [Description("Name or path of the note to inspect and suggest tags for.")] string note,
        [Description("Maximum number of tag suggestions to return.")] int max_suggestions = 10)
    {
        var found = NoteHelpers.ResolveNote(note, vault);
        if (found is null)
        {
            return Task.FromResult($"[error] Note not found: '{note}'");
        }

        var folder = Path.GetDirectoryName(found.VaultRelativePath)?.Replace('\\', '/') ?? "";
        var inherited = vaultConfig.GetInheritedTags(folder);
        var excluded = vaultConfig.ExcludeFromTags;
        var scored = ScoreTagSuggestions(found, Math.Max(0, max_suggestions), inherited, excluded);

        var sb = new StringBuilder($"[ok] Tag state for '{found.Name}':\n\n");
        sb.AppendLine($"Existing tags: {FormatTags(found.Metadata.Tags)}");
        sb.AppendLine($"Inherited tags: {FormatTags(inherited)}");
        sb.AppendLine($"Excluded from tags (frontmatter fields): {FormatTags(excluded, prefix: "")}");
        sb.AppendLine();
        sb.AppendLine($"Suggested tags ({scored.Count}):");
        if (scored.Count == 0)
        {
            sb.Append("  (none)");
        }
        else
        {
            foreach (var tag in scored)
            {
                sb.AppendLine($"  #{tag}");
            }
        }

        return Task.FromResult(sb.ToString());
    }

    /// <summary>Scores existing vault tags by keyword overlap with a note's content and title.</summary>
    private List<string> ScoreTagSuggestions(
        Note found,
        int maxSuggestions,
        IEnumerable<string>? inheritedTags = null,
        IEnumerable<string>? excludedTags = null)
    {
        // Get all unique tags across the vault
        var allTags = vault.GetAllNotes()
            .SelectMany(n => n.Metadata.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var blockedTags = found.Metadata.Tags
            .Concat(inheritedTags ?? [])
            .Concat(excludedTags ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Score tags by word overlap with note content + title
        var noteWords = TokenizeText(found.PlainText + " " + found.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return allTags
            .Where(kv => !blockedTags.Contains(kv.Key))
            .Select(kv =>
            {
                var tagWords = TokenizeText(kv.Key.Replace('-', ' ').Replace('_', ' '));
                var overlap = tagWords.Count(w => noteWords.Contains(w));
                return (Tag: kv.Key, Score: overlap * 2 + (kv.Value / 10.0));
            })
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .Take(maxSuggestions)
            .Select(s => s.Tag)
            .ToList();
    }

    // find_duplicate_notes

    [McpServerTool, Description(
        "Detects notes with very similar titles or content that may be duplicates. " +
        "Always operates as a dry run — reports findings without modifying the vault.")]
    public Task<string> find_duplicate_notes(
        [Description("Similarity threshold (0.0–1.0). Higher = only very similar notes. Default: 0.8.")] float threshold = 0.8f,
        [Description("Maximum number of duplicate pairs to report.")] int max_results = 20)
    {
        threshold = Math.Clamp(threshold, 0.0f, 1.0f);
        var notes = vault.GetAllNotes().ToList();

        var duplicates = new List<(Note A, Note B, float Similarity, string Reason)>();

        for (int i = 0; i < notes.Count && duplicates.Count < max_results; i++)
        {
            for (int j = i + 1; j < notes.Count && duplicates.Count < max_results; j++)
            {
                var a = notes[i];
                var b = notes[j];

                // Check title similarity (Jaro-Winkler approximation via word overlap)
                var titleSim = TitleSimilarity(a.Name, b.Name);
                if (titleSim >= threshold)
                {
                    duplicates.Add((a, b, titleSim, "similar title"));
                    continue;
                }

                // Check content similarity (word overlap / Jaccard)
                var contentSim = ContentJaccard(a.PlainText, b.PlainText);
                if (contentSim >= threshold)
                {
                    duplicates.Add((a, b, contentSim, "similar content"));
                }
            }
        }

        if (duplicates.Count == 0)
        {
            return Task.FromResult($"[ok] No duplicate notes found (threshold: {threshold:P0}).\\n" +
                                   $"Analyzed {notes.Count} notes.");
        }

        var sb = new StringBuilder($"[ok] Found {duplicates.Count} potential duplicate pair(s) (threshold: {threshold:P0}):\n\n");
        foreach (var (a, b, sim, reason) in duplicates)
        {
            sb.AppendLine($"  [{sim:P0} — {reason}]");
            sb.AppendLine($"    A: {a.VaultRelativePath}");
            sb.AppendLine($"    B: {b.VaultRelativePath}");
            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    // audit_vault

    [McpServerTool, Description(
        "Generates a health report of the vault: notes without tags, without dates, " +
        "without content, broken wikilinks, and notes not updated in a long time.")]
    public Task<string> audit_vault(
        [Description("Flag notes not updated in this many days (default: 90).")] int stale_days = 90)
    {
        var notes = vault.GetAllNotes().ToList();
        var cutoff = DateTime.UtcNow.AddDays(-stale_days);

        var noTags = notes.Where(n => n.Metadata.Tags.Count == 0).ToList();
        var noDates = notes.Where(n => !n.Metadata.Date.HasValue).ToList();
        var emptyNotes = notes.Where(n => string.IsNullOrWhiteSpace(n.PlainText)).ToList();
        var stale = notes.Where(n => n.LastModified < cutoff).ToList();

        var brokenLinks = ScanBrokenLinks(notes);

        var sb = new StringBuilder("# Kioku — Vault Audit Report\n\n");
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"**Total notes:** {notes.Count}\n");

        AppendSection(sb, $"Notes without tags ({noTags.Count})", noTags.Select(n => n.VaultRelativePath));
        AppendSection(sb, $"Notes without date in frontmatter ({noDates.Count})", noDates.Select(n => n.VaultRelativePath));
        AppendSection(sb, $"Empty notes ({emptyNotes.Count})", emptyNotes.Select(n => n.VaultRelativePath));
        AppendSection(sb, $"Broken wikilinks ({brokenLinks.Count})",
            brokenLinks.Select(x => $"{x.Note}: [[{x.Link}]]"));
        AppendSection(sb, $"Stale notes (not updated in {stale_days}+ days) ({stale.Count})",
            stale.OrderBy(n => n.LastModified)
                 .Select(n => $"{n.VaultRelativePath} (last modified: {n.LastModified:yyyy-MM-dd})"));

        sb.AppendLine("\n---");
        sb.AppendLine($"**Summary:** {noTags.Count} untagged · {emptyNotes.Count} empty · " +
                      $"{brokenLinks.Count} broken links · {stale.Count} stale");

        return Task.FromResult(sb.ToString());
    }

    // process_inbox

    [McpServerTool, Description(
        "Batch-triages notes in an inbox folder: for each note, suggests a destination folder " +
        "(same scoring as suggest_folder), tags (keyword overlap + destination folder " +
        "inheritance), and up to 3 related notes (semantic similarity, when Ollama embeddings " +
        "are available). apply=false (default) returns a numbered plan without touching any " +
        "file. apply=true executes it: moves each note (updating inbound full-path wikilinks), " +
        "adds the suggested tags, and appends a Related section. Review the plan before " +
        "applying; git can undo an apply.")]
    public async Task<string> process_inbox(
        [Description("Inbox folder (relative to vault root). Leave empty to use folders.inbox from .kioku/config.yml, falling back to 'Inbox'.")] string inbox_folder = "",
        [Description("Maximum number of notes to process in one call (default: 20).")] int max_notes = 20,
        [Description("If true, executes the plan (move + tag + link). Default false only previews it.")] bool apply = false)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var folder = string.IsNullOrWhiteSpace(inbox_folder)
            ? (vaultConfig.GetFolder("inbox") ?? "Inbox")
            : inbox_folder;

        var folderPath = Path.Combine(config.VaultPath, folder);
        if (!Directory.Exists(folderPath))
        {
            var configuredHint = !string.IsNullOrWhiteSpace(inbox_folder) &&
                                 !string.IsNullOrWhiteSpace(vaultConfig.ConfiguredInbox) &&
                                 !folder.Equals(vaultConfig.ConfiguredInbox, StringComparison.OrdinalIgnoreCase)
                ? $" Configured folders.inbox is '{vaultConfig.ConfiguredInbox}'; omit inbox_folder to use it."
                : string.Empty;
            return $"[info] Inbox folder not found: '{folder}'. Nothing to process.{configuredHint}";
        }

        var notes = vault.GetNotesInFolder(folder)
            .OrderBy(n => n.VaultRelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, max_notes))
            .ToList();

        if (notes.Count == 0)
        {
            return $"[info] Inbox '{folder}' is empty. Nothing to process.";
        }

        var plans = notes.Select(BuildInboxPlan).ToList();

        if (!apply)
        {
            return FormatInboxPlan(plans, folder);
        }

        var results = new List<string>(plans.Count);
        foreach (var plan in plans)
        {
            results.Add(await ApplyInboxPlanAsync(plan));
        }

        var sb = new StringBuilder($"[ok] Processed {plans.Count} note(s) from '{folder}':\n\n");
        foreach (var line in results)
        {
            sb.AppendLine(line);
        }

        sb.AppendLine();
        sb.Append("Made a mistake? Use native git commands or manual file recovery to undo this apply.");

        if (!embedding.IsAvailable)
        {
            sb.AppendLine();
            sb.Append("[info] Semantic embeddings are unavailable — link suggestions were skipped.");
        }

        return sb.ToString();
    }

    // Private helpers — process_inbox

    private sealed record InboxItemPlan(
        Note Note,
        string? DestFolder,
        double? DestScore,
        IReadOnlyList<string> Tags,
        IReadOnlyList<Note> RelatedNotes);

    private InboxItemPlan BuildInboxPlan(Note note)
    {
        var ranked = FolderRanker.RankFolders(note, 1, vault, hybrid, embedding);
        var destFolder = ranked.Count > 0 ? ranked[0].Folder : null;
        var destScore = ranked.Count > 0 ? ranked[0].Score : (double?)null;

        var similarTags = ScoreTagSuggestions(note, 5);
        var inheritedTags = destFolder is not null ? vaultConfig.GetInheritedTags(destFolder) : [];
        var tags = NoteHelpers.MergeTagsWithInheritance(similarTags, inheritedTags, vaultConfig.ExcludeFromTags);

        var related = embedding.IsAvailable
            ? hybrid.FindSimilar(note, 3, 0.3f).Select(r => r.Note).ToList()
            : [];

        return new InboxItemPlan(note, destFolder, destScore, tags, related);
    }

    private static string FormatInboxPlan(IReadOnlyList<InboxItemPlan> plans, string folder)
    {
        var sb = new StringBuilder($"[info] Inbox plan for '{folder}' ({plans.Count} note(s), apply=false — nothing changed):\n\n");

        var i = 1;
        foreach (var plan in plans)
        {
            var dest = plan.DestFolder is not null
                ? $"{plan.DestFolder} (score: {plan.DestScore:F2})"
                : "keep in place (no better folder found)";
            var tags = plan.Tags.Count > 0 ? string.Join(", ", plan.Tags.Select(t => "#" + t)) : "none";
            var links = plan.RelatedNotes.Count > 0
                ? string.Join(", ", plan.RelatedNotes.Select(n => $"[[{n.Name}]]"))
                : "none";

            sb.AppendLine($"{i}. \"{plan.Note.Name}\" → {dest} · tags: {tags} · links: {links}");
            i++;
        }

        sb.AppendLine();
        sb.Append("Set apply=true to execute this plan. Native git commands or manual file recovery can undo it if needed.");

        return sb.ToString();
    }

    private async Task<string> ApplyInboxPlanAsync(InboxItemPlan plan)
    {
        var note = plan.Note;
        var current = note;
        var actions = new List<string>();

        if (plan.DestFolder is not null)
        {
            var destDir = NoteHelpers.EnsureInsideVault(
                config.VaultPath, Path.Combine(config.VaultPath, plan.DestFolder));
            Directory.CreateDirectory(destDir);
            var destPath = Path.Combine(destDir, Path.GetFileName(note.FilePath));

            if (File.Exists(destPath))
            {
                actions.Add($"skipped move (a note named '{note.Name}' already exists in '{plan.DestFolder}')");
            }
            else
            {
                var oldPath = note.FilePath;
                File.Move(oldPath, destPath);
                await vault.SynchronizeFileMoveAsync(oldPath, destPath);
                var newRelativePath = Path.GetRelativePath(config.VaultPath, destPath);

                var updatedLinks = await UpdateInboundWikilinksForMoveAsync(note, newRelativePath);
                actions.Add($"moved to {plan.DestFolder}" +
                            (updatedLinks > 0 ? $" ({updatedLinks} wikilink(s) updated)" : ""));

                current = vault.GetNote(destPath) ?? note;
            }
        }

        if (plan.Tags.Count > 0)
        {
            var rawContent = await File.ReadAllTextAsync(current.FilePath, Encoding.UTF8);
            var bodyStart = FrontmatterParser.GetBodyStart(rawContent);
            var body = rawContent[bodyStart..];

            var meta = current.Metadata;
            var mergedTags = meta.Tags.Concat(plan.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var frontmatter = NoteHelpers.BuildFrontmatter(
                mergedTags, meta.NoteType, meta.Status, meta.Date, domain: meta.Domain, extraFields: meta.ExtraFields,
                updated: vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(DateTime.Today) : null);

            await File.WriteAllTextAsync(current.FilePath, frontmatter + body, NoteHelpers.Utf8NoBom);
            await vault.SynchronizeFileReindexAsync(current.FilePath);
            current = vault.GetNote(current.FilePath) ?? current;

            actions.Add($"tagged: {string.Join(", ", plan.Tags.Select(t => "#" + t))}");
        }

        if (plan.RelatedNotes.Count > 0)
        {
            var relatedSection = new StringBuilder("\n\n## Related\n\n");
            foreach (var related in plan.RelatedNotes)
            {
                relatedSection.AppendLine($"- [[{related.Name}]]");
            }

            await File.AppendAllTextAsync(current.FilePath, relatedSection.ToString(), NoteHelpers.Utf8NoBom);
            await vault.SynchronizeFileReindexAsync(current.FilePath);

            actions.Add($"linked: {string.Join(", ", plan.RelatedNotes.Select(n => $"[[{n.Name}]]"))}");
        }

        if (actions.Count == 0)
        {
            actions.Add("no changes suggested");
        }

        return $"- {note.Name}: {string.Join("; ", actions)}";
    }

    /// <summary>
    /// Rewrites inbound full-path wikilinks after moving a note — same semantics as
    /// NoteCommandTools.move_note (bare-name links are untouched, since the note's short name
    /// doesn't change on a folder move).
    /// </summary>
    private async Task<int> UpdateInboundWikilinksForMoveAsync(Note found, string newVaultRelativePath)
    {
        var plan = new WikilinkRewriter.RewritePlan(
            OldShortName: found.Name,
            NewShortName: found.Name,
            OldFullPath: StripMdExtension(found.VaultRelativePath.Replace('\\', '/')),
            NewFullPath: StripMdExtension(newVaultRelativePath.Replace('\\', '/')),
            RewriteShortNameLinks: false,
            ShortNameAmbiguous: false);

        var candidates = new Dictionary<string, Note>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in vault.GetBacklinks(plan.OldShortName))
        {
            candidates[candidate.FilePath] = candidate;
        }

        foreach (var candidate in vault.GetBacklinks(plan.OldFullPath))
        {
            candidates[candidate.FilePath] = candidate;
        }

        var updatedLinks = 0;
        foreach (var source in candidates.Values)
        {
            var raw = await File.ReadAllTextAsync(source.FilePath, Encoding.UTF8);
            var bodyStart = FrontmatterParser.GetBodyStart(raw);
            var result = WikilinkRewriter.Rewrite(raw, plan, bodyStart);

            if (result.ReplacedCount == 0)
            {
                continue;
            }

            await File.WriteAllTextAsync(source.FilePath, result.NewContent, NoteHelpers.Utf8NoBom);
            await vault.SynchronizeFileReindexAsync(source.FilePath);
            updatedLinks += result.ReplacedCount;
        }

        return updatedLinks;
    }

    private static string StripMdExtension(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? path[..^3] : path;

    // Private helpers

    private static bool TagWouldChange(
        string tag,
        IReadOnlyList<string> noteTags,
        string operation,
        string oldTag,
        string newTag,
        string sourceTag,
        string targetTag)
    {
        var replacement = GetTagReplacement(tag, noteTags, operation, oldTag, newTag, sourceTag, targetTag);
        return replacement is null || !string.Equals(tag, replacement, StringComparison.Ordinal);
    }

    private static int CountTagChanges(
        IEnumerable<Note> notes,
        string operation,
        string oldTag,
        string newTag,
        string sourceTag,
        string targetTag)
    {
        return notes.Sum(note => note.Metadata.Tags.Count(tag => TagWouldChange(
            tag, note.Metadata.Tags, operation, oldTag, newTag, sourceTag, targetTag)));
    }

    private async Task<(int ChangedTags, int UpdatedNotes)> RewriteTagsAsync(
        IEnumerable<Note> affected,
        string operation,
        string oldTag,
        string newTag,
        string sourceTag,
        string targetTag)
    {
        var changedTags = 0;
        var updatedNotes = 0;

        foreach (var note in affected)
        {
            var transform = new Func<string, string?>(tag => GetTagReplacement(
                tag, note.Metadata.Tags, operation, oldTag, newTag, sourceTag, targetTag));
            var rawContent = await File.ReadAllTextAsync(note.FilePath, Encoding.UTF8);
            var newContent = RewriteTagFrontmatter(rawContent, transform);

            if (string.Equals(rawContent, newContent, StringComparison.Ordinal))
            {
                continue;
            }

            await File.WriteAllTextAsync(note.FilePath, newContent, NoteHelpers.Utf8NoBom);
            await vault.SynchronizeFileReindexAsync(note.FilePath);
            changedTags += note.Metadata.Tags.Count(tag => TagWouldChange(
                tag, note.Metadata.Tags, operation, oldTag, newTag, sourceTag, targetTag));
            updatedNotes++;
        }

        return (changedTags, updatedNotes);
    }

    private static string? GetTagReplacement(
        string tag,
        IReadOnlyList<string> noteTags,
        string operation,
        string oldTag,
        string newTag,
        string sourceTag,
        string targetTag)
    {
        return operation switch
        {
            "normalize" => NormalizeTag(tag),
            "rename" when tag.Equals(oldTag, StringComparison.OrdinalIgnoreCase) => newTag,
            "merge" when tag.Equals(sourceTag, StringComparison.OrdinalIgnoreCase) =>
                noteTags.Any(t => t.Equals(targetTag, StringComparison.OrdinalIgnoreCase)) ? null : targetTag,
            _ => tag,
        };
    }

    /// <summary>
    /// Rewrites only the frontmatter tags/tag field. This deliberately avoids replacing list
    /// items in the Markdown body and supports YAML list, inline-list, and scalar forms.
    /// </summary>
    private static string RewriteTagFrontmatter(string content, Func<string, string?> transform)
    {
        var bodyStart = FrontmatterParser.GetBodyStart(content);
        if (bodyStart == 0)
        {
            return content;
        }

        var frontmatter = content[..bodyStart];
        var newline = frontmatter.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : frontmatter.Contains('\n') ? "\n" : "\r";
        var lines = frontmatter.Split(["\r\n", "\n", "\r"], StringSplitOptions.None).ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            var root = Regex.Match(lines[i], @"^(\s*)(tags?|tag)(\s*:\s*)(.*)$", RegexOptions.IgnoreCase);
            if (!root.Success)
            {
                continue;
            }

            var value = root.Groups[4].Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                var item = i + 1;
                while (item < lines.Count && Regex.IsMatch(lines[item], @"^\s+-\s+"))
                {
                    var listItem = Regex.Match(lines[item], @"^(\s*-\s+)(.*?)(\s*)$");
                    if (listItem.Success)
                    {
                        var replacement = TransformTagValue(listItem.Groups[2].Value, transform);
                        if (replacement is null)
                        {
                            lines.RemoveAt(item);
                            continue;
                        }

                        lines[item] = listItem.Groups[1].Value + replacement + listItem.Groups[3].Value;
                    }

                    item++;
                }

                i = item - 1;
                continue;
            }

            var rewritten = RewriteScalarTagValue(value, transform);
            lines[i] = root.Groups[1].Value + root.Groups[2].Value + root.Groups[3].Value + rewritten;
        }

        return string.Join(newline, lines) + content[bodyStart..];
    }

    private static string RewriteScalarTagValue(string value, Func<string, string?> transform)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            var items = trimmed[1..^1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => TransformTagValue(item, transform))
                .Where(item => item is not null)
                .ToList();
            return $"[{string.Join(", ", items)}]";
        }

        var separator = trimmed.Contains(',') ? ", " : " ";
        var scalarItems = Regex.Split(trimmed, @"[,\s]+")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => TransformTagValue(item, transform))
            .Where(item => item is not null);
        return string.Join(separator, scalarItems);
    }

    private static string? TransformTagValue(string rawValue, Func<string, string?> transform)
    {
        var trimmed = rawValue.Trim();
        if (trimmed.Length == 0)
        {
            return rawValue;
        }

        var quoted = trimmed.Length >= 2 &&
                     ((trimmed[0] == '"' && trimmed[^1] == '"') ||
                      (trimmed[0] == '\'' && trimmed[^1] == '\''));
        var quote = quoted ? trimmed[0].ToString() : "";
        var semantic = quoted ? trimmed[1..^1] : trimmed;
        var hashPrefix = semantic.StartsWith('#');
        semantic = semantic.TrimStart('#').Trim();

        var replacement = transform(semantic);
        if (replacement is null)
        {
            return null;
        }

        return $"{quote}{(hashPrefix ? "#" : "")}{replacement}{quote}";
    }

    private static string FormatTags(IEnumerable<string> tags, string prefix = "#")
    {
        var values = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => prefix + tag)
            .ToList();
        return values.Count == 0 ? "(none)" : string.Join(", ", values);
    }

    /// <summary>
    /// Finds links that don't resolve against the in-memory index. A link might still point at
    /// a real file sitting in a folder excluded from indexing (.kioku/config.yml's exclude:
    /// list) — that's not actually broken. A full path (containing '/') is checked directly; a
    /// bare note name is searched for by filename anywhere in the vault, since Obsidian resolves
    /// bare wikilinks across folders.
    /// </summary>
    private List<(string Note, string Link)> ScanBrokenLinks(IReadOnlyCollection<Note> notes)
    {
        var allNoteNames = notes
            .Select(n => n.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allNotePaths = notes
            .Select(n => StripMdExtension(n.VaultRelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return notes
            .SelectMany(note => note.OutgoingLinks
                .Select(link => (Note: note.VaultRelativePath, Link: link, Target: link.Split('#')[0].Trim()))
                .Where(link => !string.IsNullOrWhiteSpace(link.Target)
                    && !allNoteNames.Contains(link.Target)
                    && !allNotePaths.Contains(link.Target)
                    && !ExistsOnDisk(config.VaultPath, link.Target))
                .Select(link => (Note: link.Note, Link: link.Link)))
            .ToList();
    }

    /// <summary>
    /// Checks whether a link target exists on disk when its note is excluded from indexing. A
    /// full path (containing '/') is checked directly; a bare note name is searched for by
    /// filename anywhere in the vault, since Obsidian resolves bare wikilinks across folders.
    /// </summary>
    private static bool ExistsOnDisk(string vaultPath, string linkTarget)
    {
        var fileName = linkTarget.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? linkTarget
            : linkTarget + ".md";

        if (linkTarget.Contains('/'))
        {
            return File.Exists(Path.Combine(vaultPath, fileName.Replace('/', Path.DirectorySeparatorChar)));
        }

        return Directory.EnumerateFiles(vaultPath, fileName, SearchOption.AllDirectories).Any();
    }

    private static string NormalizeTag(string tag)
    {
        return tag.ToLowerInvariant()
                  .Replace(' ', '-')
                  .Replace('_', '-');
    }

    private static IEnumerable<string> TokenizeText(string text)
    {
        return text.Split([' ', '\t', '\n', '\r', '-', '_', '.', ',', '/', '\\'],
                          StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Where(w => w.Length >= 2)
                   .Select(w => w.ToLowerInvariant());
    }

    private static float TitleSimilarity(string a, string b)
    {
        var wordsA = TokenizeText(a).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wordsB = TokenizeText(b).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (wordsA.Count == 0 && wordsB.Count == 0)
        {
            return 1.0f;
        }

        if (wordsA.Count == 0 || wordsB.Count == 0)
        {
            return 0.0f;
        }

        var intersection = wordsA.Count(w => wordsB.Contains(w));
        var union = wordsA.Count + wordsB.Count - intersection;
        return union == 0 ? 0 : (float)intersection / union;
    }

    private static float ContentJaccard(string textA, string textB)
    {
        if (string.IsNullOrWhiteSpace(textA) || string.IsNullOrWhiteSpace(textB))
        {
            return 0;
        }

        // Use 3-character shingles on first 2000 chars for performance
        const int limit = 2000;
        var a = textA.Length > limit ? textA[..limit] : textA;
        var b = textB.Length > limit ? textB[..limit] : textB;

        var shinglesA = GetShingles(a);
        var shinglesB = GetShingles(b);

        if (shinglesA.Count == 0 || shinglesB.Count == 0)
        {
            return 0;
        }

        var intersection = shinglesA.Count(s => shinglesB.Contains(s));
        var union = shinglesA.Count + shinglesB.Count - intersection;
        return union == 0 ? 0 : (float)intersection / union;
    }

    private static HashSet<string> GetShingles(string text, int k = 3)
    {
        var shingles = new HashSet<string>(StringComparer.Ordinal);
        var normalized = text.ToLowerInvariant();
        for (int i = 0; i + k <= normalized.Length; i++)
        {
            shingles.Add(normalized.Substring(i, k));
        }
        return shingles;
    }

    [McpServerTool, Description("Suggest the most appropriate vault folder(s) for a note based on content similarity to existing notes.")]
    public string suggest_folder(
        [Description("Name or path of the note to suggest a folder for.")] string note,
        [Description("Maximum number of folder suggestions to return.")] int max_suggestions = 5)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            return KiokuError.InvalidArgument("The 'note' parameter cannot be empty.");
        }

        var found = NoteHelpers.ResolveNote(note, vault);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        var capped = Math.Min(max_suggestions, config.MaxSearchResults);
        var ranked = FolderRanker.RankFolders(found, capped, vault, hybrid, embedding);

        if (ranked.Count == 0)
        {
            return "[info] Could not determine a suitable folder. The vault may have too few notes or embeddings may not be loaded yet.";
        }

        var sb = new StringBuilder($"[ok] Suggested folder(s) for '{found.Name}':\n\n");
        foreach (var (folder, score) in ranked)
        {
            sb.AppendLine($"  {folder}  (score: {score:F2})");
        }

        if (!embedding.IsAvailable)
        {
            sb.AppendLine("\n[info] Semantic embeddings are unavailable — results are keyword-only.");
        }

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, IEnumerable<string> items)
    {
        var list = items.ToList();
        sb.AppendLine($"## {title}");
        if (list.Count == 0)
        {
            sb.AppendLine("_(none)_");
        }
        else
        {
            foreach (var item in list.Take(50))
            {
                sb.AppendLine($"- {item}");
            }
            if (list.Count > 50)
            {
                sb.AppendLine($"- _(... and {list.Count - 50} more)_");
            }
        }
        sb.AppendLine();
    }
}
