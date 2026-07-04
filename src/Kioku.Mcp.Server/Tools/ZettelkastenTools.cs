using System.ComponentModel;
using System.Text;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for Zettelkasten-style note creation and knowledge graph management.
/// Focused on creating structured, interconnected notes that follow
/// the Zettelkasten method for building a personal knowledge base.
/// </summary>
[McpServerToolType]
public sealed class ZettelkastenTools(
    VaultIndexService vault,
    EmbeddingService embedding,
    HybridSearchService hybrid,
    KiokuConfiguration config,
    VaultConfigService vaultConfig)
{
    // create_zettel

    [McpServerTool, Description(
        "Creates a Zettelkasten note with a unique timestamp ID (YYYYMMDDHHMMSS) as the filename. " +
        "Optionally finds semantically related notes and adds wikilinks to them. " +
        "Returns the created note's path and ID.")]
    public async Task<string> create_zettel(
        [Description("Title of the note (used as the H1 heading inside the note).")] string title,
        [Description("Main content of the note in Markdown.")] string content,
        [Description("Tags to add in the frontmatter (comma-separated). E.g. 'idea, philosophy'.")] string tags = "",
        [Description("Folder inside the vault to create the note in. Leave empty to use the configured default, or auto-detect via content similarity if no default is set.")] string folder = "",
        [Description("If true, automatically finds up to 5 semantically related notes and adds [[wikilinks]] to them.")] bool link_related = true,
        [Description("Maximum number of related notes to link (default 5). Only used when link_related=true.")] int max_links = 5)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        // Generate Zettelkasten ID from current timestamp
        var zettelId = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");

        // Resolve target folder: user-provided > config default > auto-detect via content similarity
        var targetFolder = folder;
        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            targetFolder = vaultConfig.GetFolder("zettel") ?? await SuggestFolderForContent(title, content);
        }

        var noteName = $"{targetFolder.TrimEnd('/')}/{zettelId}";
        var filePath = BuildFilePath(noteName);
        if (File.Exists(filePath))
        {
            return $"[error] A note with ID '{zettelId}' already exists. Wait one second and retry.";
        }

        // Find related notes via semantic search (if available and requested)
        var relatedLinks = new List<string>();
        if (link_related && embedding.IsAvailable)
        {
            var searchQuery = $"{title} {content}".Trim();
            var queryVector = await embedding.EmbedAsync(searchQuery);
            var results = hybrid.Search(searchQuery, maxResults: max_links + 2, queryVector: queryVector);
            relatedLinks = results
                .Take(max_links)
                .Select(r => r.Note.Name)
                .ToList();
        }

        var userTags = ParseTags(tags);
        var inherited = vaultConfig.GetInheritedTags(targetFolder);
        var tagList = NoteHelpers.MergeTagsWithInheritance(userTags, inherited, vaultConfig.ExcludeFromTags);
        var body = BuildZettelBody(title, content, relatedLinks);

        // Resolve domain: folder mapping > per-type default
        var domain = vaultConfig.GetDomainForFolder(targetFolder)
                  ?? vaultConfig.GetDefaults("zettel")?.Domain;

        var frontmatter = BuildFrontmatter(tagList, "zettel", "published",
            DateOnly.FromDateTime(DateTime.Today), zettelId, domain: domain);
        var fullContent = frontmatter + "\n" + body;

        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(filePath, fullContent, Encoding.UTF8);

        var relPath = Path.GetRelativePath(config.VaultPath, filePath).Replace('\\', '/');
        var sb = new StringBuilder();
        sb.AppendLine($"[ok] Zettel created: {relPath}");
        sb.AppendLine($"  ID: {zettelId}");
        sb.AppendLine($"  Title: {title}");

        if (relatedLinks.Count > 0)
        {
            sb.AppendLine($"  Linked to: {string.Join(", ", relatedLinks.Select(l => $"[[{l}]]"))}");
        }
        else if (link_related && !embedding.IsAvailable)
        {
            sb.AppendLine("  [info] Semantic linking skipped — Ollama not available.");
        }

        return sb.ToString().TrimEnd();
    }

    // create_moc

    [McpServerTool, Description(
        "Generates a Map of Content (MOC) note for a given vault folder. " +
        "The MOC lists all notes in the folder with their tags and a brief description, " +
        "organized hierarchically by subfolder. Overwrites any existing MOC for the same folder.")]
    public async Task<string> create_moc(
        [Description("Vault-relative folder path to generate the MOC for (e.g. 'Projects', 'Areas/Work').")] string folder,
        [Description("Name of the output MOC note (without extension). Default: '<folder>-MOC'.")] string output_name = "",
        [Description("Folder to save the MOC note. Defaults to the same folder being mapped.")] string output_folder = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            return "[error] 'folder' parameter is required.";
        }

        var notes = vault.GetNotesInFolder(folder)
            .Where(n => !n.Name.EndsWith("-MOC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.VaultRelativePath)
            .ToList();

        if (notes.Count == 0)
        {
            return $"[error] No notes found in folder '{folder}'.";
        }

        var folderTitle = folder.Split('/', '\\').Last();
        var mocName = string.IsNullOrWhiteSpace(output_name)
            ? $"{folderTitle}-MOC"
            : output_name;

        var saveFolder = string.IsNullOrWhiteSpace(output_folder) ? folder : output_folder;
        var fullMocName = $"{saveFolder.TrimEnd('/')}/{mocName}";

        var body = BuildMocBody(folder, notes);
        var domain = vaultConfig.GetDomainForFolder(saveFolder)
                  ?? vaultConfig.GetDefaults("moc")?.Domain;
        var frontmatter = BuildFrontmatter(["moc", "index"], "moc", "published",
            DateOnly.FromDateTime(DateTime.Today), domain: domain);
        var fullContent = frontmatter + "\n" + body;

        var filePath = BuildFilePath(fullMocName);
        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(filePath, fullContent, Encoding.UTF8);

        var relPath = Path.GetRelativePath(config.VaultPath, filePath).Replace('\\', '/');
        return $"[ok] MOC created: {relPath} ({notes.Count} notes indexed)";
    }

    // create_folder_readme

    [McpServerTool, Description(
        "Creates a folder note (named after the folder, e.g. Projects.md inside Projects/) " +
        "listing all its notes. Compatible with the Obsidian Folder Notes plugin. " +
        "Acts as a lightweight, non-Zettelkasten alternative to create_moc. " +
        "Overwrites any existing note with the same name in that folder. " +
        "Only supports folders up to level 2 depth (e.g. 'Projects' or 'Areas/Work').")]
    public async Task<string> create_folder_readme(
        [Description("Vault-relative folder path (max level 2, e.g. 'Projects' or 'Areas/Work').")] string folder)
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
        var notes = vault.GetNotesInFolder(folder)
            .Where(n => !n.Name.Equals(folderTitle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.VaultRelativePath)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# {folderTitle}\n");
        sb.AppendLine($"> Auto-generated index for `{folder}`. Last updated: {DateTime.Today:yyyy-MM-dd}\n");

        if (notes.Count == 0)
        {
            sb.AppendLine("*No notes found in this folder.*");
        }
        else
        {
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
                    sb.AppendLine($"\n### {noteSubfolder}\n");
                }

                var tags = note.Metadata.Tags.Count > 0
                    ? $" _{string.Join(", ", note.Metadata.Tags.Select(t => $"#{t}"))}_"
                    : "";

                sb.AppendLine($"- [[{note.Name}]]{tags}");
            }
        }

        var folderNotePath = NoteHelpers.EnsureInsideVault(
            config.VaultPath,
            Path.Combine(config.VaultPath, folder.TrimEnd('/'), $"{folderTitle}.md"));
        var dir = Path.GetDirectoryName(folderNotePath)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(folderNotePath, sb.ToString(), Encoding.UTF8);

        var relPath = Path.GetRelativePath(config.VaultPath, folderNotePath).Replace('\\', '/');
        return $"[ok] Folder note created: {relPath} ({notes.Count} notes listed)";
    }

    // link_related_notes

    [McpServerTool, Description(
        "Finds notes that are semantically related to a given note and appends wikilinks to them " +
        "in a 'Related' section at the end of the note. " +
        "Requires Ollama to be running (semantic search). " +
        "Does not add links that already exist in the note.")]
    public async Task<string> link_related_notes(
        [Description("Name or path of the note to find related notes for and link.")] string note,
        [Description("Maximum number of related notes to link (default 5).")] int max_links = 5,
        [Description("Minimum similarity score (0.0–1.0). Notes below this threshold are excluded. Default: 0.65.")] double min_similarity = 0.65)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (!embedding.IsAvailable)
        {
            return "[info] Semantic search is unavailable. Ensure Ollama is running to use link_related_notes.";
        }

        var found = ResolveNote(note);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'.";
        }

        // Use FindSimilar which handles vector lookup and note dictionary internally
        var similar = hybrid.FindSimilar(found, max_links + 5, (float)min_similarity)
            .Take(max_links)
            .ToList();

        if (similar.Count == 0)
        {
            return $"[info] No notes found with similarity >= {min_similarity:P0} to '{found.Name}'.";
        }

        // Read current content, check which links already exist
        var currentContent = await File.ReadAllTextAsync(found.FilePath);
        var existingLinks = found.OutgoingLinks.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var updatedContent = NoteHelpers.AppendLinkSection(
            currentContent, existingLinks, "Related",
            similar.Select(r => (r.Note.Name, (string?)$"{r.Score:P0} similar")));

        if (updatedContent is null)
        {
            return $"[info] All {similar.Count} related notes are already linked from '{found.Name}'.";
        }

        await File.WriteAllTextAsync(found.FilePath, updatedContent, Encoding.UTF8);

        var newLinks = similar.Where(r => !existingLinks.Contains(r.Note.Name)).ToList();
        return $"[ok] Added {newLinks.Count} related links to '{found.Name}':\n" +
               string.Join("\n", newLinks.Select(r => $"  - [[{r.Note.Name}]] ({r.Score:P0})"));
    }

    // create_literature_note

    [McpServerTool, Description(
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
        [Description("Folder to save the note in. Default: 'Literature'.")] string folder = "Literature")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var safeTitle = SanitizeFileName(title);
        var noteName = $"{folder.TrimEnd('/')}/{year}-{safeTitle}";
        var filePath = BuildFilePath(noteName);

        if (File.Exists(filePath))
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

        var body = BuildLiteratureNoteBody(title, author, year, source, summary);
        var domain = vaultConfig.GetDomainForFolder(folder)
                  ?? vaultConfig.GetDefaults("literature")?.Domain;
        var frontmatter = BuildFrontmatter(tagList, "literature", "draft",
            DateOnly.FromDateTime(DateTime.Today), domain: domain);
        var fullContent = frontmatter + "\n" + body;

        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(filePath, fullContent, Encoding.UTF8);

        var relPath = Path.GetRelativePath(config.VaultPath, filePath).Replace('\\', '/');
        return $"[ok] Literature note created: {relPath}";
    }

    // Private helpers

    private Note? ResolveNote(string nameOrPath) => NoteHelpers.ResolveNote(nameOrPath, vault);

    private string BuildFilePath(string name) => NoteHelpers.BuildFilePath(name, config.VaultPath);

    private static List<string> ParseTags(string tags) => NoteHelpers.ParseTags(tags);

    private static string BuildFrontmatter(
        IEnumerable<string> tags,
        string type,
        string status,
        DateOnly? date = null,
        string? zettelId = null,
        string? domain = null) =>
        NoteHelpers.BuildFrontmatter(tags, type, status, date, zettelId, domain);

    private static string BuildZettelBody(string title, string content, IReadOnlyList<string> relatedLinks)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {title}\n");
        sb.AppendLine(content.TrimEnd());

        if (relatedLinks.Count > 0)
        {
            sb.AppendLine("\n## Related\n");
            foreach (var link in relatedLinks)
            {
                sb.AppendLine($"- [[{link}]]");
            }
        }

        return sb.ToString();
    }

    private static string BuildMocBody(string folder, IReadOnlyList<Note> notes)
    {
        var folderTitle = folder.Split('/', '\\').Last();
        var sb = new StringBuilder();
        sb.AppendLine($"# {folderTitle} — Map of Content\n");
        sb.AppendLine($"> Auto-generated index for `{folder}`. Last updated: {DateTime.Today:yyyy-MM-dd}\n");

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
                sb.AppendLine($"\n## {sectionTitle}\n");
            }

            var tags = note.Metadata.Tags.Count > 0
                ? $" — _{string.Join(", ", note.Metadata.Tags.Select(t => $"#{t}"))}_"
                : "";

            sb.AppendLine($"- [[{note.Name}]]{tags}");
        }

        return sb.ToString();
    }

    private async Task<string> SuggestFolderForContent(string title, string content)
    {
        var searchQuery = $"{title} {content}".Trim();

        if (embedding.IsAvailable)
        {
            var queryVector = await embedding.EmbedAsync(searchQuery);
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
        sb.AppendLine($"# {title}\n");
        sb.AppendLine("## Metadata\n");
        sb.AppendLine($"- **Author:** {author}");
        sb.AppendLine($"- **Year:** {year}");

        if (!string.IsNullOrWhiteSpace(source))
        {
            sb.AppendLine($"- **Source:** {source}");
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
