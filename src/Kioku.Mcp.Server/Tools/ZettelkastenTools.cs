using System.ComponentModel;
using System.Text;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// Internal helper for Zettelkasten-style note creation delegated by NoteCommandTools.
/// </summary>
public sealed class ZettelkastenTools(
    VaultIndexService vault,
    EmbeddingService embedding,
    HybridSearchService hybrid,
    KiokuConfiguration config,
    VaultConfigService vaultConfig,
    ObsidianBridgeService bridge,
    IVaultMutationService? mutations = null)
{
    // create_zettel

    [Description(
        "Creates a Zettelkasten note with a unique timestamp ID (YYYY-MM-DD-HH-mm-ss) as the filename. " +
        "Optionally finds semantically related notes and adds wikilinks to them. " +
        "The title is stored as the note heading and alias; it is not the filename. Returns the " +
        "created note's path, ID, title, and canonical wikilink.")]
    public async Task<string> create_zettel(
        [Description("Title of the note (used as the H1 heading inside the note).")] string title,
        [Description("Main content of the note in Markdown.")] string content,
        [Description("Tags to add in the frontmatter (comma-separated). E.g. 'idea, philosophy'.")] string tags = "",
        [Description("Folder inside the vault to create the note in. Leave empty to use the configured default, or auto-detect via content similarity if no default is set.")] string folder = "",
        [Description("If true, automatically finds up to 5 semantically related notes and adds [[wikilinks]] to them.")] bool link_related = true,
        [Description("Maximum number of related notes to link (default 5). Only used when link_related=true.")] int max_links = 5,
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        // Generate Zettelkasten ID from current timestamp
        var zettelId = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);

        // Resolve target folder: user-provided > config default > auto-detect via content similarity
        var targetFolder = folder;
        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            targetFolder = vaultConfig.GetFolder("zettel") ?? await SuggestFolderForContent(title, content);
        }

        var noteName = $"{targetFolder.TrimEnd('/')}/{zettelId}";
        var filePath = BuildFilePath(noteName);
        var preconditions = VaultMutationPreconditions.FromToolArguments(
            expected_revision,
            expected_hash,
            claim_id,
            fence_generation,
            resource_key,
            mutation_id);
        if (File.Exists(filePath) && string.IsNullOrWhiteSpace(preconditions.MutationId))
        {
            return $"[error] A note with ID '{zettelId}' already exists. Wait one second and retry.";
        }

        // Find related notes via semantic search (if available and requested)
        var relatedLinks = new List<string>();
        if (link_related && embedding.IsAvailable)
        {
            var searchQuery = $"{title} {content}".Trim();
            var queryVector = await embedding.EmbedQueryAsync(searchQuery);
            var results = hybrid.Search(searchQuery, maxResults: max_links + 2, queryVector: queryVector);
            relatedLinks = results
                .Take(max_links)
                .Select(r => r.Note.Name)
                .ToList();
        }

        var userTags = ParseTags(tags);
        var inherited = vaultConfig.GetInheritedTags(targetFolder);
        var tagList = NoteHelpers.MergeTagsWithInheritance(userTags, inherited, vaultConfig.ExcludeFromTags);
        var relatedLinksBody = relatedLinks.Count > 0
            ? string.Join("\n", relatedLinks.Select(l => $"- [[{l}]]"))
            : "";
        var body = await TryRenderFolderTemplateAsync(
            targetFolder,
            new Dictionary<string, string> { ["content"] = content, ["related_links"] = relatedLinksBody },
            title)
            ?? BuildZettelBody(title, content, relatedLinks);

        // Resolve domain: folder mapping > per-type default
        var domain = vaultConfig.GetDomainForFolder(targetFolder)
                  ?? vaultConfig.GetDefaults("zettel")?.Domain;

        var frontmatter = BuildFrontmatter(tagList, "zettel", "published",
            DateOnly.FromDateTime(DateTime.Today), zettelId, domain: domain, aliases: [title],
            updated: vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(DateTime.Today) : null);
        var fullContent = frontmatter + "\n" + body;

        try
        {
            await WriteNoteAsync(
                filePath,
                fullContent,
                requireAbsent: true,
                preconditions);
        }
        catch (VaultMutationException exception)
        {
            return exception.ToToolError();
        }

        var relPath = Path.GetRelativePath(config.VaultPath, filePath).Replace('\\', '/');
        var evalResult = await bridge.EvaluateTemplaterInPlaceAsync(body, relPath);
        await vault.SynchronizeFileReindexAsync(filePath);

        if (vaultConfig.RefreshGeneratedIndexes)
        {
            await RefreshGeneratedIndexesAsync([targetFolder]);
        }

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"[ok] Zettel created: {relPath}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Path: {relPath}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  ID: {zettelId}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Title: {title}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Link: [[{relPath[..^3]}|{title.Replace('|', '-')}]]");

        if (relatedLinks.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Linked to: {string.Join(", ", relatedLinks.Select(l => $"[[{l}]]"))}");
        }
        else if (link_related && !embedding.IsAvailable)
        {
            sb.AppendLine("  [info] Semantic linking skipped — Ollama not available.");
        }

        if (evalResult.Warning is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  [warning] {evalResult.Warning}");
        }

        return sb.ToString().TrimEnd();
    }

    // create_moc

    [Description(
        "Generates a Map of Content (MOC) note for a given vault folder. " +
        "The MOC lists all notes in the folder with their tags and a brief description, " +
        "organized hierarchically by subfolder. Overwrites any existing MOC for the same folder.")]
    public async Task<string> create_moc(
        [Description("Vault-relative folder path to generate the MOC for (e.g. 'Projects', 'Areas/Work').")] string folder,
        [Description("Name of the output MOC note (without extension). Default: '<folder>-MOC'.")] string output_name = "",
        [Description("Folder to save the MOC note. Defaults to the same folder being mapped.")] string output_folder = "",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the output note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            return "[error] 'folder' parameter is required.";
        }

        var folderTitle = folder.Split('/', '\\').Last();
        var mocName = string.IsNullOrWhiteSpace(output_name)
            ? $"{folderTitle}-MOC"
            : output_name;

        var saveFolder = string.IsNullOrWhiteSpace(output_folder) ? folder : output_folder;
        var fullMocName = $"{saveFolder.TrimEnd('/')}/{mocName}";
        var filePath = BuildFilePath(fullMocName);
        var notes = vault.GetNotesInFolder(folder)
            .Where(n => !n.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
            .Where(n => !n.Name.EndsWith("-MOC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.VaultRelativePath)
            .ToList();

        // A user template only replaces the wrapper/heading — the notes list itself is always
        // generated fresh from the folder scan via {{moc_list}}, never replaced by static content.
        var notesList = BuildMocNotesList(folder, notes);
        var managedNotesList = WrapManagedSection(notesList, "moc");
        var body = await TryRenderFolderTemplateAsync(
            saveFolder,
            new Dictionary<string, string> { ["folder"] = folder, ["moc_list"] = managedNotesList },
            mocName)
            ?? BuildMocBody(folder, managedNotesList);
        var domain = vaultConfig.GetDomainForFolder(saveFolder)
                  ?? vaultConfig.GetDefaults("moc")?.Domain;
        var frontmatter = BuildFrontmatter(["moc", "index"], "moc", "published",
            DateOnly.FromDateTime(DateTime.Today), domain: domain,
            extraFields: new Dictionary<string, string>
            {
                ["kioku_generated"] = "\"moc\"",
                ["kioku_source_folder"] = $"\"{folder}\"",
            },
            updated: vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(DateTime.Today) : null);
        var fullContent = frontmatter + "\n" + body;

        try
        {
            await WriteNoteAsync(
                filePath,
                fullContent,
                requireAbsent: false,
                VaultMutationPreconditions.FromToolArguments(
                    expected_revision,
                    expected_hash,
                    claim_id,
                    fence_generation,
                    resource_key,
                    mutation_id));
        }
        catch (VaultMutationException exception)
        {
            return exception.ToToolError();
        }

        var relPath = Path.GetRelativePath(config.VaultPath, filePath).Replace('\\', '/');
        var evalResult = await bridge.EvaluateTemplaterInPlaceAsync(body, relPath);
        await vault.SynchronizeFileReindexAsync(filePath);

        var result = $"[ok] MOC created: {relPath} ({notes.Count} notes indexed)";
        return evalResult.Warning is null ? result : $"{result}\n  [warning] {evalResult.Warning}";
    }

    // create_folder_readme

    [Description(
        "Creates a folder note (named after the folder, e.g. Projects.md inside Projects/) " +
        "listing all its notes. Compatible with the Obsidian Folder Notes plugin. " +
        "Acts as a lightweight, non-Zettelkasten alternative to create_moc. " +
        "Overwrites any existing note with the same name in that folder. " +
        "Only supports folders up to level 2 depth (e.g. 'Projects' or 'Areas/Work').")]
    public async Task<string> create_folder_readme(
        [Description("Vault-relative folder path (max level 2, e.g. 'Projects' or 'Areas/Work').")] string folder,
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the output note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            return "[error] 'folder' parameter is required.";
        }

        if (folder.Count(c => c is '/' or '\\') >= 2)
        {
            return "[error] 'folder' must be at most level 2 deep (e.g. 'Projects' or 'Projects/Active').";
        }

        var folderTitle = folder.Split('/', '\\').Last();
        var folderNotePath = NoteHelpers.EnsureInsideVault(
            config.VaultPath,
            Path.Combine(config.VaultPath, folder.TrimEnd('/'), $"{folderTitle}.md"));
        var notes = vault.GetNotesInFolder(folder)
            .Where(n => !n.FilePath.Equals(folderNotePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.VaultRelativePath)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# {folderTitle}\n");
        sb.AppendLine(CultureInfo.InvariantCulture, $"> Auto-generated index for `{folder}`. Last updated: {DateTime.Today:yyyy-MM-dd}\n");
        sb.Append(WrapManagedSection(BuildFolderReadmeNotesList(folder, notes), "folder-readme"));

        var frontmatter = NoteHelpers.BuildFrontmatter(
            ["index"], "folder-readme", "published", DateOnly.FromDateTime(DateTime.Today),
            domain: vaultConfig.GetDomainForFolder(folder),
            extraFields: new Dictionary<string, string>
            {
                ["kioku_generated"] = "\"folder-readme\"",
                ["kioku_source_folder"] = $"\"{folder}\"",
            },
            updated: vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(DateTime.Today) : null);
        try
        {
            await WriteNoteAsync(
                folderNotePath,
                frontmatter + "\n" + sb,
                requireAbsent: false,
                VaultMutationPreconditions.FromToolArguments(
                    expected_revision,
                    expected_hash,
                    claim_id,
                    fence_generation,
                    resource_key,
                    mutation_id));
        }
        catch (VaultMutationException exception)
        {
            return exception.ToToolError();
        }
        await vault.SynchronizeFileReindexAsync(folderNotePath);

        var relPath = Path.GetRelativePath(config.VaultPath, folderNotePath).Replace('\\', '/');
        return $"[ok] Folder note created: {relPath} ({notes.Count} notes listed)";
    }

    // create_literature_note

    [Description(
        "Creates a structured literature note for a book, article, or paper " +
        "using the standard Zettelkasten literature note template. " +
        "The note includes fields for author, year, title, source, and a summary section.")]
    public async Task<string> create_literature_note(
        [Description("Title of the work (book, article, paper, etc.).")] string title,
        [Description("Author(s) of the work.")] string author,
        [Description("Publication year (e.g. '2023').")] string year,
        [Description("Source or URL (e.g. 'https://...', 'ISBN 978-...').")] string source = "",
        [Description("Brief summary or key insight from the work.")] string summary = "",
        [Description("Tags to add in frontmatter (comma-separated). 'literature' is always included.")] string tags = "",
        [Description("Folder to save the note in. Default: 'Literature'.")] string folder = "Literature",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var safeTitle = SanitizeFileName(title);
        var noteName = $"{folder.TrimEnd('/')}/{year}-{safeTitle}";
        var filePath = BuildFilePath(noteName);

        var preconditions = VaultMutationPreconditions.FromToolArguments(
            expected_revision,
            expected_hash,
            claim_id,
            fence_generation,
            resource_key,
            mutation_id);
        if (File.Exists(filePath) && string.IsNullOrWhiteSpace(preconditions.MutationId))
        {
            var relExisting = Path.GetRelativePath(config.VaultPath, filePath).Replace('\\', '/');
            return $"[error] Literature note already exists: {relExisting}";
        }

        var userTags = ParseTags(tags);
        var inherited = vaultConfig.GetInheritedTags(folder);
        var tagList = NoteHelpers.MergeTagsWithInheritance(userTags, inherited, vaultConfig.ExcludeFromTags);
        if (!tagList.Contains("literature", StringComparer.OrdinalIgnoreCase))
        {
            tagList.Insert(0, "literature");
        }

        var body = await TryRenderFolderTemplateAsync(
            folder,
            new Dictionary<string, string>
            {
                ["author"] = author,
                ["year"] = year,
                ["source"] = source,
                ["summary"] = summary,
            },
            title)
            ?? BuildLiteratureNoteBody(title, author, year, source, summary);
        var domain = vaultConfig.GetDomainForFolder(folder)
                  ?? vaultConfig.GetDefaults("literature")?.Domain;
        var frontmatter = BuildFrontmatter(tagList, "literature", "draft",
            DateOnly.FromDateTime(DateTime.Today), domain: domain, aliases: [title],
            updated: vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(DateTime.Today) : null);
        var fullContent = frontmatter + "\n" + body;

        try
        {
            await WriteNoteAsync(
                filePath,
                fullContent,
                requireAbsent: true,
                preconditions);
        }
        catch (VaultMutationException exception)
        {
            return exception.ToToolError();
        }

        var relPath = Path.GetRelativePath(config.VaultPath, filePath).Replace('\\', '/');
        var evalResult = await bridge.EvaluateTemplaterInPlaceAsync(body, relPath);
        await vault.SynchronizeFileReindexAsync(filePath);

        if (vaultConfig.RefreshGeneratedIndexes)
        {
            await RefreshGeneratedIndexesAsync([folder]);
        }

        var result = $"[ok] Literature note created: {relPath}";
        return evalResult.Warning is null ? result : $"{result}\n  [warning] {evalResult.Warning}";
    }

    // Private helpers

    private string BuildFilePath(string name) => NoteHelpers.BuildFilePath(name, config.VaultPath);

    private static List<string> ParseTags(string tags) => NoteHelpers.ParseTags(tags);

    private static string BuildFrontmatter(
        IEnumerable<string> tags,
        string type,
        string status,
        DateOnly? date = null,
        string? zettelId = null,
        string? domain = null,
        IReadOnlyDictionary<string, string>? extraFields = null,
        IEnumerable<string>? aliases = null,
        DateOnly? updated = null) =>
        NoteHelpers.BuildFrontmatter(tags, type, status, date, zettelId, domain,
            aliases: aliases, extraFields: extraFields, updated: updated);

    private static string BuildZettelBody(string title, string content, List<string> relatedLinks)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# {title}\n");
        sb.AppendLine(content.TrimEnd());

        if (relatedLinks.Count > 0)
        {
            sb.AppendLine("\n## Related\n");
            foreach (var link in relatedLinks)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- [[{link}]]");
            }
        }

        return sb.ToString();
    }

    private static string BuildMocBody(string folder, string notesList)
    {
        var folderTitle = folder.Split('/', '\\').Last();
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# {folderTitle} — Map of Content\n");
        sb.AppendLine(CultureInfo.InvariantCulture, $"> Auto-generated index for `{folder}`. Last updated: {DateTime.Today:yyyy-MM-dd}\n");
        sb.Append(notesList);
        return sb.ToString();
    }

    /// <summary>Just the grouped notes list (no heading/wrapper) — reused as {{moc_list}} by a user template.</summary>
    private static string BuildMocNotesList(string folder, List<Note> notes)
    {
        var sb = new StringBuilder();
        string? lastSubdir = null;

        foreach (var note in notes)
        {
            var relToFolder = Path.GetRelativePath(folder, note.VaultRelativePath.Replace('\\', '/'))
                .Replace('\\', '/');
            var subdir = Path.GetDirectoryName(relToFolder)?.Replace('\\', '/');

            if (subdir != lastSubdir)
            {
                lastSubdir = subdir;
                var sectionTitle = string.IsNullOrEmpty(subdir) ? "Notes" : subdir;
                sb.AppendLine(CultureInfo.InvariantCulture, $"\n## {sectionTitle}\n");
            }

            var tags = note.Metadata.Tags.Count > 0
                ? $" — _{string.Join(", ", note.Metadata.Tags.Select(t => $"#{t}"))}_"
                : "";

            sb.AppendLine(CultureInfo.InvariantCulture, $"- [[{note.Name}]]{tags}");
        }

        return sb.ToString();
    }

    private static string BuildFolderReadmeNotesList(string folder, List<Note> notes)
    {
        var sb = new StringBuilder();
        if (notes.Count == 0)
        {
            sb.AppendLine("*No notes found in this folder.*");
            return sb.ToString();
        }

        sb.AppendLine("## Notes\n");
        string? currentSubfolder = null;
        foreach (var note in notes)
        {
            var relToFolder = Path.GetRelativePath(folder, note.VaultRelativePath.Replace('\\', '/'))
                .Replace('\\', '/');
            var noteSubfolder = Path.GetDirectoryName(relToFolder)?.Replace('\\', '/');
            if (noteSubfolder != currentSubfolder && !string.IsNullOrEmpty(noteSubfolder))
            {
                currentSubfolder = noteSubfolder;
                sb.AppendLine(CultureInfo.InvariantCulture, $"\n### {noteSubfolder}\n");
            }

            var tags = note.Metadata.Tags.Count > 0
                ? $" _{string.Join(", ", note.Metadata.Tags.Select(t => $"#{t}"))}_"
                : "";
            sb.AppendLine(CultureInfo.InvariantCulture, $"- [[{note.Name}]]{tags}");
        }

        return sb.ToString();
    }

    private static string WrapManagedSection(string content, string kind) =>
        $"<!-- kioku:{kind}:start -->\n{content.TrimEnd()}\n<!-- kioku:{kind}:end -->\n";

    private static string? ReplaceManagedSection(string raw, string kind, string content)
    {
        var start = $"<!-- kioku:{kind}:start -->";
        var end = $"<!-- kioku:{kind}:end -->";
        var startIndex = raw.IndexOf(start, StringComparison.Ordinal);
        var endIndex = raw.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        if (startIndex < 0 || endIndex < 0)
        {
            return null;
        }

        var replacement = WrapManagedSection(content, kind).TrimEnd();
        return raw[..startIndex] + replacement + raw[(endIndex + end.Length)..];
    }

    /// <summary>Refreshes Kioku-owned generated indexes covering the supplied source folders.</summary>
    public async Task RefreshGeneratedIndexesAsync(IEnumerable<string> affectedFolders)
    {
        if (!vaultConfig.RefreshGeneratedIndexes)
        {
            return;
        }

        var affected = affectedFolders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(NormalizeFolder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var generated = vault.GetAllNotes()
            .Where(n => n.Metadata.ExtraFields.TryGetValue("kioku_generated", out var kind) &&
                        (kind.Equals("moc", StringComparison.OrdinalIgnoreCase) ||
                         kind.Equals("folder-readme", StringComparison.OrdinalIgnoreCase)) &&
                        n.Metadata.ExtraFields.ContainsKey("kioku_source_folder"))
            .ToList();

        foreach (var index in generated)
        {
            var sourceFolder = NormalizeFolder(index.Metadata.ExtraFields["kioku_source_folder"]);
            if (!affected.Any(folder => IsSameOrDescendant(folder, sourceFolder) || IsSameOrDescendant(sourceFolder, folder)))
            {
                continue;
            }

            var sourceNotes = vault.GetNotesInFolder(sourceFolder)
                .Where(n => !n.FilePath.Equals(index.FilePath, StringComparison.OrdinalIgnoreCase))
                .Where(n => !n.Name.EndsWith("-MOC", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n.VaultRelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var kind = index.Metadata.ExtraFields["kioku_generated"];
            var generatedList = kind.Equals("moc", StringComparison.OrdinalIgnoreCase)
                ? BuildMocNotesList(sourceFolder, sourceNotes)
                : BuildFolderReadmeNotesList(sourceFolder, sourceNotes);
            var updatedContent = ReplaceManagedSection(index.RawContent, kind, generatedList);
            if (updatedContent is null)
            {
                continue;
            }

            updatedContent = NoteHelpers.TouchUpdated(
                updatedContent, DateOnly.FromDateTime(DateTime.Today), vaultConfig.MaintainUpdated);
            await WriteNoteAsync(index.FilePath, updatedContent, requireAbsent: false, preconditions: null);
        }
    }

    private static string NormalizeFolder(string folder) =>
        folder.Trim().Trim('/').Replace('\\', '/');

    private async Task WriteNoteAsync(
        string path,
        string content,
        bool requireAbsent,
        VaultMutationPreconditions? preconditions)
    {
        if (mutations is null)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, content, NoteHelpers.Utf8NoBom);
            await vault.SynchronizeFileReindexAsync(path);
            return;
        }

        if (requireAbsent)
        {
            await mutations.CreateTextAsync(path, content, preconditions);
        }
        else
        {
            await mutations.WriteTextAsync(path, content, preconditions);
        }
    }

    private static bool IsSameOrDescendant(string path, string parent) =>
        path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a per-folder template (Kioku's own template_folders override, or Templater's own
    /// Folder Templates settings) for <paramref name="targetFolder"/>, reads and renders it with
    /// {{var}} substitution. Returns null when no template is configured for this folder or the
    /// configured file doesn't exist — callers should fall back to their own hardcoded body.
    /// </summary>
    private async Task<string?> TryRenderFolderTemplateAsync(
        string targetFolder, IReadOnlyDictionary<string, string> variables, string? noteTitle)
    {
        var resolvedPath = await vaultConfig.ResolveFolderTemplateAsync(targetFolder);
        if (resolvedPath is null)
        {
            return null;
        }

        var fullPath = NoteHelpers.EnsureInsideVault(config.VaultPath, Path.Combine(config.VaultPath, resolvedPath));
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var raw = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);
        return NoteHelpers.ExpandTemplateVariables(raw, variables, noteTitle);
    }

    private async Task<string> SuggestFolderForContent(string title, string content)
    {
        var searchQuery = $"{title} {content}".Trim();

        if (embedding.IsAvailable)
        {
            var queryVector = await embedding.EmbedQueryAsync(searchQuery);
            if (queryVector is not null)
            {
                var results = hybrid.Search(searchQuery, maxResults: 20, queryVector: queryVector);
                var folderScores = new Dictionary<string, (double Score, int Count)>(StringComparer.OrdinalIgnoreCase);

                foreach (var result in results)
                {
                    var f = Path.GetDirectoryName(result.Note.VaultRelativePath) ?? "";
                    if (string.IsNullOrEmpty(f))
                    {
                        continue;
                    }

                    var (score, count) = folderScores.GetValueOrDefault(f);
                    folderScores[f] = (score + result.Score, count + 1);
                }

                if (folderScores.Count > 0)
                {
                    return folderScores
                        .OrderByDescending(kv => kv.Value.Score / kv.Value.Count)
                        .First().Key;
                }
            }
        }

        // Fallback: keyword-only search, group by folder
        var keywordResults = vault.Search(searchQuery, 20);
        var kwFolderScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in keywordResults)
        {
            var f = Path.GetDirectoryName(result.Note.VaultRelativePath) ?? "";
            if (string.IsNullOrEmpty(f))
            {
                continue;
            }

            kwFolderScores[f] = kwFolderScores.GetValueOrDefault(f) + 1;
        }

        if (kwFolderScores.Count > 0)
        {
            return kwFolderScores.OrderByDescending(kv => kv.Value).First().Key;
        }

        return "Inbox";
    }

    private static string BuildLiteratureNoteBody(
        string title,
        string author,
        string year,
        string source,
        string summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# {title}\n");
        sb.AppendLine("## Metadata\n");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Author:** {author}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Year:** {year}");

        if (!string.IsNullOrWhiteSpace(source))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- **Source:** {source}");
        }

        sb.AppendLine("\n## Summary\n");
        sb.AppendLine(!string.IsNullOrWhiteSpace(summary) ? summary : "*Add your summary here.*");
        sb.AppendLine("\n## Key Ideas\n");
        sb.AppendLine("- ");
        sb.AppendLine("\n## Quotes\n");
        sb.AppendLine("> ");
        sb.AppendLine("\n## My Notes\n");
        sb.AppendLine("*Add your personal reflections here.*");

        return sb.ToString();
    }

    private static string SanitizeFileName(string name) => NoteHelpers.SanitizeFileName(name);
}
