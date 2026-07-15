using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for high-level workflow operations, including template management
/// and template-based note creation.
/// </summary>
[McpServerToolType]
public sealed partial class WorkflowTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    VaultConfigService vaultConfig,
    ObsidianBridgeService bridge)
{
    private static readonly string[] TemplateFolderCandidates =
        ["Templates", "99_System/Templates", "_templates", "System/Templates"];

    // Matches {{ variable }} or {{variable}} Mustache/Handlebars syntax
    [GeneratedRegex(@"\{\{\s*(?<var>[a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}")]
    private static partial Regex TemplateVarRegex();

    // manage_templates

    [McpServerTool, Description(
        "Manages note templates. scope='vault' handles templates in the vault's configured " +
        "templates folder; scope='engineering' handles the engineering document templates and " +
        "their vault overrides. action is list, get, or set. Vault set never overwrites " +
        "an existing file.")]
    public async Task<string> manage_templates(
        [Description("Template scope: 'vault' or 'engineering'.")] string scope = "vault",
        [Description("Action: 'list', 'get', or 'set'.")] string action = "list",
        [Description("Vault template name without .md. Required for vault get/set.")] string name = "",
        [Description("Engineering template type: adr, bug, plan, knowledge, idea, session, daily, ticket, or project-moc. Required for engineering get/set.")] string type_key = "",
        [Description("Template body. Required for engineering set unless reset_to_default=true; optional for vault set.")] string content = "",
        [Description("Vault templates folder relative to the vault. Leave empty to auto-detect.")] string templates_folder = "",
        [Description("For engineering set, delete the vault override and use the embedded default.")] bool reset_to_default = false)
    {
        var normalizedScope = scope.Trim().ToLowerInvariant();
        if (normalizedScope is not ("vault" or "engineering"))
        {
            return $"[error] Invalid template scope '{scope}'. Valid scopes: vault, engineering.";
        }

        var normalizedAction = action.Trim().ToLowerInvariant();
        if (normalizedAction is not ("list" or "get" or "set"))
        {
            return $"[error] Invalid template action '{action}'. Valid actions: list, get, set.";
        }

        return normalizedScope == "vault"
            ? await ManageVaultTemplatesAsync(normalizedAction, name, content, templates_folder)
            : await ManageEngineeringTemplatesAsync(normalizedAction, type_key, content, reset_to_default);
    }

    // create_note_from_template

    [Description(
        "Creates a new note by applying a template with variable substitution. " +
        "Replaces {{ variable }} placeholders with the provided values. " +
        "Built-in variables: {{date}} (today), {{time}} (now), {{title}} (note name).")]
    public async Task<string> create_note_from_template(
        [Description("Name of the template (without .md extension). Use manage_templates with scope='vault' and action='list' to see available templates.")] string template_name,
        [Description("Path for the new note (without .md extension). Can include subfolders: 'Projects/My Note'.")] string target_path,
        [Description(
            "Variables to inject into the template as key-value pairs. " +
            "Example: {\"title\": \"My Note\", \"status\": \"draft\", \"author\": \"David\"}. " +
            "Built-in variables (date, time, title) are auto-populated if not provided.")] Dictionary<string, string>? variables = null,
        [Description("Templates folder relative to vault root. Leave empty to auto-detect.")] string templates_folder = "")
    {
        // Resolve template file
        var folder = ResolveTemplatesFolder(templates_folder);
        if (folder is null)
        {
            return "[error] No templates folder found. Create a 'Templates' folder in your vault first.";
        }

        var templatePath = Path.Combine(folder, template_name + ".md");
        if (!File.Exists(templatePath))
        {
            return $"[error] Template not found: '{template_name}'. Use manage_templates(scope='vault', action='list') to see available templates.";
        }

        // Resolve target file path
        var targetFilePath = BuildFilePath(target_path);
        if (File.Exists(targetFilePath))
        {
            return $"[error] Note already exists: '{target_path}'. Use edit_note to modify it.";
        }

        // Use provided variables or start fresh
        var vars = variables ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Inject built-in variables (only if not already provided)
        var now = DateTime.Now;
        vars.TryAdd("date", now.ToString("yyyy-MM-dd"));
        vars.TryAdd("time", now.ToString("HH:mm"));
        vars.TryAdd("title", Path.GetFileNameWithoutExtension(target_path.Replace('/', Path.DirectorySeparatorChar)));
        vars.TryAdd("datetime", now.ToString("yyyy-MM-dd HH:mm"));

        // Read and interpolate template
        var templateContent = await File.ReadAllTextAsync(templatePath, Encoding.UTF8);
        var rendered = TemplateVarRegex().Replace(templateContent, match =>
        {
            var varName = match.Groups["var"].Value;
            return vars.TryGetValue(varName, out var value) ? value : match.Value; // keep unresolved placeholders as-is
        });

        // Check for unresolved variables
        var unresolved = TemplateVarRegex()
            .Matches(rendered)
            .Select(m => m.Groups["var"].Value)
            .Distinct()
            .ToList();

        // Create target directory and write note
        var targetDir = Path.GetDirectoryName(targetFilePath)!;
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(targetFilePath, rendered, NoteHelpers.Utf8NoBom);
        // Reindex immediately (matches every other creation tool) instead of relying solely on
        // the FileSystemWatcher's 500ms debounce — a caller following the documented pattern of
        // an immediate follow-up update_frontmatter call would otherwise race the watcher and
        // get a spurious "note not found".
        await vault.SynchronizeFileReindexAsync(targetFilePath);

        var relPath = Path.GetRelativePath(config.VaultPath, targetFilePath).Replace('\\', '/');

        var evalResult = await bridge.EvaluateTemplaterInPlaceAsync(rendered, relPath);
        if (evalResult.Applied)
        {
            await vault.SynchronizeFileReindexAsync(targetFilePath);
        }

        var result = new StringBuilder($"[ok] Note created from template '{template_name}': {relPath}");

        if (unresolved.Count > 0)
        {
            result.AppendLine();
            result.Append($"   Warning: {unresolved.Count} unresolved variable(s): " +
                          string.Join(", ", unresolved.Select(v => "{{" + v + "}}")));
        }

        if (evalResult.Warning is not null)
        {
            result.AppendLine();
            result.Append($"   [warning] {evalResult.Warning}");
        }

        return result.ToString();
    }

    // Private helpers

    private async Task<string> ManageVaultTemplatesAsync(
        string action, string name, string content, string templatesFolder)
    {
        if (action == "list")
        {
            return ListVaultTemplates(templatesFolder);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return "[error] Template name cannot be empty.";
        }

        var folder = ResolveTemplatesFolder(templatesFolder);
        if (folder is null && action == "get")
        {
            return "[error] No templates folder found. Create a 'Templates' folder in your vault first.";
        }

        folder ??= NoteHelpers.EnsureInsideVault(
            config.VaultPath,
            Path.Combine(config.VaultPath, TemplateFolderCandidates[0]));
        Directory.CreateDirectory(folder);

        var filePath = NoteHelpers.EnsureInsideVault(config.VaultPath, Path.Combine(folder, name + ".md"));
        if (action == "get")
        {
            if (!File.Exists(filePath))
            {
                return $"[error] Template not found: '{name}'. Use manage_templates(scope='vault', action='list') to see available templates.";
            }

            var templateContent = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var vars = TemplateVarRegex()
                .Matches(templateContent)
                .Select(m => m.Groups["var"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v)
                .ToList();
            var result = new StringBuilder($"[ok] Template '{name}' ({Path.GetRelativePath(config.VaultPath, filePath)}):\n\n");
            result.AppendLine($"Supported variables: {string.Join(", ", vars.Select(v => "{{" + v + "}}"))}");
            result.AppendLine();
            result.AppendLine("```markdown");
            result.AppendLine(templateContent);
            result.Append("```");
            return result.ToString();
        }

        if (File.Exists(filePath))
        {
            return $"[error] Template '{name}' already exists. Delete it first or use a different name.";
        }

        await File.WriteAllTextAsync(filePath, content, NoteHelpers.Utf8NoBom);
        var variables = TemplateVarRegex()
            .Matches(content)
            .Select(m => m.Groups["var"].Value)
            .Distinct()
            .ToList();
        var relPath = Path.GetRelativePath(config.VaultPath, filePath);
        var setResult = $"[ok] Template created: {relPath}";
        return variables.Count == 0
            ? setResult
            : $"{setResult}\n   Variables: {string.Join(", ", variables.Select(v => "{{" + v + "}}"))}";
    }

    private string ListVaultTemplates(string templatesFolder)
    {
        var folder = ResolveTemplatesFolder(templatesFolder);
        if (folder is null)
        {
            return "[info] No templates folder found. Create a 'Templates' folder in your vault to get started. " +
                   "Checked: " + string.Join(", ", TemplateFolderCandidates);
        }

        var templateFiles = Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly).ToList();
        if (templateFiles.Count == 0)
        {
            var relFolder = Path.GetRelativePath(config.VaultPath, folder);
            return $"[info] No templates found in '{relFolder}'. Add .md files to use as templates.";
        }

        var sb = new StringBuilder($"[ok] Found {templateFiles.Count} template(s):\n\n");
        var relFolderName = Path.GetRelativePath(config.VaultPath, folder);
        foreach (var file in templateFiles.OrderBy(f => f))
        {
            var templateName = Path.GetFileNameWithoutExtension(file);
            var content = File.ReadAllText(file);
            var vars = TemplateVarRegex()
                .Matches(content)
                .Select(m => m.Groups["var"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v)
                .ToList();

            sb.Append($"  **{templateName}** ({relFolderName}/{templateName}.md)");
            sb.Append(vars.Count > 0
                ? $" — variables: {string.Join(", ", vars.Select(v => "{{" + v + "}}"))}"
                : " — no variables");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task<string> ManageEngineeringTemplatesAsync(
        string action, string typeKey, string content, bool resetToDefault)
    {
        if (action != "list" && !ProjectWorkspaceService.TemplateKeys.Contains(typeKey, StringComparer.OrdinalIgnoreCase))
        {
            return $"[error] Unknown template type '{typeKey}'. Valid types: {string.Join(", ", ProjectWorkspaceService.TemplateKeys)}.";
        }

        var workspace = new ProjectWorkspaceService(config, vaultConfig, bridge);
        var overridePath = workspace.GetVaultTemplatePath(typeKey);
        var isOverride = overridePath is not null && File.Exists(overridePath);

        if (action == "list")
        {
            var sb = new StringBuilder($"[ok] {ProjectWorkspaceService.TemplateKeys.Length} engineering template(s):\n\n");
            foreach (var key in ProjectWorkspaceService.TemplateKeys)
            {
                var path = workspace.GetVaultTemplatePath(key);
                var hasOverride = path is not null && File.Exists(path);
                var vars = ProjectWorkspaceService.SupportedVariablesFor(key);
                sb.Append($"  **{key}** — ");
                sb.Append(hasOverride ? $"override at {workspace.ToVaultRelative(path!)}" : "using embedded default");
                sb.AppendLine($" — variables: {string.Join(", ", vars.Select(v => "{{" + v + "}}"))}");
            }

            return sb.ToString();
        }

        if (action == "get")
        {
            var effectiveContent = await workspace.ResolveTemplateAsync(typeKey);
            var vars = ProjectWorkspaceService.SupportedVariablesFor(typeKey);
            var result = new StringBuilder($"[ok] Template '{typeKey}' ({(isOverride ? $"override: {workspace.ToVaultRelative(overridePath!)}" : "embedded default")}):\n\n");
            result.AppendLine($"Supported variables: {string.Join(", ", vars.Select(v => "{{" + v + "}}"))}");
            result.AppendLine();
            result.AppendLine("```markdown");
            result.AppendLine(effectiveContent);
            result.AppendLine("```");
            return result.ToString();
        }

        if (resetToDefault)
        {
            if (isOverride)
            {
                File.Delete(overridePath!);
                return $"[ok] Reverted '{typeKey}' to the embedded default (removed {workspace.ToVaultRelative(overridePath!)}).";
            }

            return $"[ok] '{typeKey}' already uses the embedded default (no override to remove).";
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return "[error] The 'content' parameter cannot be empty unless reset_to_default=true.";
        }

        var targetDir = Path.Combine(workspace.ResolveTemplatesFolderOrDefault(), "kioku");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, $"{typeKey}.md");
        await File.WriteAllTextAsync(targetPath, content, NoteHelpers.Utf8NoBom);

        var recognized = new HashSet<string>(ProjectWorkspaceService.SupportedVariablesFor(typeKey), StringComparer.OrdinalIgnoreCase);
        var unknownVars = ProjectWorkspaceService.ExtractTemplateVariableNames(content)
            .Where(v => !recognized.Contains(v))
            .ToList();
        var resultText = $"[ok] Template '{typeKey}' saved: {workspace.ToVaultRelative(targetPath)}";
        return unknownVars.Count == 0
            ? resultText
            : $"{resultText}\n   [warning] not a recognized variable for '{typeKey}' and will be left literal: " +
              string.Join(", ", unknownVars.Select(v => "{{" + v + "}}"));
    }

    private string? ResolveTemplatesFolder(string? overrideFolder)
    {
        if (!string.IsNullOrWhiteSpace(overrideFolder))
        {
            var path = NoteHelpers.EnsureInsideVault(
                config.VaultPath,
                Path.Combine(config.VaultPath, overrideFolder));
            return Directory.Exists(path) ? path : null;
        }

        // folders.templates from .kioku/config.yml wins over the conventional candidates
        var configured = vaultConfig.GetFolder("templates");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var configuredPath = NoteHelpers.EnsureInsideVault(
                config.VaultPath,
                Path.Combine(config.VaultPath, configured));
            if (Directory.Exists(configuredPath))
            {
                return configuredPath;
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

    private string BuildFilePath(string name) => NoteHelpers.BuildFilePath(name, config.VaultPath);

}
