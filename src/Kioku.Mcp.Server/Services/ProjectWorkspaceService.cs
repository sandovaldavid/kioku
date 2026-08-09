using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Shared logic for per-project engineering workspaces: path resolution, lazy scaffolding,
/// ADR numbering, template resolution (vault override falls back to embedded defaults),
/// and doc enumeration. Used by EngineeringWorkflowTools and SessionContextTools.
/// </summary>
public sealed partial class ProjectWorkspaceService(
    KiokuConfiguration config,
    VaultConfigService vaultConfig,
    ObsidianBridgeService bridge,
    IVaultMutationService? mutations = null)
{
    /// <summary>Core per-project subfolders created eagerly by the project scaffold.</summary>
    public static readonly string[] CoreSubfolderKeys =
        ["decisions", "bugs", "plans", "knowledge", "sessions", "backlog"];

    /// <summary>Supported workflow subfolders created only when an explicit write needs them.</summary>
    public static readonly string[] OptionalSubfolderKeys = ["daily", "tickets"];

    /// <summary>
    /// All recognized project subfolder keys. Keep this full set for discovery, context filters,
    /// counts, historical projects, and template mappings; scaffold creation uses CoreSubfolderKeys.
    /// </summary>
    public static readonly string[] SubfolderKeys =
        ["decisions", "bugs", "plans", "knowledge", "sessions", "daily", "tickets", "backlog"];

    /// <summary>Template type keys shipped as embedded defaults.</summary>
    public static readonly string[] TemplateKeys =
        ["adr", "bug", "plan", "knowledge", "idea", "session", "daily", "ticket", "project-moc"];

    /// <summary>Built-in template variables available on every doc type (see NoteHelpers.ExpandTemplateVariables).</summary>
    public static readonly string[] BuiltInTemplateVariables =
        ["date", "time", "datetime", "year", "month", "day", "uid", "title"];

    /// <summary>Type-specific template variables supported per doc type, beyond the built-ins.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> TemplateVariables = new Dictionary<string, string[]>
    {
        ["adr"] = ["project", "project_link", "number", "context", "decision", "consequences", "alternatives"],
        ["bug"] = ["project", "project_link", "symptom", "root_cause", "fix", "related_files"],
        ["plan"] = ["project", "project_link", "objective", "steps", "ticket"],
        ["knowledge"] = ["project", "project_link", "content"],
        ["idea"] = ["project", "project_link", "description"],
        ["session"] = ["project", "project_link", "goal", "agent"],
        ["daily"] = ["project", "project_link"],
        ["ticket"] = ["project", "project_link"],
        ["project-moc"] = ["project", "project_folder", "decisions_folder", "plans_folder", "bugs_folder", "backlog_folder"],
    };

    private static readonly string[] TemplateFolderCandidates =
        ["Templates", "99_System/Templates", "_templates", "System/Templates"];

    [GeneratedRegex(@"^ADR-(?<num>\d{1,4})-", RegexOptions.IgnoreCase)]
    private static partial Regex AdrNumberRegex();

    // Matches {{ variable }} or {{variable}} Mustache/Handlebars syntax
    [GeneratedRegex(@"\{\{\s*(?<var>[a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}")]
    private static partial Regex TemplateVarRegex();

    /// <summary>Distinct {{var}} names referenced in a template body.</summary>
    public static IReadOnlyList<string> ExtractTemplateVariableNames(string content) =>
        [.. TemplateVarRegex().Matches(content)
            .Select(m => m.Groups["var"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>All variables recognized for a doc type: built-ins plus its type-specific ones.</summary>
    public static IReadOnlyList<string> SupportedVariablesFor(string typeKey) =>
        [.. BuiltInTemplateVariables, .. TemplateVariables.TryGetValue(typeKey, out var v) ? v : []];

    public string ProjectsRootRelative => vaultConfig.GetFolder("projects") ?? "Projects";

    public string KnowledgeRootRelative => vaultConfig.GetFolder("knowledge") ?? "Knowledge";

    public string ProjectsRoot => NoteHelpers.EnsureInsideVault(
        config.VaultPath, Path.Combine(config.VaultPath, ProjectsRootRelative));

    public string KnowledgeRoot => NoteHelpers.EnsureInsideVault(
        config.VaultPath, Path.Combine(config.VaultPath, KnowledgeRootRelative));

    /// <summary>
    /// Validates a project identifier: a plain folder name, or a '/'-separated path grouping
    /// several projects under shared folders (e.g. "Atena/api.core"). Each segment must be
    /// non-empty (no leading/trailing/double slashes) and backslashes/'..' are always rejected.
    /// Returns an error message or null when valid.
    /// </summary>
    public static string? ValidateProjectName(string project)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            return "[error] The 'project' parameter cannot be empty. Use list_projects to see existing projects.";
        }

        if (project.Contains('\\') || project.Contains(".."))
        {
            return $"[error] Invalid project name '{project}'. Use '/' to group projects (e.g. 'Atena/api.core'); no backslashes or '..'.";
        }

        if (project.Split('/').Any(string.IsNullOrWhiteSpace))
        {
            return $"[error] Invalid project name '{project}'. Each '/'-separated segment must be non-empty (no leading, trailing, or double slashes).";
        }

        return null;
    }

    public string GetProjectFolder(string project) =>
        NoteHelpers.EnsureInsideVault(config.VaultPath, Path.Combine(ProjectsRoot, project));

    public string GetSubfolder(string project, string key) =>
        NoteHelpers.EnsureInsideVault(
            config.VaultPath,
            Path.Combine(GetProjectFolder(project), vaultConfig.GetEngineeringSubfolder(key)));

    public string ToVaultRelative(string absolutePath) =>
        Path.GetRelativePath(config.VaultPath, absolutePath).Replace('\\', '/');

    /// <summary>Leaf (last '/'-separated) segment of a possibly-grouped project identifier.</summary>
    public static string ProjectLeafName(string project) => project.Split('/')[^1];

    /// <summary>
    /// Ensures the project folder, its core subfolders, and the project MOC note exist.
    /// Optional workflow folders such as daily/ and tickets/ are recognized but materialize only
    /// when an explicit note write targets them. Never overwrites existing files. Returns the
    /// vault-relative paths that were created.
    /// </summary>
    public async Task<List<string>> EnsureProjectScaffoldAsync(string project)
    {
        using var scaffoldLock = await AcquireScaffoldLockAsync(project);
        var created = new List<string>();
        var projectFolder = GetProjectFolder(project);

        if (!Directory.Exists(projectFolder))
        {
            Directory.CreateDirectory(projectFolder);
            created.Add(ToVaultRelative(projectFolder) + "/");
        }

        foreach (var key in CoreSubfolderKeys)
        {
            var sub = GetSubfolder(project, key);
            if (!Directory.Exists(sub))
            {
                Directory.CreateDirectory(sub);
                created.Add(ToVaultRelative(sub) + "/");
            }
        }

        // Use the folder's own leaf name for the MOC file, not the full (possibly grouped)
        // project identifier — "Atena/api.core" scaffolds ".../Atena/api.core/api.core.md",
        // never ".../Atena/api.core/Atena/api.core.md".
        var leafName = Path.GetFileName(projectFolder);
        var mocPath = Path.Combine(projectFolder, $"{leafName}.md");
        if (!File.Exists(mocPath))
        {
            var template = await ResolveTemplateAsync("project-moc");
            var body = NoteHelpers.ExpandTemplateVariables(
                template,
                new Dictionary<string, string>
                {
                    ["project"] = project,
                    // Vault-relative subfolder paths, used by the Dataview blocks in the MOC template
                    ["project_folder"] = ToVaultRelative(projectFolder),
                    ["decisions_folder"] = ToVaultRelative(GetSubfolder(project, "decisions")),
                    ["plans_folder"] = ToVaultRelative(GetSubfolder(project, "plans")),
                    ["bugs_folder"] = ToVaultRelative(GetSubfolder(project, "bugs")),
                    ["backlog_folder"] = ToVaultRelative(GetSubfolder(project, "backlog")),
                },
                noteTitle: leafName);

            var relFolder = ToVaultRelative(projectFolder);
            var tags = NoteHelpers.MergeTagsWithInheritance(
                ["moc", "project"],
                vaultConfig.GetInheritedTags(relFolder),
                vaultConfig.ExcludeFromTags);
            var frontmatter = NoteHelpers.BuildFrontmatter(
                tags,
                type: "moc",
                status: "active",
                date: DateOnly.FromDateTime(DateTime.Now),
                domain: vaultConfig.GetDomainForFolder(relFolder),
                cssClasses: ["kioku-project-moc"],
                updated: vaultConfig.MaintainUpdated ? DateOnly.FromDateTime(DateTime.Today) : null,
                extraFields: new Dictionary<string, string> { ["project"] = project });

            await WriteTextAsync(mocPath, frontmatter + "\n" + body, requireAbsent: true);

            var evalResult = await bridge.EvaluateTemplaterInPlaceAsync(body, ToVaultRelative(mocPath));
            created.Add(evalResult.Warning is null
                ? ToVaultRelative(mocPath)
                : $"{ToVaultRelative(mocPath)} [warning: {evalResult.Warning}]");
        }

        // Only on first-time scaffold of this project (avoids redundant I/O on every
        // record_adr/log_bug call once the project already exists).
        if (created.Count > 0)
        {
            await EnsureEngineeringTemplatesOnDiskAsync();
            var registered = await RegisterTemplaterFolderTemplatesAsync(project);
            if (registered > 0)
            {
                created.Add($"Templater folder templates: {registered} registered (Settings → Folder Templates)");
            }
        }

        return created;
    }


    /// <summary>Doc type key each engineering subfolder maps to, for Templater folder-template registration.</summary>
    private static readonly (string SubfolderKey, string TemplateKey)[] SubfolderTemplatePairs =
    [
        ("decisions", "adr"), ("bugs", "bug"), ("plans", "plan"), ("knowledge", "knowledge"),
        ("sessions", "session"), ("daily", "daily"), ("tickets", "ticket"), ("backlog", "idea"),
    ];

    /// <summary>
    /// Copies any of the embedded default engineering templates that don't yet exist on disk to
    /// {templates}/kioku/{typeKey}.md. Idempotent: never overwrites an existing file. Templater
    /// can only point to a real vault file, so this must run before a folder template pointing
    /// at one of these files can be registered in Templater's own settings.
    /// </summary>
    public async Task<(List<string> Created, List<string> Skipped)> EnsureEngineeringTemplatesOnDiskAsync()
    {
        var created = new List<string>();
        var skipped = new List<string>();

        var kiokuTemplatesDir = Path.Combine(ResolveTemplatesFolderOrDefault(), "kioku");
        Directory.CreateDirectory(kiokuTemplatesDir);

        foreach (var key in TemplateKeys)
        {
            var target = Path.Combine(kiokuTemplatesDir, $"{key}.md");
            var rel = ToVaultRelative(target);
            if (File.Exists(target))
            {
                skipped.Add(rel);
            }
            else
            {
                await WriteTextAsync(target, ReadEmbeddedTemplate(key), requireAbsent: true);
                created.Add(rel);
            }
        }

        return (created, skipped);
    }

    /// <summary>
    /// Registers all supported project subfolders (including future optional daily/ and tickets/
    /// paths) as folder templates in Templater's own settings. Templater resolves a folder mapping
    /// when a file is created there, so the mapped folder itself does not need to exist at scaffold
    /// time. This keeps manual first-use creation in Obsidian templated without materializing the
    /// optional folders. Deliberately excludes the project root itself (would apply the project-MOC
    /// template to any new note created there). Never overwrites a folder the user already mapped
    /// in Templater, even to a different template. No-op if Templater isn't installed or its settings
    /// file doesn't exist yet, or if the corresponding template file isn't actually on disk.
    /// </summary>
    public async Task<int> RegisterTemplaterFolderTemplatesAsync(string project)
    {
        var entries = SubfolderTemplatePairs
            .Select(p => (
                Folder: ToVaultRelative(GetSubfolder(project, p.SubfolderKey)),
                Template: ToVaultRelative(Path.Combine(ResolveTemplatesFolderOrDefault(), "kioku", $"{p.TemplateKey}.md"))))
            .Where(p => File.Exists(Path.Combine(config.VaultPath, p.Template)))
            .ToList();

        return entries.Count == 0
            ? 0
            : await TemplaterFolderTemplates.RegisterFolderTemplatesAsync(config.VaultPath, entries, mutations);
    }

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> AdrLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ScaffoldLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private async Task<IDisposable> AcquireScaffoldLockAsync(string project)
    {
        var semaphore = ScaffoldLocks.GetOrAdd(GetProjectFolder(project), _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        return new SemaphoreReleaser(semaphore);
    }

    private async Task WriteTextAsync(string path, string content, bool requireAbsent)
    {
        if (mutations is null)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, content, NoteHelpers.Utf8NoBom);
            return;
        }

        if (requireAbsent)
        {
            await mutations.CreateTextAsync(path, content);
        }
        else
        {
            await mutations.WriteTextAsync(path, content);
        }
    }

    /// <summary>
    /// Serializes ADR number allocation per project. GetNextAdrNumber scans disk with no
    /// locking of its own, so two rapid/concurrent record_adr calls for the same project
    /// could otherwise compute the same "next" number and both succeed (filenames differ
    /// only by title slug, so there's no collision to force a retry). Callers should hold
    /// this for the entire compute-number-then-write-file critical section.
    /// </summary>
    public async Task<IDisposable> AcquireAdrLockAsync(string project)
    {
        var semaphore = AdrLocks.GetOrAdd(project, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        return new SemaphoreReleaser(semaphore);
    }

    private sealed class SemaphoreReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }

    /// <summary>
    /// Next sequential ADR number for a project, scanning the decisions folder on disk
    /// (max existing number + 1; 1 for an empty or missing folder).
    /// </summary>
    public int GetNextAdrNumber(string project)
    {
        var decisionsFolder = GetSubfolder(project, "decisions");
        if (!Directory.Exists(decisionsFolder))
        {
            return 1;
        }

        var max = 0;
        foreach (var file in Directory.EnumerateFiles(decisionsFolder, "ADR-*.md", SearchOption.TopDirectoryOnly))
        {
            var match = AdrNumberRegex().Match(Path.GetFileName(file));
            if (match.Success && int.TryParse(match.Groups["num"].Value, out var n) && n > max)
            {
                max = n;
            }
        }

        return max + 1;
    }

    /// <summary>
    /// Resolves the body template for a doc type: a vault override at
    /// {templates}/kioku/{typeKey}.md wins; otherwise the embedded default is returned.
    /// </summary>
    public async Task<string> ResolveTemplateAsync(string typeKey)
    {
        var overridePath = GetVaultTemplatePath(typeKey);
        if (overridePath is not null && File.Exists(overridePath))
        {
            return await NoteHelpers.ReadAllTextAsync(overridePath);
        }

        return ReadEmbeddedTemplate(typeKey);
    }

    /// <summary>
    /// Absolute path where the vault override template for a type key lives
    /// ({templates}/kioku/{typeKey}.md), or null when no templates folder exists.
    /// </summary>
    public string? GetVaultTemplatePath(string typeKey)
    {
        var folder = ResolveTemplatesFolder();
        return folder is null ? null : Path.Combine(folder, "kioku", $"{typeKey}.md");
    }

    /// <summary>
    /// The vault's templates folder: folders.templates from config first, then the
    /// conventional candidates. Returns the first existing folder, or the configured/default
    /// path even when missing (callers that write templates create it).
    /// </summary>
    public string ResolveTemplatesFolderOrDefault()
    {
        return ResolveTemplatesFolder() ?? NoteHelpers.EnsureInsideVault(
            config.VaultPath,
            Path.Combine(config.VaultPath, vaultConfig.GetFolder("templates") ?? TemplateFolderCandidates[0]));
    }

    private string? ResolveTemplatesFolder()
    {
        var configured = vaultConfig.GetFolder("templates");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = NoteHelpers.EnsureInsideVault(
                config.VaultPath, Path.Combine(config.VaultPath, configured));
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        foreach (var candidate in TemplateFolderCandidates)
        {
            var path = Path.Combine(config.VaultPath, candidate);
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static string ReadEmbeddedTemplate(string typeKey)
    {
        var resourceName = $"Kioku.Mcp.Server.Resources.Templates.AgentWorkflow.{typeKey}.md";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded template not found: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Enumerates markdown files in a project subfolder, newest first (by last write time).
    /// </summary>
    public IReadOnlyList<FileInfo> EnumerateProjectDocs(string project, string subfolderKey)
    {
        var folder = GetSubfolder(project, subfolderKey);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)];
    }

    /// <summary>
    /// Recursively discovers project identifiers under the projects root, so projects can be
    /// grouped in plain folders (e.g. Projects/Atena/api.core and Projects/Atena/api.common are
    /// both discovered as "Atena/api.core" and "Atena/api.common" — "Atena" itself is a pure
    /// grouping folder, not a project, and is never listed).
    /// A directory counts as a project if it has its own "{leaf}.md" MOC note with type: moc,
    /// or already has at least one recognized engineering subfolder, including historical
    /// optional daily/ and tickets/ folders. Anything else is treated as a grouping folder and
    /// recursed into.
    /// </summary>
    public IReadOnlyList<string> DiscoverProjects()
    {
        var results = new List<string>();
        if (Directory.Exists(ProjectsRoot))
        {
            // Never evaluate IsProjectFolder on ProjectsRoot itself: a vault-level MOC note
            // there (or engineering subfolders placed directly under it) would otherwise
            // misclassify the whole root as a single project named ".", hiding everything
            // beneath it. Always recurse starting from its direct subdirectories instead.
            foreach (var sub in Directory.EnumerateDirectories(ProjectsRoot)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                WalkForProjects(sub, results);
            }
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    private void WalkForProjects(string dir, List<string> results)
    {
        if (IsProjectFolder(dir))
        {
            results.Add(Path.GetRelativePath(ProjectsRoot, dir).Replace('\\', '/'));
            return;
        }

        foreach (var sub in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            WalkForProjects(sub, results);
        }
    }

    private bool IsProjectFolder(string dir)
    {
        var mocPath = Path.Combine(dir, $"{Path.GetFileName(dir)}.md");
        if (File.Exists(mocPath))
        {
            var metadata = FrontmatterParser.Parse(File.ReadAllText(mocPath));
            if (string.Equals(metadata.NoteType, "moc", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return SubfolderKeys.Any(key => Directory.Exists(Path.Combine(dir, vaultConfig.GetEngineeringSubfolder(key))));
    }
}
