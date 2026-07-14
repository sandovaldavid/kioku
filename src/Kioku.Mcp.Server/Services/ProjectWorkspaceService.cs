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
    KiokuConfiguration config, VaultConfigService vaultConfig, ObsidianBridgeService bridge)
{
    /// <summary>Doc type keys, each mapping to a per-project subfolder.</summary>
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
        ["adr"] = ["project", "number", "context", "decision", "consequences", "alternatives"],
        ["bug"] = ["project", "symptom", "root_cause", "fix", "related_files"],
        ["plan"] = ["project", "objective", "steps", "ticket"],
        ["knowledge"] = ["project", "content"],
        ["idea"] = ["project", "description"],
        ["session"] = ["project", "goal", "agent"],
        ["daily"] = ["project"],
        ["ticket"] = ["project"],
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
    /// Validates a project name: must be a single folder name, not a path.
    /// Returns an error message or null when valid.
    /// </summary>
    public static string? ValidateProjectName(string project)
    {
        if (string.IsNullOrWhiteSpace(project))
        {
            return "[error] The 'project' parameter cannot be empty. Use list_projects to see existing projects.";
        }

        if (project.Contains('/') || project.Contains('\\') || project.Contains(".."))
        {
            return $"[error] Invalid project name '{project}'. Use a plain folder name without path separators.";
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

    /// <summary>
    /// Ensures the project folder, its standard subfolders, and the project MOC note exist.
    /// Never overwrites existing files. Returns the vault-relative paths that were created.
    /// </summary>
    public async Task<List<string>> EnsureProjectScaffoldAsync(string project)
    {
        var created = new List<string>();
        var projectFolder = GetProjectFolder(project);

        if (!Directory.Exists(projectFolder))
        {
            Directory.CreateDirectory(projectFolder);
            created.Add(ToVaultRelative(projectFolder) + "/");
        }

        foreach (var key in SubfolderKeys)
        {
            var sub = GetSubfolder(project, key);
            if (!Directory.Exists(sub))
            {
                Directory.CreateDirectory(sub);
                created.Add(ToVaultRelative(sub) + "/");
            }
        }

        var mocPath = Path.Combine(projectFolder, $"{project}.md");
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
                noteTitle: project);

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
                extraFields: new Dictionary<string, string> { ["project"] = project });

            await File.WriteAllTextAsync(mocPath, frontmatter + "\n" + body, Encoding.UTF8);

            var evalResult = await bridge.EvaluateTemplaterInPlaceAsync(body, ToVaultRelative(mocPath));
            created.Add(evalResult.Warning is null
                ? ToVaultRelative(mocPath)
                : $"{ToVaultRelative(mocPath)} [warning: {evalResult.Warning}]");
        }

        return created;
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
            return await File.ReadAllTextAsync(overridePath, Encoding.UTF8);
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

        return [.. Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)];
    }
}
