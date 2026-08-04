using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for academic research workflows: BibTeX import, citation export, and literature audits.
/// </summary>
[McpServerToolType]
public sealed partial class ResearchTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    VaultConfigService vaultConfig,
    VaultPathPolicy paths,
    IVaultMutationService? mutations = null)
{
    [McpServerTool, Description(
        "Imports a vault-local BibTeX (.bib) file, an explicitly allowlisted external .bib file, " +
        "or raw BibTeX content as literature notes. External file reads are denied by default. " +
        "Use dry_run=true to preview before writing.")]
    public async Task<string> import_bibtex(
        [Description("Vault-relative .bib path, allowlisted absolute .bib path, or raw BibTeX content. Relative paths never use the server CWD.")] string source,
        [Description("Folder to create literature notes in. Default: the configured 'literature' folder, or 'Literature'.")] string folder = "",
        [Description("If a note with the same citekey already exists, refresh its frontmatter fields while preserving its body.")] bool update_existing = false,
        [Description("Preview what would be created, updated, or skipped without writing files.")] bool dry_run = false)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return KiokuError.InvalidArgument("The BibTeX source cannot be empty.");
        }

        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        string content;
        if (LooksLikeInlineBibtex(source) || !LooksLikeFileSource(source))
        {
            content = source;
        }
        else
        {
            string sourcePath;
            try
            {
                sourcePath = paths.ResolveExternalReadPath(source);
            }
            catch (VaultAccessDeniedException)
            {
                return KiokuError.AccessDenied(
                    "BibTeX file access is limited to the vault and explicitly allowlisted external roots.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return KiokuError.AccessDenied("The BibTeX source could not be resolved within the configured security boundary.");
            }

            if (!Path.GetExtension(sourcePath).Equals(".bib", StringComparison.OrdinalIgnoreCase))
            {
                return KiokuError.InvalidArgument("File-based BibTeX imports require a .bib file.");
            }

            if (!File.Exists(sourcePath))
            {
                return KiokuError.NotFound("The requested BibTeX source file was not found.");
            }

            try
            {
                content = await File.ReadAllTextAsync(sourcePath, Encoding.UTF8);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return KiokuError.AccessDenied("The requested BibTeX source file could not be read.");
            }
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

    private static bool LooksLikeInlineBibtex(string source) =>
        source.TrimStart().StartsWith('@');

    private static bool LooksLikeFileSource(string source) =>
        Path.IsPathRooted(source) ||
        source.EndsWith(".bib", StringComparison.OrdinalIgnoreCase) ||
        source.Contains('/') ||
        source.Contains('\\');

    private Dictionary<string, Note> BuildCitekeyIndex()
    {
        var index = new Dictionary<string, Note>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in vault.GetAllNotes())
        {
            var citekey = GetCitekey(note);
            if (!string.IsNullOrWhiteSpace(citekey) && !index.ContainsKey(citekey))
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

        return usedInBatch.Contains(baseName) || File.Exists(BuildFilePath(folder, baseName))
            ? withCitekeySuffix
            : baseName;
    }

    private string BuildFilePath(string folder, string fileName) =>
        NoteHelpers.BuildFilePath($"{folder.TrimEnd('/')}/{fileName}", config.VaultPath);

    private async Task CreateLiteratureNoteFromBibtexAsync(BibtexEntry entry, string folder, string fileName)
    {
        var filePath = BuildFilePath(folder, fileName);
        var frontmatter = NoteHelpers.BuildFrontmatter(
            ["literature"],
            "literature",
            "draft",
            DateOnly.FromDateTime(DateTime.Today),
            extraFields: BuildExtraFields(entry),
            updated: vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(DateTime.Today) : null);

        var content = frontmatter + "\n" + BuildBibtexNoteBody(entry);
        if (mutations is null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, content, NoteHelpers.Utf8NoBom);
        }
        else
        {
            await mutations.CreateTextAsync(filePath, content);
        }
    }

    private async Task UpdateLiteratureNoteFrontmatterAsync(Note existingNote, BibtexEntry entry)
    {
        var rawContent = await File.ReadAllTextAsync(existingNote.FilePath, Encoding.UTF8);
        var document = FrontmatterDocument.Parse(rawContent);

        foreach (var (name, value) in BuildExtraFields(entry))
        {
            document.SetString(name, value);
        }

        if (vaultConfig.MaintainUpdated)
        {
            document.SetDate("updated", DateOnly.FromDateTime(DateTime.Today), "modified");
        }

        if (mutations is null)
        {
            await File.WriteAllTextAsync(existingNote.FilePath, document.Serialize(), NoteHelpers.Utf8NoBom);
        }
        else
        {
            await mutations.WriteTextAsync(existingNote.FilePath, document.Serialize());
        }
    }

    private static Dictionary<string, string> BuildExtraFields(BibtexEntry entry)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["citekey"] = entry.CiteKey,
            ["bibtex-type"] = entry.Type,
        };

        foreach (var (name, value) in entry.Fields)
        {
            fields[name] = value;
        }

        return fields;
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

    [McpServerTool, Description(
        "Exports citation keys found in note frontmatter as a full-fidelity BibTeX document or Markdown table. " +
        "Accepted formats are exactly 'bibtex' and 'markdown'.")]
    public string export_citations(
        [Description("Export format: 'bibtex' or 'markdown'.")] string format = "markdown",
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

        var withCitekey = notes
            .Select(note =>
            {
                var citekey = GetCitekey(note) ?? string.Empty;
                var author = note.Metadata.ExtraFields.TryGetValue("author", out var a)
                    ? a
                    : note.Metadata.ExtraFields.TryGetValue("authors", out var authors) ? authors : "Unknown";
                var year = note.Metadata.ExtraFields.TryGetValue("year", out var y)
                    ? y
                    : note.Metadata.Date?.Year.ToString() ?? "n.d.";
                var title = note.Metadata.ExtraFields.TryGetValue("title", out var t) ? t : note.Name;
                return (note, citekey, author, year, title);
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.citekey))
            .OrderBy(item => item.citekey, StringComparer.OrdinalIgnoreCase)
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

    private string BuildLiteratureGapReport(string folder)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var allNotes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes().ToList()
            : vault.GetNotesInFolder(folder).ToList();
        var knownCitekeys = allNotes
            .Select(GetCitekey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var referencedCitekeys = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in allNotes)
        {
            foreach (Match match in InlineCitePattern().Matches(note.RawContent))
            {
                var citekey = match.Groups["key"].Value;
                if (!referencedCitekeys.TryGetValue(citekey, out var references))
                {
                    references = [];
                    referencedCitekeys[citekey] = references;
                }
                references.Add(note.Name);
            }
        }

        var gaps = referencedCitekeys
            .Where(pair => !knownCitekeys.Contains(pair.Key))
            .OrderBy(pair => pair.Key)
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
            var distinct = sources.Distinct().ToList();
            var sourceList = string.Join(", ", distinct.Take(5));
            if (distinct.Count > 5)
            {
                sourceList += $" (+{distinct.Count - 5} more)";
            }
            sb.AppendLine($"| `@{citekey}` | {sourceList} |");
        }

        sb.AppendLine();
        sb.AppendLine($"**{knownCitekeys.Count}** notes with citekey found · **{referencedCitekeys.Count}** total referenced · **{gaps.Count}** missing");
        return sb.ToString();
    }

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
            .Select(note => (Note: note, Citekey: GetCitekey(note)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Citekey))
            .ToList();

        if (sources.Count == 0)
        {
            return "[ok] No notes with 'citekey' found in the vault. " +
                   "Import a .bib file with import_bibtex, or add 'citekey' to a literature note's frontmatter.";
        }

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

        foreach (var note in vault.GetAllNotes())
        {
            foreach (Match match in InlineCitePattern().Matches(note.RawContent))
            {
                if (citersByKey.TryGetValue(match.Groups["key"].Value, out var citers))
                {
                    citers.Add(note.Name);
                }
            }
        }

        var ranked = sources
            .Select(item => (item.Note, Citekey: item.Citekey!, Citers: citersByKey[item.Citekey!]))
            .OrderByDescending(item => item.Citers.Count)
            .ThenBy(item => item.Citekey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var cited = ranked.Where(item => item.Citers.Count > 0).ToList();
        var orphans = ranked.Where(item => item.Citers.Count == 0).ToList();

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
                var ordered = citers.OrderBy(citer => citer, StringComparer.OrdinalIgnoreCase).ToList();
                var citerList = string.Join(", ", ordered.Take(5));
                if (ordered.Count > 5)
                {
                    citerList += $" (+{ordered.Count - 5} more)";
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
        "and required metadata on research/literature notes.")]
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
                           (note.Metadata.ExtraFields.TryGetValue("type", out var type) ? type : null);
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

        var problematic = researchNotes.Where(item => item.MissingFields.Count > 0).ToList();
        if (problematic.Count == 0)
        {
            return $"[ok] All {researchNotes.Count} research/literature note(s) are complete.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[info] Validation report — {problematic.Count}/{researchNotes.Count} research note(s) missing fields:");
        sb.AppendLine();
        sb.AppendLine("| Note | Missing Fields |");
        sb.AppendLine("|------|---|");
        foreach (var (note, missing) in problematic.OrderBy(item => item.Note.Name))
        {
            sb.AppendLine($"| {note.Name} | {string.Join(", ", missing)} |");
        }
        sb.AppendLine();
        sb.AppendLine($"**Summary:** {researchNotes.Count} total · {researchNotes.Count - problematic.Count} complete · {problematic.Count} incomplete");
        return sb.ToString();
    }

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
