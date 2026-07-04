using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Markdig;
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
    IHttpClientFactory httpClientFactory,
    VaultConfigService vaultConfig)
{
    // -------------------------------------------------------------------------
    // import_bibtex
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Imports a BibTeX (.bib) file or raw BibTeX content as literature notes, one per entry. " +
        "Parses tolerantly: malformed entries are reported individually rather than aborting the " +
        "whole import. Deduplicates by 'citekey' — re-importing the same file never creates " +
        "duplicates. All BibTeX fields are stored in frontmatter, so export_bibtex can reconstruct " +
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
    // export_bibtex
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Reconstructs a BibTeX (.bib) document from literature notes that carry a 'citekey' in " +
        "frontmatter, including notes originally created by import_bibtex. Complements " +
        "export_citations (which exports Markdown/BibTeX stubs) with a full round-trip-capable export.")]
    public string export_bibtex(
        [Description("Folder to scan (vault-relative). Leave empty to scan the entire vault.")] string folder = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var notes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes()
            : vault.GetNotesInFolder(folder);

        var withCitekey = notes
            .Where(n => n.Metadata.ExtraFields.ContainsKey("citekey"))
            .OrderBy(n => n.Metadata.ExtraFields["citekey"], StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (withCitekey.Count == 0)
        {
            return "[ok] No notes with 'citekey' found in the vault. Import a .bib file with import_bibtex first.";
        }

        var sb = new StringBuilder();
        foreach (var note in withCitekey)
        {
            AppendBibtexEntry(sb, note.Metadata.ExtraFields);
        }

        return $"[ok] Exported {withCitekey.Count} BibTeX entries:\n\n{sb}";
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
            if (note.Metadata.ExtraFields.TryGetValue("citekey", out var citekey) &&
                !string.IsNullOrWhiteSpace(citekey) &&
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
        await File.WriteAllTextAsync(filePath, frontmatter + "\n" + body, Encoding.UTF8);
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

        await File.WriteAllTextAsync(existingNote.FilePath, frontmatter + body, Encoding.UTF8);
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
        "Exports citation keys found in note frontmatter ('citekey' field) as a BibTeX stub list or Markdown table. " +
        "Useful for building a bibliography from your literature notes. " +
        "Each note with a 'citekey' in its extra frontmatter fields is included.")]
    public string export_citations(
        [Description("Export format: 'bib' for BibTeX stubs, 'markdown' for a Markdown table (default: markdown).")] string format = "markdown",
        [Description("Folder to scan (vault-relative). Leave empty to scan the entire vault.")] string folder = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var notes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes()
            : vault.GetNotesInFolder(folder);

        // Gather notes that have a citekey in ExtraFields or in NoteType heuristic
        var withCitekey = notes
            .Where(n =>
                n.Metadata.ExtraFields.ContainsKey("citekey") ||
                n.Metadata.ExtraFields.ContainsKey("citation-key") ||
                n.Metadata.ExtraFields.ContainsKey("key"))
            .Select(n =>
            {
                var citekey = n.Metadata.ExtraFields.TryGetValue("citekey", out var ck) ? ck
                    : n.Metadata.ExtraFields.TryGetValue("citation-key", out var ck2) ? ck2
                    : n.Metadata.ExtraFields.TryGetValue("key", out var ck3) ? ck3
                    : string.Empty;

                var author = n.Metadata.ExtraFields.TryGetValue("author", out var a) ? a
                    : n.Metadata.ExtraFields.TryGetValue("authors", out var a2) ? a2 : "Unknown";

                var year = n.Metadata.ExtraFields.TryGetValue("year", out var y) ? y
                    : n.Metadata.Date?.Year.ToString() ?? "n.d.";

                var title = n.Metadata.ExtraFields.TryGetValue("title", out var t) ? t : n.Name;

                return (note: n, citekey, author, year, title);
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.citekey))
            .OrderBy(x => x.citekey)
            .ToList();

        if (withCitekey.Count == 0)
        {
            return "[ok] No notes with 'citekey' found in the vault. " +
                   "Add 'citekey: authorYYYY' to your literature note frontmatter.";
        }

        var sb = new StringBuilder();

        if (format.Equals("bib", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"[ok] Exported {withCitekey.Count} BibTeX entries:");
            sb.AppendLine();

            foreach (var (note, citekey, author, year, title) in withCitekey)
            {
                sb.AppendLine($"@misc{{{citekey},");
                sb.AppendLine($"  author = {{{author}}},");
                sb.AppendLine($"  year   = {{{year}}},");
                sb.AppendLine($"  title  = {{{title}}},");
                sb.AppendLine($"  note   = {{Kioku: {note.VaultRelativePath}}}");
                sb.AppendLine("}");
                sb.AppendLine();
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
    // get_literature_gap
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Identifies citekeys referenced inline in notes (as [@citekey] or @citekey) that do not have " +
        "a corresponding literature note in the vault. " +
        "Helps find gaps in your literature review — citations you have referenced but not yet synthesized.")]
    public string get_literature_gap(
        [Description("Folder to scan for literature notes (vault-relative). Leave empty to scan the entire vault.")] string folder = "")
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
            .SelectMany(n => new[]
            {
                n.Metadata.ExtraFields.TryGetValue("citekey", out var ck) ? ck : null,
                n.Metadata.ExtraFields.TryGetValue("citation-key", out var ck2) ? ck2 : null,
                n.Metadata.ExtraFields.TryGetValue("key", out var ck3) ? ck3 : null,
            })
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
    // export_note (HTML)
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Exports a note as HTML (rendered from Markdown using Markdig). " +
        "Returns a self-contained HTML document with inline styles. " +
        "Useful for sharing notes without requiring Obsidian.")]
    public async Task<string> export_note(
        [Description("Name or path of the note to export.")] string note,
        [Description("Output format: only 'html' is supported.")] string format = "html")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (!format.Equals("html", StringComparison.OrdinalIgnoreCase))
        {
            return "[error] Only 'html' format is currently supported. PDF export requires Obsidian to be open.";
        }

        var resolved = ResolveNote(note);
        if (resolved is null)
        {
            return $"[error] Note not found: '{note}'. Use list_notes to see available notes.";
        }

        // Re-read from disk for freshest content
        var rawContent = await File.ReadAllTextAsync(resolved.FilePath);

        // Strip YAML frontmatter before converting
        var bodyContent = StripFrontmatter(rawContent);

        // Convert Markdown to HTML using Markdig (full pipeline)
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        var htmlBody = Markdown.ToHtml(bodyContent, pipeline);

        var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="UTF-8">
              <meta name="viewport" content="width=device-width, initial-scale=1.0">
              <title>{{EscapeHtml(resolved.Name)}}</title>
              <style>
                body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                       max-width: 800px; margin: 40px auto; padding: 0 20px;
                       line-height: 1.6; color: #333; background: #fff; }
                h1, h2, h3 { border-bottom: 1px solid #eee; padding-bottom: .3em; }
                code { background: #f6f8fa; padding: .2em .4em; border-radius: 3px; font-size: 85%; }
                pre { background: #f6f8fa; padding: 16px; border-radius: 6px; overflow: auto; }
                pre code { background: none; padding: 0; font-size: 100%; }
                blockquote { border-left: 4px solid #dfe2e5; padding: 0 1em; color: #6a737d; margin: 0; }
                table { border-collapse: collapse; width: 100%; }
                th, td { border: 1px solid #dfe2e5; padding: 6px 13px; }
                tr:nth-child(even) { background: #f6f8fa; }
                a { color: #0366d6; }
                .meta { color: #6a737d; font-size: 0.85em; margin-bottom: 2em;
                        padding-bottom: 1em; border-bottom: 1px solid #eee; }
              </style>
            </head>
            <body>
              <h1>{{EscapeHtml(resolved.Name)}}</h1>
              <div class="meta">
                <span>Path: {{EscapeHtml(resolved.VaultRelativePath)}}</span>
                {{(resolved.Metadata.Date.HasValue ? $" · <span>Date: {resolved.Metadata.Date:yyyy-MM-dd}</span>" : "")}}
                {{(resolved.Metadata.Tags.Any() ? $" · <span>Tags: {string.Join(", ", resolved.Metadata.Tags)}</span>" : "")}}
              </div>
              {{htmlBody}}
              <hr>
              <footer style="color:#6a737d;font-size:.75em;margin-top:2em">
                Exported by Kioku MCP Server · {{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}} UTC
              </footer>
            </body>
            </html>
            """;

        return $"[ok] Exported '{resolved.Name}' as HTML ({html.Length} chars):\n\n{html}";
    }

    // -------------------------------------------------------------------------
    // share_as_gist
    // -------------------------------------------------------------------------

    [McpServerTool, Description(
        "Publishes a note as a GitHub Gist and returns the URL. " +
        "Requires the KIOKU_GITHUB_TOKEN environment variable to be set with a GitHub personal access token " +
        "that has the 'gist' scope. " +
        "Gists are public by default; set 'public' to false for a secret Gist.")]
    public async Task<string> share_as_gist(
        [Description("Name or path of the note to share.")] string note,
        [Description("Gist description shown on GitHub.")] string description = "",
        [Description("Whether the Gist should be public (default: true).")] bool @public = true)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var token = config.GitHubToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return "[error] KIOKU_GITHUB_TOKEN is not set. " +
                   "Create a GitHub personal access token with 'gist' scope and set it as an environment variable.";
        }

        var resolved = ResolveNote(note);
        if (resolved is null)
        {
            return $"[error] Note not found: '{note}'. Use list_notes to see available notes.";
        }

        var rawContent = await File.ReadAllTextAsync(resolved.FilePath);
        var gistDesc = string.IsNullOrWhiteSpace(description)
            ? $"Kioku export: {resolved.Name}"
            : description;

        var filename = resolved.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? resolved.Name
            : resolved.Name + ".md";

        var payload = new
        {
            description = gistDesc,
            @public,
            files = new Dictionary<string, object>
            {
                [filename] = new { content = rawContent }
            }
        };

        using var http = httpClientFactory.CreateClient("web");
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        http.DefaultRequestHeaders.Add("User-Agent", "Kioku-MCP-Server/1.0");
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync("https://api.github.com/gists", content);
        }
        catch (HttpRequestException ex)
        {
            return $"[error] GitHub API request failed: {ex.Message}";
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            return $"[error] GitHub API returned {(int)response.StatusCode}: {errorBody}";
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(responseJson);

        var htmlUrl = doc.RootElement.GetProperty("html_url").GetString() ?? "(no URL)";
        var gistId = doc.RootElement.GetProperty("id").GetString() ?? "(no ID)";
        var visibility = @public ? "public" : "secret";

        return $"[ok] Note '{resolved.Name}' shared as {visibility} Gist:\n" +
               $"URL: {htmlUrl}\n" +
               $"ID:  {gistId}";
    }

    [McpServerTool, Description(
        "Validates that research and literature notes have required metadata fields (citekey, year, authors, status, updated). " +
        "Returns a report of notes with missing fields for quality assurance.")]
    public string validate_research_notes(
        [Description("Folder to scan for research notes (vault-relative). Leave empty for entire vault.")] string folder = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var allNotes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes().ToList()
            : vault.GetNotesInFolder(folder).ToList();

        var requiredFields = new[] { "citekey", "year", "authors", "status", "updated" };
        var researchNotes = new List<(Note Note, List<string> MissingFields)>();

        foreach (var note in allNotes)
        {
            var noteType = note.Metadata.ExtraFields.TryGetValue("type", out var t) ? t : null;
            var isResearch = noteType is not null &&
                             (noteType.Equals("literature", StringComparison.OrdinalIgnoreCase) ||
                              noteType.Equals("research", StringComparison.OrdinalIgnoreCase));

            if (!isResearch)
            {
                continue;
            }

            var missing = new List<string>();
            foreach (var field in requiredFields)
            {
                if (!note.Metadata.ExtraFields.ContainsKey(field))
                {
                    missing.Add(field);
                }
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

    private Note? ResolveNote(string input) => NoteHelpers.ResolveNote(input, vault);

    private static string StripFrontmatter(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            return content;
        }

        var end = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
        {
            return content;
        }

        var afterFm = content[(end + 4)..];
        return afterFm.TrimStart('\n', '\r');
    }

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    [GeneratedRegex(@"\[@?(?<key>[A-Za-z][A-Za-z0-9_:./-]+)\]|\B@(?<key>[A-Za-z][A-Za-z0-9_:./-]+)", RegexOptions.Compiled)]
    private static partial Regex InlineCitePattern();
}
