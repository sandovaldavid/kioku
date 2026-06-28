using System.ComponentModel;
using System.Text;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP write tools for the Obsidian vault.
/// All operations here modify files on disk.
/// </summary>
[McpServerToolType]
public sealed class NoteCommandTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    VaultConfigService vaultConfig,
    MetricsService? metrics = null)
{
    private static void Count(string name, MetricsService? metrics) => metrics?.RecordToolCall(name);

    // create_note

    [McpServerTool, Description(
        "Creates a new note in the Obsidian vault with frontmatter and content. " +
        "If the note already exists, returns an error — use update_note_content to modify it.")]
    public async Task<string> create_note(
        [Description("Name of the note (without .md extension). Can include subfolders: 'Projects/My Note'.")] string name,
        [Description("Content of the note body (in Markdown).")] string content,
        [Description("Tags to add in the frontmatter (comma-separated). E.g. 'ai, project, draft'.")] string tags = "",
        [Description("Note type for frontmatter (e.g. 'note', 'project', 'area', 'resource').")] string type = "",
        [Description("Status of the note (e.g. 'draft', 'published').")] string status = "draft")
    {
        Count(nameof(create_note), metrics);
        var filePath = BuildFilePath(name);

        if (File.Exists(filePath))
        {
            return KiokuError.InvalidArgument(
                $"Note '{name}' already exists at: {Path.GetRelativePath(config.VaultPath, filePath)}. " +
                "Use update_note_content to modify an existing note.");
        }

        // Ensure directory exists
        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);

        var noteContent = BuildNoteContent(content, tags, type, status, name);
        await File.WriteAllTextAsync(filePath, noteContent, Encoding.UTF8);

        return $"[ok] Note created: {Path.GetRelativePath(config.VaultPath, filePath)}\n" +
               $"Path: {filePath}";
    }

    // update_note_content

    [McpServerTool, Description(
        "Replaces the body of an existing note keeping its YAML frontmatter intact.")]
    public async Task<string> update_note_content(
        [Description("Name or path of the note.")] string note,
        [Description("New content of the body of the note.")] string content)
    {
        Count(nameof(update_note_content), metrics);
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        var rawContent = await File.ReadAllTextAsync(found.FilePath, Encoding.UTF8);
        var bodyStart = FrontmatterParser.GetBodyStart(rawContent);
        var frontmatter = rawContent[..bodyStart];

        var newContent = frontmatter + content;
        await File.WriteAllTextAsync(found.FilePath, newContent, Encoding.UTF8);

        return $"[ok] Content updated in '{found.Name}'";
    }

    // prepend_to_note

    [McpServerTool, Description(
        "Prepends text to the beginning of a note body (just after the YAML frontmatter).")]
    public async Task<string> prepend_to_note(
        [Description("Name or path of the note.")] string note,
        [Description("Text to prepend (in Markdown).")] string content)
    {
        Count(nameof(prepend_to_note), metrics);
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        var rawContent = await File.ReadAllTextAsync(found.FilePath, Encoding.UTF8);
        var bodyStart = FrontmatterParser.GetBodyStart(rawContent);
        var frontmatter = rawContent[..bodyStart];
        var body = rawContent[bodyStart..];

        var newContent = frontmatter + content.Replace("\\n", "\n") + "\n" + body;
        await File.WriteAllTextAsync(found.FilePath, newContent, Encoding.UTF8);

        return $"[ok] Content prepended to the start of '{found.Name}'";
    }

    // append_to_note

    [McpServerTool, Description(
        "Appends text to the end of an existing note. " +
        "Ideal for log notes or journals where the agent records entries.")]
    public async Task<string> append_to_note(
        [Description("Name or path of the note.")] string note,
        [Description("Text to append to the end of the note (in Markdown).")] string content,
        [Description("If true, appends a horizontal separator (---) before the new content.")] bool add_separator = false)
    {
        Count(nameof(append_to_note), metrics);
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        var toAppend = new StringBuilder("\n");
        if (add_separator)
        {
            toAppend.AppendLine("\n---");
        }

        toAppend.AppendLine(content.Replace("\\n", "\n"));

        await File.AppendAllTextAsync(found.FilePath, toAppend.ToString(), Encoding.UTF8);
        return $"[ok] Content appended to '{found.Name}' ({content.Length} characters)";
    }

    // update_frontmatter

    [McpServerTool, Description(
        "Updates or adds fields in the YAML frontmatter of an existing note. " +
        "Only modifies specified fields, the rest remains intact.")]
    public async Task<string> update_frontmatter(
        [Description("Name or path of the note.")] string note,
        [Description("New tags (replaces existing ones, comma-separated). Leave empty to not modify.")] string tags = "",
        [Description("New status (e.g. 'published', 'draft', 'archived'). Leave empty to not modify.")] string status = "",
        [Description("New note type. Leave empty to not modify.")] string type = "")
    {
        Count(nameof(update_frontmatter), metrics);
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        var rawContent = await File.ReadAllTextAsync(found.FilePath, Encoding.UTF8);
        var bodyStart = FrontmatterParser.GetBodyStart(rawContent);
        var body = rawContent[bodyStart..];

        // Rebuild frontmatter with changes
        var existingMeta = found.Metadata;
        var newTags = !string.IsNullOrWhiteSpace(tags)
            ? tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : existingMeta.Tags.ToList();

        var newStatus = !string.IsNullOrWhiteSpace(status) ? status : existingMeta.Status;
        var newType = !string.IsNullOrWhiteSpace(type) ? type : existingMeta.NoteType;
        var newDomain = existingMeta.Domain ?? vaultConfig.GetDomainForFolder(
            Path.GetDirectoryName(found.VaultRelativePath) ?? "");

        var frontmatter = BuildFrontmatter(newTags, newType ?? "", newStatus ?? "",
            existingMeta.Date, domain: newDomain, extraFields: existingMeta.ExtraFields);
        var newContent = frontmatter + body;

        await File.WriteAllTextAsync(found.FilePath, newContent, Encoding.UTF8);
        return $"[ok] Frontmatter updated in '{found.Name}'";
    }

    // add_tag

    [McpServerTool, Description("Adds one or more tags to an existing note.")]
    public async Task<string> add_tag(
        [Description("Name or path of the note.")] string note,
        [Description("Tag(s) to add (comma-separated).")] string tags)
    {
        Count(nameof(add_tag), metrics);
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        var newTags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var existingTags = found.Metadata.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = new List<string>();

        foreach (var tag in newTags)
        {
            if (existingTags.Add(tag))
            {
                added.Add(tag);
            }
        }

        if (added.Count == 0)
        {
            return $"[info] Tags already exist in '{found.Name}': #{string.Join(", #", newTags)}";
        }

        return await update_frontmatter(found.Name, string.Join(", ", existingTags));
    }

    // remove_tag

    [McpServerTool, Description("Removes one or more tags from an existing note.")]
    public async Task<string> remove_tag(
        [Description("Name or path of the note.")] string note,
        [Description("Tag(s) to remove (comma-separated).")] string tags)
    {
        Count(nameof(remove_tag), metrics);
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        var toRemove = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var remaining = found.Metadata.Tags
            .Where(t => !toRemove.Contains(t))
            .ToList();

        return await update_frontmatter(found.Name, string.Join(", ", remaining));
    }

    // move_note

    [McpServerTool, Description(
        "Moves a note to another folder in the vault. " +
        "Note: wikilinks pointing to this note are not updated automatically in v1.")]
    public async Task<string> move_note(
        [Description("Name or path of the note to move.")] string note,
        [Description("Destination folder (relative to the vault). E.g. 'Archive/2024'")] string destination_folder)
    {
        Count(nameof(move_note), metrics);
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        var destDir = NoteHelpers.EnsureInsideVault(
            config.VaultPath,
            Path.Combine(config.VaultPath, destination_folder));
        Directory.CreateDirectory(destDir);

        var destPath = Path.Combine(destDir, Path.GetFileName(found.FilePath));
        if (File.Exists(destPath))
        {
            return KiokuError.InvalidArgument($"A note with that name already exists in '{destination_folder}'");
        }

        var oldPath = found.FilePath;
        File.Move(oldPath, destPath);
        await vault.SynchronizeFileMoveAsync(oldPath, destPath);
        var newRelativePath = Path.GetRelativePath(config.VaultPath, destPath);

        return $"[ok] Note moved:\n   Before: {found.VaultRelativePath}\n   After: {newRelativePath}";
    }

    // rename_note

    [McpServerTool, Description(
        "Renames a note in the vault. The new name can include subfolders.")]
    public async Task<string> rename_note(
        [Description("Name or path of the note to rename.")] string note,
        [Description("New name of the note (without .md extension, e.g. 'New Folder/New Name').")] string new_name)
    {
        Count(nameof(rename_note), metrics);
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        var destPath = BuildFilePath(new_name);
        if (File.Exists(destPath))
        {
            return KiokuError.InvalidArgument($"A note already exists at the destination path: {Path.GetRelativePath(config.VaultPath, destPath)}");
        }

        var destDir = Path.GetDirectoryName(destPath)!;
        Directory.CreateDirectory(destDir);

        var oldPath = found.FilePath;
        File.Move(oldPath, destPath);
        await vault.SynchronizeFileMoveAsync(oldPath, destPath);
        var newRelativePath = Path.GetRelativePath(config.VaultPath, destPath);

        return $"[ok] Note renamed:\n   Before: {found.VaultRelativePath}\n   After: {newRelativePath}";
    }

    // delete_note

    [McpServerTool, Description(
        "Deletes a note from the vault by moving it to .trash folder (recoverable). " +
        "Set permanent=true to delete immediately (irreversible). " +
        "When dry_run is true, only reports what would be deleted without modifying the vault.")]
    public async Task<string> delete_note(
        [Description("Name or path of the note to delete.")] string note,
        [Description("If true, only reports what would be deleted without modifying the vault.")] bool dry_run = false,
        [Description("If true, deletes permanently instead of moving to trash. Default: false (soft delete).")] bool permanent = false)
    {
        Count(nameof(delete_note), metrics);
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        if (dry_run)
        {
            var action = permanent ? "permanently delete" : "move to trash";
            return $"[info] Would {action}: {found.VaultRelativePath}";
        }

        var filePath = found.FilePath;

        if (permanent)
        {
            // Permanent delete
            File.Delete(filePath);
            vault.SynchronizeFileDelete(filePath);
            return $"[ok] Note permanently deleted: {found.VaultRelativePath}";
        }
        else
        {
            // Soft delete: move to .trash
            var trashDir = Path.Combine(config.VaultPath, ".trash");
            if (!Directory.Exists(trashDir))
            {
                Directory.CreateDirectory(trashDir);
            }

            // Generate unique filename in trash to avoid conflicts
            var fileName = Path.GetFileName(filePath);
            var trashPath = Path.Combine(trashDir, fileName);
            var counter = 1;
            while (File.Exists(trashPath))
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);
                trashPath = Path.Combine(trashDir, $"{nameWithoutExt}_{counter}{ext}");
                counter++;
            }

            File.Move(filePath, trashPath);
            vault.SynchronizeFileDelete(filePath);

            var trashRelativePath = Path.GetRelativePath(config.VaultPath, trashPath);
            return $"[ok] Note moved to trash: {found.VaultRelativePath} → {trashRelativePath}\n" +
                   "Use restore_note_from_trash to recover if needed.";
        }
    }

    // Private helpers

    private string BuildNoteContent(string body, string tags, string type, string status, string name)
    {
        // Resolve domain from the note's folder path (if name includes a subfolder)
        var folder = Path.GetDirectoryName(name.Replace('\\', '/')) ?? "";
        var userTags = NoteHelpers.ParseTags(tags);
        var inherited = vaultConfig.GetInheritedTags(folder);
        var tagList = NoteHelpers.MergeTagsWithInheritance(userTags, inherited, vaultConfig.ExcludeFromTags);

        var domain = vaultConfig.GetDomainForFolder(folder)
                  ?? vaultConfig.GetDefaults(type.ToLowerInvariant())?.Domain;

        var frontmatter = NoteHelpers.BuildFrontmatter(tagList, type, status,
            DateOnly.FromDateTime(DateTime.Today), domain: domain);
        return frontmatter + "\n" + body;
    }

    private Note? ResolveNote(string nameOrPath) => NoteHelpers.ResolveNote(nameOrPath, vault);

    private string BuildFilePath(string name) => NoteHelpers.BuildFilePath(name, config.VaultPath);

    private static string BuildFrontmatter(
        IEnumerable<string> tags,
        string? type,
        string? status,
        DateOnly? date = null,
        string? domain = null,
        IReadOnlyDictionary<string, string>? extraFields = null) =>
        NoteHelpers.BuildFrontmatter(tags, type, status, date, domain: domain, extraFields: extraFields);
}
