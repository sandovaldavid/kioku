using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for academic research workflows: citation export and literature gap analysis.
/// All operations are read-only and require no external dependencies.
/// </summary>
[McpServerToolType]
public sealed partial class ResearchTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    VaultConfigService vaultConfig)
{
    // -------------------------------------------------------------------------
    // import_bibtex
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Imports a BibTeX (.bib) file or raw BibTeX content as literature notes, one per entry. " +
        "Parses tolerantly: malformed entries are reported individually rather than aborting the " +
        "whole import. Deduplicates by 'citekey' — re-importing the same file never creates " +
        "duplicates. All BibTeX fields are stored in frontmatter, so export_citations(format='bibtex') can reconstruct " +
        "the original entries losslessly. Use dry_run=true to preview before writing.")]
    public async Task<string> import_bibtex(
        [Description("Path to a .bib file (absolute, vault-relative, or CWD-relative), or raw BibTeX content.")] string source,
        [Description("Folder to create literature notes in. Default: the configured 'literature' folder, or 'Literature'.")] string folder = "",
        [Description("If a note with the same citekey already exists, refresh its frontmatter fields (body is left untouched). Default: skip existing entries.")] bool update_existing = false,
        [Description("Preview what would be created/updated/skipped without writing any files.")] bool dry_run = false)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        string content;
        var sourcePath = ResolveSourcePath(source);
        if (sourcePath is not null)
        {
            content = await File.ReadAllTextAsync(sourcePath, Encoding.UTF8);
        }
        else
        {
            content = source;
        }

        var parsed = BibtexParser.Parse(content);
        if (parsed.Entries.Count == 0 && parsed.Errors.Count == 0)
        {
            return "[error] No BibTeX entries found in the given source.";
        }

        var targetFolder = string.IsNullOrWhiteSpace(folder)
            ? (vaultConfig.GetFolder("literature") ?? "Literature")
            : folder;

        var existingByCitekey = BuildCitekeyIndex();
        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var created = new List<string>();
        var updatedEntries = new List<string>();
        var skipped = new List<string>();

        foreach (var entry in parsed.Entries)
        {
            if (existingByCitekey.TryGetValue(entry.CiteKey, out var existingNote))
            {
                if (!update_existing)
                {
                    skipped.Add($"`{entry.CiteKey}` — already exists at {existingNote.VaultRelativePath}");
                    continue;
                }

                if (!dry_run)
                {
                    await UpdateLiteratureNoteFrontmatterAsync(existingNote, entry);
                }

                updatedEntries.Add($"`{entry.CiteKey}` — {existingNote.VaultRelativePath}");
                continue;
            }

            var fileName = BuildUniqueFileName(entry, targetFolder, usedFileNames);
            usedFileNames.Add(fileName);

            if (!dry_run)
            {
                await CreateLiteratureNoteFromBibtexAsync(entry, targetFolder, fileName);
            }

            created.Add($"`{entry.CiteKey}` — {targetFolder.TrimEnd('/')}/{fileName}.md");
        }

        return FormatImportReport(dry_run, created, updatedEntries, skipped, parsed.Errors);
    }

    // -------------------------------------------------------------------------
    // BibTeX helpers
    // -------------------------------------------------------------------------

    private string? ResolveSourcePath(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        if (Path.IsPathRooted(source) && File.Exists(source))
        {
            return source;
        }

        var vaultRelative = Path.Combine(config.VaultPath, source);
        if (File.Exists(vaultRelative))
        {
            return vaultRelative;
        }

        if (File.Exists(source))
        {
            return Path.GetFullPath(source);
        }

        return null;
    }

    private Dictionary<string, Note> BuildCitekeyIndex()
    {
        var index = new Dictionary<string, Note>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in vault.GetAllNotes())
        {
            var citekey = GetCitekey(note);
            if (!string.IsNullOrWhiteSpace(citekey) &&
                !index.ContainsKey(citekey))
            {
                index[citekey] = note;
            }
        }

        return index;
    }

    private string BuildUniqueFileName(BibtexEntry entry, string folder, HashSet<string> usedInBatch)
    {
        entry.Fields.TryGetValue("title", out var title);
        entry.Fields.TryGetValue("year", out var year);

        var baseName = $"{year ?? "n.d."}-{NoteHelpers.SanitizeFileName(title ?? entry.CiteKey)}";
        var withCitekeySuffix = $"{baseName}-{NoteHelpers.SanitizeFileName(entry.CiteKey)}";

        if (usedInBatch.Contains(baseName) || File.Exists(BuildFilePath(folder, baseName)))
        {
            return withCitekeySuffix;
        }

        return baseName;
    }

    private string BuildFilePath(string folder, string fileName) =>
        NoteHelpers.BuildFilePath($"{folder.TrimEnd('/')}/{fileName}", config.VaultPath);

    private async Task CreateLiteratureNoteFromBibtexAsync(BibtexEntry entry, string folder, string fileName)
    {
        var filePath = BuildFilePath(folder, fileName);
        var extraFields = BuildExtraFields(entry);
        var body = BuildBibtexNoteBody(entry);

        var frontmatter = NoteHelpers.BuildFrontmatter(
            ["literature"], "literature", "draft",
            DateOnly.FromDateTime(DateTime.Today), extraFields: extraFields);

        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(filePath, frontmatter + "\n" + body, NoteHelpers.Utf8NoBom);
    }

    private async Task UpdateLiteratureNoteFrontmatterAsync(Note existingNote, BibtexEntry entry)
    {
        var rawContent = await File.ReadAllTextAsync(existingNote.FilePath, Encoding.UTF8);
        var bodyStart = FrontmatterParser.GetBodyStart(rawContent);
        var body = rawContent[bodyStart..];

        var existingMeta = existingNote.Metadata;
        var extraFields = BuildExtraFields(entry);

        var frontmatter = NoteHelpers.BuildFrontmatter(
            existingMeta.Tags, existingMeta.NoteType, existingMeta.Status,
            existingMeta.Date, domain: existingMeta.Domain, extraFields: extraFields);

        await File.WriteAllTextAsync(existingNote.FilePath, frontmatter + body, NoteHelpers.Utf8NoBom);
    }

    private static Dictionary<string, string> BuildExtraFields(BibtexEntry entry)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["citekey"] = FormatFrontmatterValue(entry.CiteKey),
            ["bibtex-type"] = FormatFrontmatterValue(entry.Type),
        };

        foreach (var (name, value) in entry.Fields)
        {
            fields[name] = FormatFrontmatterValue(value);
        }

        return fields;
    }

    private static string FormatFrontmatterValue(string value)
    {
        var singleLine = value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return $"\"{singleLine}\"";
    }

    private static string BuildBibtexNoteBody(BibtexEntry entry)
    {
        entry.Fields.TryGetValue("title", out var title);
        entry.Fields.TryGetValue("author", out var author);
        entry.Fields.TryGetValue("year", out var year);
        entry.Fields.TryGetValue("journal", out var journal);
        entry.Fields.TryGetValue("booktitle", out var booktitle);
        entry.Fields.TryGetValue("doi", out var doi);
        entry.Fields.TryGetValue("url", out var url);
        entry.Fields.TryGetValue("abstract", out var abstractText);

        var sb = new StringBuilder();
        sb.AppendLine($"# {title ?? entry.CiteKey}\n");
        sb.AppendLine("## Metadata\n");
        sb.AppendLine($"- **Author:** {author ?? "Unknown"}");
        sb.AppendLine($"- **Year:** {year ?? "n.d."}");

        var venue = !string.IsNullOrWhiteSpace(journal) ? journal : booktitle;
        if (!string.IsNullOrWhiteSpace(venue))
        {
            sb.AppendLine($"- **Venue:** {venue}");
        }

        if (!string.IsNullOrWhiteSpace(doi))
        {
            sb.AppendLine($"- **DOI:** {doi}");
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            sb.AppendLine($"- **URL:** {url}");
        }

        sb.AppendLine("\n## Summary\n");
        sb.AppendLine(!string.IsNullOrWhiteSpace(abstractText) ? abstractText : "*Add your summary here.*");
        sb.AppendLine("\n## Key Ideas\n");
        sb.AppendLine("- ");
        sb.AppendLine("\n## Quotes\n");
        sb.AppendLine("> ");
        sb.AppendLine("\n## My Notes\n");
        sb.AppendLine("*Add your personal reflections here.*");

        return sb.ToString();
    }

    private static void AppendBibtexEntry(StringBuilder sb, IReadOnlyDictionary<string, string> extraFields)
    {
        var citekey = extraFields.GetValueOrDefault("citekey", "unknown");
        var type = extraFields.GetValueOrDefault("bibtex-type", "misc");

        sb.AppendLine($"@{type}{{{citekey},");

        foreach (var (name, value) in extraFields)
        {
            if (name.Equals("citekey", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("bibtex-type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.AppendLine($"  {name} = {{{value}}},");
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static IReadOnlyDictionary<string, string> BuildBibtexFields(Note note, string citekey)
    {
        var fields = new Dictionary<string, string>(note.Metadata.ExtraFields, StringComparer.OrdinalIgnoreCase)
        {
            ["citekey"] = citekey,
        };

        // Normalize aliases to the canonical field so exported BibTeX can be imported again.
        fields.Remove("citation-key");
        fields.Remove("key");
        return fields;
    }

    private static string FormatImportReport(
        bool dryRun,
        List<string> created,
        List<string> updated,
        List<string> skipped,
        IReadOnlyList<string> parseErrors)
    {
        var sb = new StringBuilder();
        var verb = dryRun ? "[dry-run] Would import" : "[ok] Imported";
        sb.AppendLine($"{verb} {created.Count + updated.Count} entries " +
                      $"({created.Count} new, {updated.Count} updated, {skipped.Count} skipped, {parseErrors.Count} failed to parse):");
        sb.AppendLine();

        if (created.Count > 0)
        {
            sb.AppendLine(dryRun ? "**Would create:**" : "**Created:**");
            foreach (var line in created)
            {
                sb.AppendLine($"- {line}");
            }

            sb.AppendLine();
        }

        if (updated.Count > 0)
        {
            sb.AppendLine(dryRun ? "**Would update:**" : "**Updated:**");
            foreach (var line in updated)
            {
                sb.AppendLine($"- {line}");
            }

            sb.AppendLine();
        }

        if (skipped.Count > 0)
        {
            sb.AppendLine("**Skipped (already exist):**");
            foreach (var line in skipped)
            {
                sb.AppendLine($"- {line}");
            }

            sb.AppendLine();
        }

        if (parseErrors.Count > 0)
        {
            sb.AppendLine("**Parse errors:**");
            foreach (var error in parseErrors)
            {
                sb.AppendLine($"- {error}");
            }
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // export_citations
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Exports citation keys found in note frontmatter as a full-fidelity BibTeX document or Markdown table. " +
        "The BibTeX format preserves fields imported by import_bibtex for round-trip export. " +
        "Accepted formats are exactly 'bibtex' and 'markdown'.")]
    public string export_citations(
        [Description("Export format: 'bibtex' for a round-trip BibTeX document or 'markdown' for a Markdown table (default: markdown).")] string format = "markdown",
        [Description("Folder to scan (vault-relative). Leave empty to scan the entire vault.")] string folder = "")
    {
        if (!string.Equals(format, "bibtex", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase))
        {
            return $"[error] Invalid export format '{format}'. Supported formats are 'bibtex' and 'markdown'.";
        }

        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var notes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes()
            : vault.GetNotesInFolder(folder);

        // Gather notes that have a citekey in ExtraFields or in NoteType heuristic
        var withCitekey = notes
            .Select(n =>
            {
                var citekey = GetCitekey(n) ?? string.Empty;

                var author = n.Metadata.ExtraFields.TryGetValue("author", out var a) ? a
                    : n.Metadata.ExtraFields.TryGetValue("authors", out var a2) ? a2 : "Unknown";

                var year = n.Metadata.ExtraFields.TryGetValue("year", out var y) ? y
                    : n.Metadata.Date?.Year.ToString() ?? "n.d.";

                var title = n.Metadata.ExtraFields.TryGetValue("title", out var t) ? t : n.Name;

                return (note: n, citekey, author, year, title);
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.citekey))
            .OrderBy(x => x.citekey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (withCitekey.Count == 0)
        {
            return "[ok] No notes with 'citekey' found in the vault. " +
                   "Add 'citekey: authorYYYY' to your literature note frontmatter.";
        }

        var sb = new StringBuilder();

        if (format.Equals("bibtex", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"[ok] Exported {withCitekey.Count} BibTeX entries:");
            sb.AppendLine();

            foreach (var (note, citekey, _, _, _) in withCitekey)
            {
                AppendBibtexEntry(sb, BuildBibtexFields(note, citekey));
            }
        }
        else
        {
            sb.AppendLine($"[ok] Citation export — {withCitekey.Count} notes:");
            sb.AppendLine();
            sb.AppendLine("| Citekey | Author | Year | Title | Note |");
            sb.AppendLine("|---------|--------|------|-------|------|");

            foreach (var (note, citekey, author, year, title) in withCitekey)
            {
                var truncTitle = title.Length > 60 ? title[..57] + "..." : title;
                var truncAuthor = author.Length > 30 ? author[..27] + "..." : author;
                sb.AppendLine($"| `{citekey}` | {truncAuthor} | {year} | {truncTitle} | [{note.Name}]({note.VaultRelativePath}) |");
            }
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Literature-gap audit section
    // -------------------------------------------------------------------------

    private string BuildLiteratureGapReport(string folder)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var allNotes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes().ToList()
            : vault.GetNotesInFolder(folder).ToList();

        // Build set of known citekeys from frontmatter
        var knownCitekeys = allNotes
            .Select(GetCitekey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Find inline citekey references: [@citekey] or @citekey patterns
        var inlineCiteRegex = InlineCitePattern();

        var referencedCitekeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var note in allNotes)
        {
            var matches = inlineCiteRegex.Matches(note.RawContent);
            foreach (Match match in matches)
            {
                var citekey = match.Groups["key"].Value;
                if (!referencedCitekeys.ContainsKey(citekey))
                {
                    referencedCitekeys[citekey] = [];
                }

                referencedCitekeys[citekey].Add(note.Name);
            }
        }

        var gaps = referencedCitekeys
            .Where(kv => !knownCitekeys.Contains(kv.Key))
            .OrderBy(kv => kv.Key)
            .ToList();

        if (gaps.Count == 0)
        {
            return $"[ok] No literature gaps found. All {referencedCitekeys.Count} referenced citekeys have corresponding literature notes.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[ok] Literature gaps — {gaps.Count} missing notes:");
        sb.AppendLine();
        sb.AppendLine("| Missing Citekey | Referenced In |");
        sb.AppendLine("|----------------|---------------|");

        foreach (var (citekey, sources) in gaps)
        {
            var sourceList = string.Join(", ", sources.Distinct().Take(5));
            if (sources.Distinct().Count() > 5)
            {
                sourceList += $" (+{sources.Distinct().Count() - 5} more)";
            }

            sb.AppendLine($"| `@{citekey}` | {sourceList} |");
        }

        sb.AppendLine();
        sb.AppendLine($"**{knownCitekeys.Count}** notes with citekey found · **{referencedCitekeys.Count}** total referenced · **{gaps.Count}** missing");

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Citation-graph audit section
    // -------------------------------------------------------------------------

    private string BuildCitationGraphReport(string folder)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var candidateSources = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes().ToList()
            : vault.GetNotesInFolder(folder).ToList();

        var sources = candidateSources
            .Select(n => (Note: n, Citekey: GetCitekey(n)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Citekey))
            .ToList();

        if (sources.Count == 0)
        {
            return "[ok] No notes with 'citekey' found in the vault. " +
                   "Import a .bib file with import_bibtex, or add 'citekey' to a literature note's frontmatter.";
        }

        // citekey -> set of citing note names, deduplicated across the wikilink + inline signals
        var citersByKey = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sourceNote, citekey) in sources)
        {
            var citers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var backlink in vault.GetBacklinks(sourceNote.Name))
            {
                citers.Add(backlink.Name);
            }

            citersByKey[citekey!] = citers;
        }

        var inlineCiteRegex = InlineCitePattern();
        foreach (var note in vault.GetAllNotes())
        {
            foreach (Match match in inlineCiteRegex.Matches(note.RawContent))
            {
                if (citersByKey.TryGetValue(match.Groups["key"].Value, out var citers))
                {
                    citers.Add(note.Name);
                }
            }
        }

        var ranked = sources
            .Select(x => (x.Note, Citekey: x.Citekey!, Citers: citersByKey[x.Citekey!]))
            .OrderByDescending(x => x.Citers.Count)
            .ThenBy(x => x.Citekey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cited = ranked.Where(x => x.Citers.Count > 0).ToList();
        var orphans = ranked.Where(x => x.Citers.Count == 0).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"[ok] Citation graph — {sources.Count} source(s), {cited.Count} cited, {orphans.Count} orphan(s):");
        sb.AppendLine();

        if (cited.Count > 0)
        {
            sb.AppendLine("**Most cited:**");
            sb.AppendLine();
            sb.AppendLine("| Citekey | Source | Citations | Cited By |");
            sb.AppendLine("|---------|--------|-----------|----------|");

            foreach (var (note, citekey, citers) in cited)
            {
                var orderedCiters = citers.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
                var citerList = string.Join(", ", orderedCiters.Take(5));
                if (orderedCiters.Count > 5)
                {
                    citerList += $" (+{orderedCiters.Count - 5} more)";
                }

                sb.AppendLine($"| `{citekey}` | {note.Name} | {citers.Count} | {citerList} |");
            }

            sb.AppendLine();
        }

        if (orphans.Count > 0)
        {
            sb.AppendLine("**Orphan sources (never cited):**");
            foreach (var (note, citekey, _) in orphans)
            {
                sb.AppendLine($"- `{citekey}` — {note.Name} ({note.VaultRelativePath})");
            }
        }
        else
        {
            sb.AppendLine("**Orphan sources:** none — every source is cited at least once.");
        }

        return sb.ToString();
    }

    [McpServerTool, Description(
        "Audits citations in one combined report: citation graph and orphan sources, inline citation gaps, " +
        "and required metadata on research/literature notes. The folder scopes source and audit notes; " +
        "citation graph citers are still searched across the entire vault.")]
    public string audit_citations(
        [Description("Folder to scope source notes, inline-gap notes, and metadata validation (vault-relative). Leave empty for the entire vault.")] string folder = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var scope = string.IsNullOrWhiteSpace(folder) ? "entire vault" : $"folder '{folder}'";
        var sb = new StringBuilder();
        sb.AppendLine($"[ok] Citation audit ({scope}):");
        sb.AppendLine();
        sb.AppendLine("## Citation graph");
        sb.AppendLine(BuildCitationGraphReport(folder));
        sb.AppendLine("## Literature gaps");
        sb.AppendLine(BuildLiteratureGapReport(folder));
        sb.AppendLine("## Metadata validation");
        sb.AppendLine(BuildResearchValidationReport(folder));
        return sb.ToString();
    }

    private string BuildResearchValidationReport(string folder)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var allNotes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes().ToList()
            : vault.GetNotesInFolder(folder).ToList();

        var researchNotes = new List<(Note Note, List<string> MissingFields)>();

        foreach (var note in allNotes)
        {
            var noteType = note.Metadata.NoteType ??
                           (note.Metadata.ExtraFields.TryGetValue("type", out var t) ? t : null);
            var isResearch = noteType is not null &&
                             (noteType.Equals("literature", StringComparison.OrdinalIgnoreCase) ||
                              noteType.Equals("research", StringComparison.OrdinalIgnoreCase));

            if (!isResearch)
            {
                continue;
            }

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(GetCitekey(note)))
            {
                missing.Add("citekey");
            }

            if (!note.Metadata.ExtraFields.ContainsKey("year"))
            {
                missing.Add("year");
            }

            if (!note.Metadata.ExtraFields.ContainsKey("authors") &&
                !note.Metadata.ExtraFields.ContainsKey("author"))
            {
                missing.Add("authors");
            }

            if (string.IsNullOrWhiteSpace(note.Metadata.Status))
            {
                missing.Add("status");
            }

            if (!note.Metadata.Updated.HasValue)
            {
                missing.Add("updated");
            }

            researchNotes.Add((note, missing));
        }

        if (researchNotes.Count == 0)
        {
            return "[info] No research or literature notes found in the vault.";
        }

        var problematic = researchNotes.Where(x => x.MissingFields.Count > 0).ToList();

        if (problematic.Count == 0)
        {
            return $"[ok] All {researchNotes.Count} research/literature note(s) are complete.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[info] Validation report — {problematic.Count}/{researchNotes.Count} research note(s) missing fields:");
        sb.AppendLine();
        sb.AppendLine("| Note | Missing Fields |");
        sb.AppendLine("|------|---|");

        foreach (var (note, missing) in problematic.OrderBy(x => x.Note.Name))
        {
            var missingStr = string.Join(", ", missing);
            sb.AppendLine($"| {note.Name} | {missingStr} |");
        }

        sb.AppendLine();
        sb.AppendLine($"**Summary:** {researchNotes.Count} total · {researchNotes.Count - problematic.Count} complete · {problematic.Count} incomplete");

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string? GetCitekey(Note note)
    {
        foreach (var field in new[] { "citekey", "citation-key", "key" })
        {
            if (note.Metadata.ExtraFields.TryGetValue(field, out var citekey) &&
                !string.IsNullOrWhiteSpace(citekey))
            {
                return citekey;
            }
        }

        return null;
    }

    [GeneratedRegex(@"\[@?(?<key>[A-Za-z][A-Za-z0-9_:./-]+)\]|\B@(?<key>[A-Za-z][A-Za-z0-9_:./-]+)", RegexOptions.Compiled)]
    private static partial Regex InlineCitePattern();
}
