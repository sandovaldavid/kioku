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
    EmbeddingService embedding)
{
    // normalize_tags

    [McpServerTool, Description(
        "Normalizes tag formatting across all notes in the vault. " +
        "Converts tags to lowercase and replaces spaces/underscores with hyphens. " +
        "Use dry_run=true to preview changes without modifying files.")]
    public async Task<string> normalize_tags(
        [Description("If true, returns a preview of what would change without modifying any files.")] bool dry_run = false)
    {
        var notes = vault.GetAllNotes().ToList();
        var changes = new List<(string NotePath, string OldTag, string NewTag)>();

        foreach (var note in notes)
        {
            foreach (var tag in note.Metadata.Tags)
            {
                var normalized = NormalizeTag(tag);
                if (!string.Equals(tag, normalized, StringComparison.Ordinal))
                {
                    changes.Add((note.VaultRelativePath, tag, normalized));
                }
            }
        }

        if (changes.Count == 0)
        {
            return "[ok] All tags are already normalized. No changes needed.";
        }

        if (dry_run)
        {
            var sb = new StringBuilder($"[info] dry_run=true — {changes.Count} tag(s) would be normalized:\n\n");
            foreach (var (path, old, @new) in changes)
            {
                sb.AppendLine($"  {path}: #{old} → #{@new}");
            }
            return sb.ToString();
        }

        // Group by note path and apply all changes to each note at once
        var byNote = changes.GroupBy(c => c.NotePath).ToList();
        var updatedCount = 0;

        foreach (var group in byNote)
        {
            var note = vault.GetNote(group.Key) ?? vault.GetNoteByName(Path.GetFileNameWithoutExtension(group.Key));
            if (note is null)
            {
                continue;
            }

            var rawContent = await File.ReadAllTextAsync(note.FilePath, Encoding.UTF8);
            var newContent = rawContent;

            foreach (var (_, old, @new) in group)
            {
                // Replace in frontmatter tag lines: "  - OldTag"
                newContent = Regex.Replace(newContent,
                    $@"(^\s*-\s+){Regex.Escape(old)}(\s*$)",
                    $"$1{@new}$2",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase);
            }

            if (!string.Equals(rawContent, newContent, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(note.FilePath, newContent, Encoding.UTF8);
                updatedCount++;
            }
        }

        return $"[ok] Normalized {changes.Count} tag(s) across {updatedCount} note(s).";
    }

    // rename_tag_globally

    [McpServerTool, Description(
        "Renames a tag in every note across the entire vault. " +
        "Use dry_run=true to preview which notes would be affected without modifying files.")]
    public async Task<string> rename_tag_globally(
        [Description("The tag to rename (without the # prefix).")] string old_tag,
        [Description("The new tag name (without the # prefix).")] string new_tag,
        [Description("If true, returns a preview of which notes would change without modifying any files.")] bool dry_run = false)
    {
        if (string.IsNullOrWhiteSpace(old_tag) || string.IsNullOrWhiteSpace(new_tag))
        {
            return "[error] Both old_tag and new_tag are required.";
        }

        var affected = vault.GetAllNotes()
            .Where(n => n.Metadata.Tags.Any(t => t.Equals(old_tag, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (affected.Count == 0)
        {
            return $"[info] Tag '#{old_tag}' not found in any note.";
        }

        if (dry_run)
        {
            var sb = new StringBuilder($"[info] dry_run=true — #{old_tag} → #{new_tag} would affect {affected.Count} note(s):\n\n");
            foreach (var note in affected)
            {
                sb.AppendLine($"  {note.VaultRelativePath}");
            }
            return sb.ToString();
        }

        var updatedCount = 0;
        foreach (var note in affected)
        {
            var rawContent = await File.ReadAllTextAsync(note.FilePath, Encoding.UTF8);

            // Replace in YAML list format: "  - old_tag"
            var newContent = Regex.Replace(rawContent,
                $@"(^\s*-\s+){Regex.Escape(old_tag)}(\s*$)",
                $"$1{new_tag}$2",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            if (!string.Equals(rawContent, newContent, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(note.FilePath, newContent, Encoding.UTF8);
                updatedCount++;
            }
        }

        return $"[ok] Renamed #{old_tag} → #{new_tag} in {updatedCount} note(s).";
    }

    // merge_tags

    [McpServerTool, Description(
        "Merges two tags into one across the entire vault. " +
        "All notes containing source_tag will have it replaced with target_tag. " +
        "Use dry_run=true to preview changes without modifying files.")]
    public async Task<string> merge_tags(
        [Description("The tag to merge away (will be replaced).")] string source_tag,
        [Description("The tag to merge into (will remain).")] string target_tag,
        [Description("If true, returns a preview without modifying any files.")] bool dry_run = false)
    {
        if (string.IsNullOrWhiteSpace(source_tag) || string.IsNullOrWhiteSpace(target_tag))
        {
            return "[error] Both source_tag and target_tag are required.";
        }

        if (source_tag.Equals(target_tag, StringComparison.OrdinalIgnoreCase))
        {
            return "[error] source_tag and target_tag cannot be the same.";
        }

        var affected = vault.GetAllNotes()
            .Where(n => n.Metadata.Tags.Any(t => t.Equals(source_tag, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (affected.Count == 0)
        {
            return $"[info] Tag '#{source_tag}' not found in any note. Nothing to merge.";
        }

        if (dry_run)
        {
            var sb = new StringBuilder($"[info] dry_run=true — #{source_tag} → #{target_tag} would affect {affected.Count} note(s):\n\n");
            foreach (var note in affected)
            {
                sb.AppendLine($"  {note.VaultRelativePath}");
            }
            return sb.ToString();
        }

        var updatedCount = 0;
        foreach (var note in affected)
        {
            var rawContent = await File.ReadAllTextAsync(note.FilePath, Encoding.UTF8);

            // If target_tag already exists, just remove source_tag line
            var hasTarget = note.Metadata.Tags.Any(t => t.Equals(target_tag, StringComparison.OrdinalIgnoreCase));

            string newContent;
            if (hasTarget)
            {
                // Remove the source_tag line entirely
                newContent = Regex.Replace(rawContent,
                    $@"^\s*-\s+{Regex.Escape(source_tag)}\s*\n?",
                    string.Empty,
                    RegexOptions.Multiline | RegexOptions.IgnoreCase);
            }
            else
            {
                // Replace source_tag with target_tag
                newContent = Regex.Replace(rawContent,
                    $@"(^\s*-\s+){Regex.Escape(source_tag)}(\s*$)",
                    $"$1{target_tag}$2",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase);
            }

            if (!string.Equals(rawContent, newContent, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(note.FilePath, newContent, Encoding.UTF8);
                updatedCount++;
            }
        }

        return $"[ok] Merged #{source_tag} → #{target_tag} in {updatedCount} note(s).";
    }

    // suggest_tags

    [McpServerTool, Description(
        "Suggests existing tags from the vault that are relevant to the given note's content. " +
        "Uses keyword overlap between the note's text and existing tags.")]
    public Task<string> suggest_tags(
        [Description("Name or path of the note to suggest tags for.")] string note,
        [Description("Maximum number of tag suggestions to return.")] int max_suggestions = 10)
    {
        var found = vault.GetNote(note) ?? vault.GetNoteByName(note);
        if (found is null)
        {
            return Task.FromResult($"[error] Note not found: '{note}'");
        }

        // Get all unique tags across the vault
        var allTags = vault.GetAllNotes()
            .SelectMany(n => n.Metadata.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var existingTags = found.Metadata.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Score tags by word overlap with note content + title
        var noteWords = TokenizeText(found.PlainText + " " + found.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scored = allTags
            .Where(kv => !existingTags.Contains(kv.Key))
            .Select(kv =>
            {
                var tagWords = TokenizeText(kv.Key.Replace('-', ' ').Replace('_', ' '));
                var overlap = tagWords.Count(w => noteWords.Contains(w));
                return (Tag: kv.Key, Score: overlap * 2 + (kv.Value / 10.0));
            })
            .Where(s => s.Score > 0)
            .OrderByDescending(s => s.Score)
            .Take(max_suggestions)
            .ToList();

        if (scored.Count == 0)
        {
            return Task.FromResult($"[info] No relevant tags found for '{found.Name}'. The note may need more content.");
        }

        var sb = new StringBuilder($"[ok] Suggested tags for '{found.Name}' ({scored.Count} suggestions):\n\n");
        foreach (var (tag, _) in scored)
        {
            sb.AppendLine($"  #{tag}");
        }

        return Task.FromResult(sb.ToString());
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

    // find_broken_links

    [McpServerTool, Description(
        "Scans the entire vault for broken wikilinks — links that point to notes that do not exist.")]
    public Task<string> find_broken_links(
        [Description("Folder to scope the scan (relative to vault root). Leave empty for full vault.")] string folder = "")
    {
        var notes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes()
            : vault.GetNotesInFolder(folder);

        var allNotesList = vault.GetAllNotes().ToList();
        var allNoteNames = allNotesList
            .Select(n => n.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allNotePaths = allNotesList
            .Select(n => n.VaultRelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? n.VaultRelativePath[..^3]
                : n.VaultRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var broken = new List<(string NoteRelPath, string BrokenLink)>();

        foreach (var note in notes)
        {
            foreach (var link in note.OutgoingLinks)
            {
                // Strip anchor fragments (#heading)
                var linkTarget = link.Split('#')[0].Trim();
                if (!string.IsNullOrWhiteSpace(linkTarget)
                    && !allNoteNames.Contains(linkTarget)
                    && !allNotePaths.Contains(linkTarget))
                {
                    broken.Add((note.VaultRelativePath, link));
                }
            }
        }

        if (broken.Count == 0)
        {
            var scopeDesc = string.IsNullOrWhiteSpace(folder) ? "vault" : $"'{folder}'";
            return Task.FromResult($"[ok] No broken links found in {scopeDesc}.");
        }

        var sb = new StringBuilder($"[ok] Found {broken.Count} broken link(s):\n\n");
        foreach (var (path, link) in broken.OrderBy(x => x.NoteRelPath))
        {
            sb.AppendLine($"  {path}: [[{link}]]");
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

        // Broken links
        var allNoteNames = notes.Select(n => n.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allNotePaths = notes
            .Select(n => n.VaultRelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? n.VaultRelativePath[..^3]
                : n.VaultRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var brokenLinks = notes
            .SelectMany(n => n.OutgoingLinks
                .Select(l => l.Split('#')[0].Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l)
                    && !allNoteNames.Contains(l)
                    && !allNotePaths.Contains(l))
                .Select(l => (Note: n.VaultRelativePath, Link: l)))
            .ToList();

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

    // Private helpers

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
            return "[error] The 'note' parameter cannot be empty.";
        }

        var found = vault.GetNote(note) ?? vault.GetNoteByName(note);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'";
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

    [McpServerTool, Description("Move a note to the most appropriate folder based on its content. Uses the same scoring as suggest_folder.")]
    public string reclassify_note(
        [Description("Name or path of the note to reclassify.")] string note,
        [Description("If true, returns the suggested destination without moving the file.")] bool dry_run = false)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            return "[error] The 'note' parameter cannot be empty.";
        }

        var found = vault.GetNote(note) ?? vault.GetNoteByName(note);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'";
        }

        var ranked = FolderRanker.RankFolders(found, 1, vault, hybrid, embedding);

        if (ranked.Count == 0)
        {
            return "[info] Could not determine a suitable folder. The vault may have too few notes or embeddings may not be loaded yet.";
        }

        var (bestFolder, bestScore) = ranked[0];
        var currentFolder = Path.GetDirectoryName(found.VaultRelativePath) ?? "";

        if (bestFolder.Equals(currentFolder, StringComparison.OrdinalIgnoreCase))
        {
            return $"[info] Note is already in the best-matching folder: '{currentFolder}'.";
        }

        if (dry_run)
        {
            return $"[info] dry_run=true — would move '{found.Name}':\n  From: {currentFolder}\n  To:   {bestFolder}  (score: {bestScore:F2})";
        }

        var destDir = NoteHelpers.EnsureInsideVault(
            config.VaultPath,
            Path.Combine(config.VaultPath, bestFolder));
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, Path.GetFileName(found.FilePath));

        if (File.Exists(destPath))
        {
            return $"[error] A note named '{found.Name}' already exists in '{bestFolder}'.";
        }

        File.Move(found.FilePath, destPath);
        var newRelativePath = Path.GetRelativePath(config.VaultPath, destPath);

        return $"[ok] Note reclassified:\n   Before: {found.VaultRelativePath}\n   After:  {newRelativePath}";
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
