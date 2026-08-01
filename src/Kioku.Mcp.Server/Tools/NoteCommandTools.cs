using System.Collections.Concurrent;
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
public sealed partial class NoteCommandTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    VaultConfigService vaultConfig,
    ZettelkastenTools? zettelkasten = null,
    MetricsService? metrics = null,
    VaultPathPolicy? pathPolicy = null,
    IVaultMutationService? mutations = null)
{
    private static void Count(string name, MetricsService? metrics) => metrics?.RecordToolCall(name);

    private static readonly UTF8Encoding Utf8NoBom = NoteHelpers.Utf8NoBom;
    private readonly VaultPathPolicy _paths = pathPolicy ?? new VaultPathPolicy(config);
    private readonly IVaultMutationService? _mutations = mutations;

    // Serializes trash destination-name allocation per vault. The soft-delete path computes a
    // unique name in .trash and then moves the file; without a lock two concurrent delete_note
    // calls for notes sharing a basename can both pick the same name and the second File.Move
    // (rename on Unix) silently overwrites the first, losing a recoverable note.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> TrashLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private static async Task<IDisposable> AcquireTrashLockAsync(string vaultPath)
    {
        var semaphore = TrashLocks.GetOrAdd(vaultPath, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        return new SemaphoreReleaser(semaphore);
    }

    private sealed class SemaphoreReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }

    // create_note

    [McpServerTool, Description(
        "Creates a note in the vault. kind='note' (default) creates a regular note; " +
        "'zettel', 'literature', 'moc', and 'folder-readme' preserve the corresponding " +
        "structured creation conventions. Use template with kind='note' to render a vault " +
        "template while keeping generated frontmatter.")]
    public async Task<string> create_note(
        [Description("Note name or vault-relative path. For zettel/literature this is the title.")] string name = "",
        [Description("Markdown body. Required for kind='note' and kind='zettel'.")] string content = "",
        [Description("'note' (default), 'zettel', 'literature', 'moc', or 'folder-readme'.")] string kind = "note",
        [Description("Comma-separated tags for note, zettel, or literature kinds.")] string tags = "",
        [Description("Frontmatter type for a regular note. Empty uses configured note defaults.")] string type = "",
        [Description("Frontmatter status for a regular note. Empty uses configured note defaults.")] string status = "",
        [Description("Target folder for structured kinds, or an optional folder for a regular note name.")] string folder = "",
        [Description("Vault-relative template path, used for kind='note'.")] string template = "",
        [Description("Literature author(s), required for kind='literature'.")] string author = "",
        [Description("Literature publication year, required for kind='literature'.")] string year = "",
        [Description("Literature source or URL.")] string source = "",
        [Description("Literature summary.")] string summary = "",
        [Description("For kind='zettel', automatically add related wikilinks.")] bool link_related = true,
        [Description("For kind='zettel', maximum related notes to link.")] int max_links = 5,
        [Description("For kind='moc', optional output filename without extension.")] string output_name = "",
        [Description("For kind='moc', optional output folder.")] string output_folder = "",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the target path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "")
    {
        Count(nameof(create_note), metrics);
        var preconditions = CreatePreconditions(
            expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id);

        switch (kind.Trim().ToLowerInvariant())
        {
            case "note":
                return await CreateRegularNoteAsync(
                    name, content, tags, type, status, folder, template, preconditions);

            case "zettel":
                if (zettelkasten is null)
                {
                    return "[error] Zettelkasten creation is unavailable.";
                }

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(content))
                {
                    return KiokuError.InvalidArgument("kind='zettel' requires name (title) and content.");
                }

                if (max_links < 0)
                {
                    return KiokuError.InvalidArgument("'max_links' must be 0 or greater.");
                }

                return await zettelkasten.create_zettel(
                    name, content, tags, folder, link_related, max_links,
                    preconditions.ExpectedRevision ?? string.Empty,
                    preconditions.ExpectedHash ?? string.Empty,
                    preconditions.ClaimId ?? string.Empty,
                    preconditions.FenceGeneration ?? 0,
                    preconditions.ResourceKey ?? string.Empty,
                    preconditions.MutationId ?? string.Empty);

            case "literature":
                if (zettelkasten is null)
                {
                    return "[error] Literature creation is unavailable.";
                }

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(year))
                {
                    return KiokuError.InvalidArgument("kind='literature' requires name, author, and year.");
                }

                return await zettelkasten.create_literature_note(
                    name, author, year, source, summary, tags,
                    string.IsNullOrWhiteSpace(folder) ? "Literature" : folder,
                    preconditions.ExpectedRevision ?? string.Empty,
                    preconditions.ExpectedHash ?? string.Empty,
                    preconditions.ClaimId ?? string.Empty,
                    preconditions.FenceGeneration ?? 0,
                    preconditions.ResourceKey ?? string.Empty,
                    preconditions.MutationId ?? string.Empty);

            case "moc":
                if (zettelkasten is null)
                {
                    return "[error] MOC creation is unavailable.";
                }

                if (string.IsNullOrWhiteSpace(folder))
                {
                    return KiokuError.InvalidArgument("kind='moc' requires folder.");
                }

                return await zettelkasten.create_moc(
                    folder,
                    string.IsNullOrWhiteSpace(output_name) ? name : output_name,
                    output_folder,
                    preconditions.ExpectedRevision ?? string.Empty,
                    preconditions.ExpectedHash ?? string.Empty,
                    preconditions.ClaimId ?? string.Empty,
                    preconditions.FenceGeneration ?? 0,
                    preconditions.ResourceKey ?? string.Empty,
                    preconditions.MutationId ?? string.Empty);

            case "folder-readme":
                if (zettelkasten is null)
                {
                    return "[error] Folder README creation is unavailable.";
                }

                if (string.IsNullOrWhiteSpace(folder))
                {
                    return KiokuError.InvalidArgument("kind='folder-readme' requires folder.");
                }

                return await zettelkasten.create_folder_readme(
                    folder,
                    preconditions.ExpectedRevision ?? string.Empty,
                    preconditions.ExpectedHash ?? string.Empty,
                    preconditions.ClaimId ?? string.Empty,
                    preconditions.FenceGeneration ?? 0,
                    preconditions.ResourceKey ?? string.Empty,
                    preconditions.MutationId ?? string.Empty);

            default:
                return KiokuError.InvalidArgument(
                    $"Unknown kind '{kind}'. Use 'note', 'zettel', 'literature', 'moc', or 'folder-readme'.");
        }
    }

    private async Task<string> CreateRegularNoteAsync(
        string name,
        string content,
        string tags,
        string type,
        string status,
        string folder,
        string template,
        VaultMutationPreconditions preconditions)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return KiokuError.InvalidArgument("The 'name' parameter cannot be empty.");
        }

        var noteName = string.IsNullOrWhiteSpace(folder)
            ? name
            : $"{folder.TrimEnd('/', '\\')}/{name.TrimStart('/', '\\')}";
        var filePath = BuildFilePath(noteName);
        if (File.Exists(filePath) && string.IsNullOrWhiteSpace(preconditions.MutationId))
        {
            return KiokuError.InvalidArgument($"Note already exists: '{noteName}'. Use edit_note to modify it.");
        }

        var targetFolder = Path.GetDirectoryName(noteName)?.Replace('\\', '/') ?? string.Empty;
        var userTags = NoteHelpers.ParseTags(tags);
        var inherited = vaultConfig.GetInheritedTags(targetFolder);
        var tagList = NoteHelpers.MergeTagsWithInheritance(userTags, inherited, vaultConfig.ExcludeFromTags);
        var noteType = string.IsNullOrWhiteSpace(type) ? vaultConfig.GetDefaults("note")?.Type : type;
        // Per-type config defaults win over the generic 'note' entry; 'draft' keeps the
        // historical status fallback for vaults with no defaults configured.
        var defaultsKey = string.IsNullOrWhiteSpace(noteType) ? "note" : noteType.ToLowerInvariant();
        var noteStatus = string.IsNullOrWhiteSpace(status)
            ? vaultConfig.GetDefaults(defaultsKey)?.Status ?? vaultConfig.GetDefaults("note")?.Status ?? "draft"
            : status;
        var domain = vaultConfig.GetDomainForFolder(targetFolder)
                   ?? vaultConfig.GetDefaults(defaultsKey)?.Domain
                   ?? vaultConfig.GetDefaults("note")?.Domain;

        var body = content;
        if (!string.IsNullOrWhiteSpace(template))
        {
            var templatePath = NoteHelpers.EnsureInsideVault(
                config.VaultPath, Path.Combine(config.VaultPath, template));
            if (!File.Exists(templatePath))
            {
                return KiokuError.NotFound($"Template not found: '{template}'");
            }

            var rawTemplate = await File.ReadAllTextAsync(templatePath, Encoding.UTF8);
            body = NoteHelpers.ExpandTemplateVariables(
                rawTemplate,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["content"] = content,
                    ["title"] = Path.GetFileNameWithoutExtension(noteName),
                },
                Path.GetFileNameWithoutExtension(noteName));
        }

        var frontmatter = NoteHelpers.BuildFrontmatter(
            tagList, noteType, noteStatus, DateOnly.FromDateTime(DateTime.Today), domain: domain,
            updated: vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(DateTime.Today) : null);
        var directory = Path.GetDirectoryName(filePath)!;
        try
        {
            if (_mutations is not null)
            {
                await _mutations.CreateTextAsync(
                    filePath, frontmatter + "\n" + body, preconditions);
            }
            else
            {
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(filePath, frontmatter + "\n" + body, Utf8NoBom);
                await vault.SynchronizeFileReindexAsync(filePath);
            }
        }
        catch (VaultMutationException exception)
        {
            return exception.ToToolError();
        }

        await RefreshGeneratedIndexesAsync(targetFolder);

        var relativePath = Path.GetRelativePath(config.VaultPath, filePath).Replace('\\', '/');
        return $"[ok] Note created: {relativePath}";
    }

    // edit_note

    [McpServerTool, Description(
        "Edits the body of an existing note, keeping its YAML frontmatter intact. " +
        "mode='replace' (default) replaces the whole body, 'append' adds at the end, " +
        "'prepend' inserts just after the frontmatter.")]
    public async Task<string> edit_note(
        [Description("Name or path of the note.")] string note,
        [Description("The content to write (in Markdown).")] string content,
        [Description("'replace' (default), 'append', or 'prepend'.")] string mode = "replace",
        [Description("Append mode only: adds a horizontal separator (---) before the new content.")] bool add_separator = false,
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "")
    {
        Count(nameof(edit_note), metrics);
        var preconditions = CreatePreconditions(
            expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id);
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        switch (mode.ToLowerInvariant())
        {
            case "replace":
                {
                    var rawContent = await File.ReadAllTextAsync(found.FilePath, Encoding.UTF8);
                    var bodyStart = FrontmatterParser.GetBodyStart(rawContent);
                    var frontmatter = rawContent[..bodyStart];
                    var updatedContent = NoteHelpers.TouchUpdated(
                        frontmatter + content, DateOnly.FromDateTime(DateTime.Today), vaultConfig.MaintainUpdated);
                    try
                    {
                        await WriteNoteTextAsync(found.FilePath, updatedContent, preconditions);
                    }
                    catch (VaultMutationException exception)
                    {
                        return exception.ToToolError();
                    }
                    await RefreshGeneratedIndexesAsync(Path.GetDirectoryName(found.VaultRelativePath));
                    return $"[ok] Content updated in '{found.Name}'";
                }

            case "prepend":
                {
                    var rawContent = await File.ReadAllTextAsync(found.FilePath, Encoding.UTF8);
                    var bodyStart = FrontmatterParser.GetBodyStart(rawContent);
                    var frontmatter = rawContent[..bodyStart];
                    var body = rawContent[bodyStart..];
                    var newContent = NoteHelpers.TouchUpdated(
                        frontmatter + content.Replace("\\n", "\n") + "\n" + body,
                        DateOnly.FromDateTime(DateTime.Today), vaultConfig.MaintainUpdated);
                    try
                    {
                        await WriteNoteTextAsync(found.FilePath, newContent, preconditions);
                    }
                    catch (VaultMutationException exception)
                    {
                        return exception.ToToolError();
                    }
                    await RefreshGeneratedIndexesAsync(Path.GetDirectoryName(found.VaultRelativePath));
                    return $"[ok] Content prepended to the start of '{found.Name}'";
                }

            case "append":
                {
                    var toAppend = new StringBuilder("\n");
                    if (add_separator)
                    {
                        toAppend.AppendLine("\n---");
                    }

                    toAppend.AppendLine(content.Replace("\\n", "\n"));
                    var rawContent = await File.ReadAllTextAsync(found.FilePath, Encoding.UTF8);
                    var updatedContent = NoteHelpers.TouchUpdated(
                        rawContent + toAppend.ToString(), DateOnly.FromDateTime(DateTime.Today), vaultConfig.MaintainUpdated);
                    try
                    {
                        await WriteNoteTextAsync(found.FilePath, updatedContent, preconditions);
                    }
                    catch (VaultMutationException exception)
                    {
                        return exception.ToToolError();
                    }
                    await RefreshGeneratedIndexesAsync(Path.GetDirectoryName(found.VaultRelativePath));
                    return $"[ok] Content appended to '{found.Name}' ({content.Length} characters)";
                }

            default:
                return KiokuError.InvalidArgument($"Unknown mode '{mode}'. Use 'replace', 'append', or 'prepend'.");
        }
    }

    // update_frontmatter

    [McpServerTool, Description(
        "Updates or adds fields in the YAML frontmatter of an existing note. " +
        "Only modifies specified fields, the rest remains intact. " +
        "Use add_tags/remove_tags to change tags incrementally, or tags to replace them all.")]
    public async Task<string> update_frontmatter(
        [Description("Name or path of the note.")] string note,
        [Description("New tags (replaces existing ones, comma-separated). Leave empty to not modify.")] string tags = "",
        [Description("New status (e.g. 'published', 'draft', 'archived'). Leave empty to not modify.")] string status = "",
        [Description("New note type. Leave empty to not modify.")] string type = "",
        [Description("If true, removes all tags regardless of the 'tags' argument.")] bool clear_tags = false,
        [Description("Tag(s) to add to the existing set (comma-separated).")] string add_tags = "",
        [Description("Tag(s) to remove from the existing set (comma-separated).")] string remove_tags = "",
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "")
    {
        Count(nameof(update_frontmatter), metrics);
        var preconditions = CreatePreconditions(
            expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id);
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
        var newTags = clear_tags
            ? []
            : !string.IsNullOrWhiteSpace(tags)
                ? tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : existingMeta.Tags.ToList();

        if (!clear_tags && !string.IsNullOrWhiteSpace(add_tags))
        {
            var seen = newTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in add_tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (seen.Add(tag))
                {
                    newTags.Add(tag);
                }
            }
        }

        if (!clear_tags && !string.IsNullOrWhiteSpace(remove_tags))
        {
            var toRemove = remove_tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            newTags.RemoveAll(toRemove.Contains);
        }

        var newStatus = !string.IsNullOrWhiteSpace(status) ? status : existingMeta.Status;
        var newType = !string.IsNullOrWhiteSpace(type) ? type : existingMeta.NoteType;
        var newDomain = existingMeta.Domain ?? vaultConfig.GetDomainForFolder(
            Path.GetDirectoryName(found.VaultRelativePath) ?? "");

        var frontmatter = BuildFrontmatter(newTags, newType ?? "", newStatus ?? "",
            existingMeta.Date, domain: newDomain, extraFields: existingMeta.ExtraFields,
            aliases: existingMeta.Aliases, updated: existingMeta.Updated);
        var newContent = NoteHelpers.TouchUpdated(
            frontmatter + body, DateOnly.FromDateTime(DateTime.Today), vaultConfig.MaintainUpdated);

        try
        {
            await WriteNoteTextAsync(found.FilePath, newContent, preconditions);
        }
        catch (VaultMutationException exception)
        {
            return exception.ToToolError();
        }

        await RefreshGeneratedIndexesAsync(Path.GetDirectoryName(found.VaultRelativePath));
        return $"[ok] Frontmatter updated in '{found.Name}'";
    }

    // move_note

    [McpServerTool, Description(
        "Moves and/or renames a note. Provide destination_folder to move, new_name to rename " +
        "(may include subfolders), or both. When the name changes, inbound wikilinks (bare name, " +
        "full path, aliases, headings, block refs, embeds) are rewritten; bare-name links shared " +
        "by another note are skipped and reported. When only the folder changes, just full-path " +
        "links are rewritten. update_links=false skips rewriting; dry_run=true previews.")]
    public async Task<string> move_note(
        [Description("Name or path of the note to move or rename.")] string note,
        [Description("Destination folder (relative to the vault). E.g. 'Archive/2024'. Empty = keep folder.")] string destination_folder = "",
        [Description("New name (without .md, may include subfolders). Empty = keep name.")] string new_name = "",
        [Description("If true (default), rewrites inbound wikilinks to the note's new location.")] bool update_links = true,
        [Description("If true, previews the change without modifying any file.")] bool dry_run = false,
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "")
    {
        Count(nameof(move_note), metrics);
        var preconditions = CreatePreconditions(
            expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id);
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        if (string.IsNullOrWhiteSpace(destination_folder) && string.IsNullOrWhiteSpace(new_name))
        {
            return KiokuError.InvalidArgument("Provide destination_folder, new_name, or both.");
        }

        string destPath;
        if (!string.IsNullOrWhiteSpace(new_name))
        {
            var target = string.IsNullOrWhiteSpace(destination_folder)
                ? new_name
                : $"{destination_folder.TrimEnd('/')}/{new_name}";
            destPath = BuildFilePath(target);
        }
        else
        {
            var destDir = NoteHelpers.EnsureInsideVault(
                config.VaultPath,
                Path.Combine(config.VaultPath, destination_folder));
            destPath = Path.Combine(destDir, Path.GetFileName(found.FilePath));
        }

        if (File.Exists(destPath))
        {
            return KiokuError.InvalidArgument(
                $"A note already exists at the destination path: {Path.GetRelativePath(config.VaultPath, destPath)}");
        }

        var newRelativePath = Path.GetRelativePath(config.VaultPath, destPath).Replace('\\', '/');
        var newShortName = Path.GetFileNameWithoutExtension(destPath);
        var nameChanged = !newShortName.Equals(found.Name, StringComparison.OrdinalIgnoreCase);
        var ambiguous = nameChanged &&
            vault.GetAllNotes().Count(n => n.Name.Equals(found.Name, StringComparison.OrdinalIgnoreCase)) > 1;
        var plan = new WikilinkRewriter.RewritePlan(
            OldShortName: found.Name,
            NewShortName: newShortName,
            OldFullPath: StripMdExtension(found.VaultRelativePath.Replace('\\', '/')),
            NewFullPath: StripMdExtension(newRelativePath),
            RewriteShortNameLinks: nameChanged,
            ShortNameAmbiguous: ambiguous);

        // Compute the plan before moving, but defer writes until the destination exists. This
        // prevents a failed File.Move from leaving inbound links pointing at a missing note.
        var linkSummary = update_links
            ? await UpdateInboundWikilinksAsync(plan, dryRun: true)
            : LinkUpdateSummary.Empty;

        var action = nameChanged ? "rename" : "move";
        if (dry_run)
        {
            return FormatDryRunResult(action, found.VaultRelativePath, newRelativePath, update_links, linkSummary);
        }

        var oldPath = found.FilePath;
        string? replacementContent = null;
        if (vaultConfig.MaintainUpdated)
        {
            var movedContent = await File.ReadAllTextAsync(oldPath, Encoding.UTF8);
            replacementContent = NoteHelpers.TouchUpdated(
                movedContent, DateOnly.FromDateTime(DateTime.Today), true);
        }

        try
        {
            if (_mutations is not null)
            {
                await _mutations.MoveAsync(oldPath, destPath, replacementContent, preconditions);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Move(oldPath, destPath);
                if (replacementContent is not null)
                {
                    await File.WriteAllTextAsync(destPath, replacementContent, Utf8NoBom);
                }

                await vault.SynchronizeFileMoveAsync(oldPath, destPath);
            }
        }
        catch (VaultMutationException exception)
        {
            return exception.ToToolError();
        }

        if (update_links && linkSummary.LinksUpdated > 0)
        {
            linkSummary = await UpdateInboundWikilinksAsync(plan, dryRun: false);
        }

        await RefreshGeneratedIndexesAsync(
            Path.GetDirectoryName(found.VaultRelativePath),
            Path.GetDirectoryName(newRelativePath));

        return $"[ok] Note {(nameChanged ? "renamed" : "moved")}:\n   Before: {found.VaultRelativePath}\n   After: {newRelativePath}" +
               FormatLinkSummarySuffix(update_links, linkSummary);
    }

    // delete_note

    [McpServerTool, Description(
        "Deletes a note from the vault by moving it to .trash folder (recoverable). " +
        "Set permanent=true to delete immediately (irreversible). " +
        "When dry_run is true, only reports what would be deleted without modifying the vault.")]
    public async Task<string> delete_note(
        [Description("Name or path of the note to delete.")] string note,
        [Description("If true, only reports what would be deleted without modifying the vault.")] bool dry_run = false,
        [Description("If true, deletes permanently instead of moving to trash. Default: false (soft delete).")] bool permanent = false,
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "")
    {
        Count(nameof(delete_note), metrics);
        var preconditions = CreatePreconditions(
            expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id);
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        if (permanent && !_paths.AllowPermanentDelete)
        {
            return KiokuError.AccessDenied(
                "Permanent deletion is disabled. Use soft delete or explicitly enable KIOKU_ALLOW_PERMANENT_DELETE.");
        }

        if (dry_run)
        {
            var action = permanent ? "permanently delete" : "move to trash";
            return $"[info] Would {action}: {found.VaultRelativePath}";
        }

        string filePath;
        try
        {
            filePath = _paths.ResolveVaultDeletePath(found.FilePath);
        }
        catch (VaultAccessDeniedException)
        {
            return KiokuError.AccessDenied();
        }


        if (permanent)
        {
            // Permanent delete
            try
            {
                if (_mutations is not null)
                {
                    await _mutations.DeleteAsync(filePath, preconditions);
                }
                else
                {
                    File.Delete(filePath);
                    vault.SynchronizeFileDelete(filePath);
                }
            }
            catch (VaultMutationException exception)
            {
                return exception.ToToolError();
            }

            await RefreshGeneratedIndexesAsync(Path.GetDirectoryName(found.VaultRelativePath));
            return $"[ok] Note permanently deleted: {found.VaultRelativePath}";
        }
        else
        {
            // Soft delete: move to .trash
            var trashDir = _paths.ResolveVaultWritePath(".trash");
            if (!Directory.Exists(trashDir))
            {
                Directory.CreateDirectory(trashDir);
            }

            // Generate a unique filename in trash and move the file under a per-vault lock. The
            // name selection and the move must be one atomic critical section: otherwise two
            // concurrent deletes of same-basename notes can select the same trash name and the
            // second move overwrites the first (File.Move is rename() on Unix, which clobbers).
            // Generate a unique filename in trash and move the file under a per-vault lock. The
            // name selection and the move must be one atomic critical section: otherwise two
            // concurrent deletes of same-basename notes can select the same trash name and the
            // second move overwrites the first (File.Move is rename() on Unix, which clobbers).
            string trashPath;
            using (await AcquireTrashLockAsync(config.VaultPath))
            {
                var fileName = Path.GetFileName(filePath);
                trashPath = Path.Combine(trashDir, fileName);
                var counter = 1;
                while (File.Exists(trashPath))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    trashPath = Path.Combine(trashDir, $"{nameWithoutExt}_{counter}{ext}");
                    counter++;
                }

                // overwrite: false so a name collision fails loudly instead of losing a note,
                // even if some future caller reaches this move without holding the lock.
                var move = _paths.ResolveVaultMove(filePath, trashPath);
                try
                {
                    if (_mutations is not null)
                    {
                        await _mutations.MoveAsync(
                            move.Source, move.Destination, preconditions: preconditions);
                    }
                    else
                    {
                        File.Move(move.Source, move.Destination, overwrite: false);
                    }

                    trashPath = move.Destination;
                }
                catch (VaultMutationException exception)
                {
                    return exception.ToToolError();
                }
            }

            vault.SynchronizeFileDelete(filePath);
            await RefreshGeneratedIndexesAsync(Path.GetDirectoryName(found.VaultRelativePath));

            var trashRelativePath = Path.GetRelativePath(config.VaultPath, trashPath);
            return $"[ok] Note moved to trash: {found.VaultRelativePath} → {trashRelativePath}\n" +
                   "Use manage_trash (action='restore') to recover if needed.";
        }
    }

    // manage_trash

    [McpServerTool, Description(
        "Manages the vault trash. action='list' (default) shows deleted notes in '.trash' or " +
        "'.obsidian/trash'; action='restore' moves a note out of the trash back into the vault " +
        "(to the vault root, or the folder given in destination). List supports filtering and pagination.")]
    public async Task<string> manage_trash(
        [Description("'list' (default) or 'restore'.")] string action = "list",
        [Description("Restore only: name or path of the note in the trash.")] string note = "",
        [Description("Restore only: target folder (vault-relative). Defaults to vault root.")] string destination = "",
        [Description("Restore only: if true, reports what would be restored without moving the file.")] bool dry_run = false,
        [Description("List only: case-insensitive relative-path prefix filter.")] string prefix = "",
        [Description("List only: maximum entries to return (default: 50).")] int limit = 50,
        [Description("List only: number of matching entries to skip.")] int offset = 0,
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the trash path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "")
    {
        Count(nameof(manage_trash), metrics);
        var preconditions = CreatePreconditions(
            expected_revision, expected_hash, claim_id, fence_generation, resource_key, mutation_id);
        switch (action.ToLowerInvariant())
        {
            case "list":
                {
                    if (offset < 0)
                    {
                        return KiokuError.InvalidArgument("'offset' must be 0 or greater.");
                    }

                    if (limit <= 0)
                    {
                        return KiokuError.InvalidArgument("'limit' must be greater than 0.");
                    }

                    limit = Math.Min(limit, 100);
                    var trashPath = FindTrashFolder();
                    if (trashPath is null)
                    {
                        return "[info] No trash folder found ('.trash' or '.obsidian/trash') — nothing deleted yet.";
                    }

                    var files = Directory.GetFiles(trashPath, "*.md", SearchOption.AllDirectories)
                        .Select(file => new
                        {
                            File = file,
                            Relative = Path.GetRelativePath(config.VaultPath, file).Replace('\\', '/')
                        })
                        .Where(item => string.IsNullOrWhiteSpace(prefix) ||
                                       item.Relative.StartsWith(prefix.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
                        .OrderBy(item => item.Relative, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (files.Count == 0)
                    {
                        return "[info] The trash is empty.";
                    }

                    var page = files.Skip(offset).Take(limit).ToList();
                    var sb = new StringBuilder(
                        $"[ok] Trash notes (total: {files.Count}, offset: {offset}, limit: {limit}, returned: {page.Count}):\n\n");
                    foreach (var item in page)
                    {
                        var age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(item.File);
                        var ageStr = age.TotalHours < 24 ? $"{(int)age.TotalHours}h" : $"{(int)age.TotalDays}d";
                        sb.AppendLine($"  {item.Relative} (modified {ageStr} ago)");
                    }

                    return sb.ToString();
                }

            case "restore":
                {
                    if (string.IsNullOrWhiteSpace(note))
                    {
                        return KiokuError.InvalidArgument("The 'note' parameter is required for action='restore'.");
                    }

                    if (Path.IsPathRooted(note) || note.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                        .Any(part => part is "." or ".."))
                    {
                        return KiokuError.InvalidArgument("The trash note must be a relative path inside the trash folder.");
                    }

                    var trashPath = FindTrashFolder();
                    if (trashPath is null)
                    {
                        return "[error] No trash folder found. Looked for '.trash' and '.obsidian/trash'.";
                    }

                    var trashFile = FindInTrash(note, trashPath);
                    if (trashFile is null)
                    {
                        return KiokuError.NotFound($"Note not found in trash: '{note}'");
                    }

                    string sourcePath;
                    try
                    {
                        sourcePath = _paths.ResolveVaultDeletePath(trashFile);
                    }
                    catch (VaultAccessDeniedException)
                    {
                        return KiokuError.AccessDenied();
                    }

                    var destPath = string.IsNullOrWhiteSpace(destination)
                        ? Path.Combine(config.VaultPath, Path.GetFileName(sourcePath))
                        : Path.Combine(config.VaultPath, destination, Path.GetFileName(sourcePath));
                    destPath = _paths.ResolveVaultWritePath(destPath);

                    if (File.Exists(destPath))
                    {
                        return KiokuError.InvalidArgument(
                            $"A note already exists at the destination: {Path.GetRelativePath(config.VaultPath, destPath)}");
                    }

                    if (dry_run)
                    {
                        var srcRel = Path.GetRelativePath(config.VaultPath, trashFile);
                        var dstRel = Path.GetRelativePath(config.VaultPath, destPath);
                        return $"[info] Would restore: {srcRel} → {dstRel}";
                    }

                    var restore = _paths.ResolveVaultMove(sourcePath, destPath);
                    try
                    {
                        if (_mutations is not null)
                        {
                            await _mutations.MoveAsync(
                                restore.Source, restore.Destination, preconditions: preconditions);
                        }
                        else
                        {
                            var destDir = Path.GetDirectoryName(destPath)!;
                            if (!string.IsNullOrEmpty(destDir))
                            {
                                Directory.CreateDirectory(destDir);
                            }

                            File.Move(restore.Source, restore.Destination);
                            await vault.SynchronizeFileReindexAsync(destPath);
                        }
                    }
                    catch (VaultMutationException exception)
                    {
                        return exception.ToToolError();
                    }

                    return $"[ok] Note restored to: {Path.GetRelativePath(config.VaultPath, destPath)}";
                }

            default:
                return KiokuError.InvalidArgument($"Unknown action '{action}'. Use 'list' or 'restore'.");
        }
    }

    private string? FindTrashFolder()
    {
        var candidates = new[] { ".trash", Path.Combine(".obsidian", "trash") };
        foreach (var c in candidates)
        {
            var path = Path.Combine(config.VaultPath, c);
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private string? FindInTrash(string name, string trashFolder)
    {
        var normalized = name.Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var nameWithExt = normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + ".md";
        var trashRoot = Path.GetFullPath(trashFolder);
        var trashPrefix = trashRoot.EndsWith(Path.DirectorySeparatorChar)
            ? trashRoot
            : trashRoot + Path.DirectorySeparatorChar;

        var exact = Path.GetFullPath(Path.Combine(trashRoot, nameWithExt));
        if (exact.StartsWith(trashPrefix, StringComparison.OrdinalIgnoreCase) && File.Exists(exact))
        {
            return exact;
        }

        // Obsidian preserves folder structure in trash
        var withSubfolder = Path.GetFullPath(Path.Combine(trashRoot, nameWithExt));
        if (withSubfolder.StartsWith(trashPrefix, StringComparison.OrdinalIgnoreCase) && File.Exists(withSubfolder))
        {
            return withSubfolder;
        }

        var bare = Path.GetFileNameWithoutExtension(nameWithExt);
        return Directory.GetFiles(trashFolder, "*.md", SearchOption.AllDirectories)
            .FirstOrDefault(f =>
                Path.GetFileName(f).Equals(Path.GetFileName(nameWithExt), StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(f).Equals(bare, StringComparison.OrdinalIgnoreCase));
    }

    // Private helpers

    private Note? ResolveNote(string nameOrPath) => NoteHelpers.ResolveNote(nameOrPath, vault);

    private async Task RefreshGeneratedIndexesAsync(params string?[] folders)
    {
        if (zettelkasten is null || !vaultConfig.RefreshGeneratedIndexes)
        {
            return;
        }

        await zettelkasten.RefreshGeneratedIndexesAsync(
            folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!));
    }

    private async Task WriteNoteTextAsync(
        string filePath,
        string content,
        VaultMutationPreconditions preconditions)
    {
        if (_mutations is not null)
        {
            await _mutations.WriteTextAsync(filePath, content, preconditions);
            return;
        }

        await File.WriteAllTextAsync(filePath, content, Utf8NoBom);
        await vault.SynchronizeFileReindexAsync(filePath);
    }

    private static VaultMutationPreconditions CreatePreconditions(
        string expectedRevision,
        string expectedHash,
        string claimId,
        long fenceGeneration,
        string resourceKey,
        string mutationId) =>
        new()
        {
            ExpectedRevision = string.IsNullOrWhiteSpace(expectedRevision) ? null : expectedRevision,
            ExpectedHash = string.IsNullOrWhiteSpace(expectedHash) ? null : expectedHash,
            ClaimId = string.IsNullOrWhiteSpace(claimId) ? null : claimId,
            FenceGeneration = fenceGeneration > 0 ? fenceGeneration : null,
            ResourceKey = string.IsNullOrWhiteSpace(resourceKey) ? null : resourceKey,
            MutationId = string.IsNullOrWhiteSpace(mutationId) ? null : mutationId,
        };

    private string BuildFilePath(string name) => NoteHelpers.BuildFilePath(name, config.VaultPath);

    private static string BuildFrontmatter(
        IEnumerable<string> tags,
        string? type,
        string? status,
        DateOnly? date = null,
        string? domain = null,
        IReadOnlyDictionary<string, string>? extraFields = null,
        IEnumerable<string>? aliases = null,
        DateOnly? updated = null) =>
        NoteHelpers.BuildFrontmatter(tags, type, status, date, domain: domain, extraFields: extraFields,
            aliases: aliases, updated: updated);

    // Wikilink auto-update helpers (move_note / rename_note)

    private sealed record LinkUpdateSummary(
        int LinksUpdated,
        int NotesUpdated,
        IReadOnlyList<string> AmbiguousLinks,
        IReadOnlyList<(string Note, int Count)> Details)
    {
        public static readonly LinkUpdateSummary Empty = new(0, 0, [], []);
    }

    private async Task<LinkUpdateSummary> UpdateInboundWikilinksAsync(WikilinkRewriter.RewritePlan plan, bool dryRun)
    {
        var candidates = new Dictionary<string, Note>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in vault.GetBacklinks(plan.OldShortName))
        {
            candidates[candidate.FilePath] = candidate;
        }

        foreach (var candidate in vault.GetBacklinks(plan.OldFullPath))
        {
            candidates[candidate.FilePath] = candidate;
        }

        var linksUpdated = 0;
        var notesUpdated = 0;
        var ambiguous = new List<string>();
        var details = new List<(string, int)>();

        foreach (var source in candidates.Values.OrderBy(n => n.VaultRelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var raw = await File.ReadAllTextAsync(source.FilePath, Encoding.UTF8);
            var bodyStart = FrontmatterParser.GetBodyStart(raw);
            var result = WikilinkRewriter.Rewrite(raw, plan, bodyStart);

            foreach (var link in result.AmbiguousMatches)
            {
                ambiguous.Add($"{source.VaultRelativePath}: [[{link}]]");
            }

            if (result.ReplacedCount == 0)
            {
                continue;
            }

            linksUpdated += result.ReplacedCount;
            notesUpdated++;
            details.Add((source.VaultRelativePath, result.ReplacedCount));

            if (!dryRun)
            {
                var contentToWrite = NoteHelpers.TouchUpdated(
                    result.NewContent, DateOnly.FromDateTime(DateTime.Today), vaultConfig.MaintainUpdated);
                await WriteNoteTextAsync(
                    source.FilePath,
                    contentToWrite,
                    new VaultMutationPreconditions());
            }
        }

        return new LinkUpdateSummary(linksUpdated, notesUpdated, ambiguous, details);
    }

    private static string StripMdExtension(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? path[..^3] : path;

    private static string FormatLinkSummarySuffix(bool updateLinks, LinkUpdateSummary summary)
    {
        if (!updateLinks)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append($"\n   Updated {summary.LinksUpdated} wikilink(s) in {summary.NotesUpdated} note(s).");

        if (summary.AmbiguousLinks.Count > 0)
        {
            sb.Append($"\n   Skipped {summary.AmbiguousLinks.Count} ambiguous bare-name link(s) (another note shares this name):");
            foreach (var link in summary.AmbiguousLinks)
            {
                sb.Append($"\n     - {link}");
            }
        }

        return sb.ToString();
    }

    private static string FormatDryRunResult(
        string action, string beforePath, string afterPath, bool updateLinks, LinkUpdateSummary summary)
    {
        var sb = new StringBuilder();
        sb.Append("[info] Dry run — no files were modified.\n");
        sb.Append($"   Would {action}: {beforePath} -> {afterPath}");

        if (!updateLinks)
        {
            sb.Append("\n   Link updates disabled (update_links=false).");
            return sb.ToString();
        }

        if (summary.Details.Count == 0)
        {
            sb.Append("\n   No inbound wikilinks require updates.");
        }
        else
        {
            sb.Append($"\n   Would update {summary.LinksUpdated} wikilink(s) in {summary.NotesUpdated} note(s):");
            foreach (var (noteName, count) in summary.Details)
            {
                sb.Append($"\n     - {noteName}: {count} link(s)");
            }
        }

        if (summary.AmbiguousLinks.Count > 0)
        {
            sb.Append($"\n   {summary.AmbiguousLinks.Count} ambiguous bare-name link(s) would be skipped:");
            foreach (var link in summary.AmbiguousLinks)
            {
                sb.Append($"\n     - {link}");
            }
        }

        return sb.ToString();
    }
}
