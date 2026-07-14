using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for high-level workflow operations: template-based note creation
/// and action item extraction from note content.
/// </summary>
[McpServerToolType]
public sealed partial class WorkflowTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    TaskService tasks,
    VaultConfigService vaultConfig,
    GenerationService generation,
    ObsidianBridgeService bridge)
{
    private static readonly string[] TemplateFolderCandidates =
        ["Templates", "99_System/Templates", "_templates", "System/Templates"];

    // Matches {{ variable }} or {{variable}} Mustache/Handlebars syntax
    [GeneratedRegex(@"\{\{\s*(?<var>[a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}")]
    private static partial Regex TemplateVarRegex();

    // Matches Markdown task checkboxes: "- [ ] text" or "- [x] text"
    [GeneratedRegex(@"^(?<indent>\s*)- \[(?<state>[ xX])\] (?<text>.+)$", RegexOptions.Multiline)]
    private static partial Regex CheckboxRegex();

    // list_templates

    [McpServerTool, Description(
        "Lists all available note templates in the vault's templates folder. " +
        "Returns template names and the variables they accept ({{ variable }} syntax).")]
    public Task<string> list_templates(
        [Description("Templates folder relative to vault root. Leave empty to auto-detect.")] string templates_folder = "")
    {
        var folder = ResolveTemplatesFolder(templates_folder);
        if (folder is null)
        {
            return Task.FromResult(
                "[info] No templates folder found. Create a 'Templates' folder in your vault to get started. " +
                "Checked: " + string.Join(", ", TemplateFolderCandidates));
        }

        var templateFiles = Directory.EnumerateFiles(folder, "*.md", SearchOption.TopDirectoryOnly).ToList();
        if (templateFiles.Count == 0)
        {
            var relFolder = Path.GetRelativePath(config.VaultPath, folder);
            return Task.FromResult($"[info] No templates found in '{relFolder}'. Add .md files to use as templates.");
        }

        var sb = new StringBuilder($"[ok] Found {templateFiles.Count} template(s):\n\n");
        var relFolderName = Path.GetRelativePath(config.VaultPath, folder);

        foreach (var file in templateFiles.OrderBy(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var content = File.ReadAllText(file);
            var vars = TemplateVarRegex()
                .Matches(content)
                .Select(m => m.Groups["var"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v)
                .ToList();

            sb.Append($"  **{name}** ({relFolderName}/{name}.md)");
            if (vars.Count > 0)
            {
                sb.Append($" — variables: {string.Join(", ", vars.Select(v => "{{" + v + "}}"))}");
            }
            else
            {
                sb.Append(" — no variables");
            }

            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    // create_note_from_template

    [McpServerTool, Description(
        "Creates a new note by applying a template with variable substitution. " +
        "Replaces {{ variable }} placeholders with the provided values. " +
        "Built-in variables: {{date}} (today), {{time}} (now), {{title}} (note name).")]
    public async Task<string> create_note_from_template(
        [Description("Name of the template (without .md extension). Use list_templates to see available templates.")] string template_name,
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
            return $"[error] Template not found: '{template_name}'. Use list_templates to see available templates.";
        }

        // Resolve target file path
        var targetFilePath = BuildFilePath(target_path);
        if (File.Exists(targetFilePath))
        {
            return $"[error] Note already exists: '{target_path}'. Use update_note_content to modify it.";
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
        await File.WriteAllTextAsync(targetFilePath, rendered, Encoding.UTF8);

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

    // create_template

    [McpServerTool, Description(
        "Creates a new template file in the vault's templates folder. " +
        "Use {{ variable }} syntax for placeholders that will be filled when the template is used.")]
    public async Task<string> create_template(
        [Description("Template name (without .md extension).")] string name,
        [Description("Template content in Markdown. Use {{ variable }} for placeholders.")] string content,
        [Description("Templates folder relative to vault root. Leave empty to auto-detect (defaults to 'Templates').")] string templates_folder = "")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "[error] Template name cannot be empty.";
        }

        var folder = ResolveTemplatesFolder(templates_folder) ??
                     NoteHelpers.EnsureInsideVault(
                         config.VaultPath,
                         Path.Combine(config.VaultPath, TemplateFolderCandidates[0]));

        Directory.CreateDirectory(folder);

        var filePath = Path.Combine(folder, name + ".md");
        if (File.Exists(filePath))
        {
            return $"[error] Template '{name}' already exists. Delete it first or use a different name.";
        }

        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);

        var vars = TemplateVarRegex()
            .Matches(content)
            .Select(m => m.Groups["var"].Value)
            .Distinct()
            .ToList();

        var relPath = Path.GetRelativePath(config.VaultPath, filePath);
        var result = $"[ok] Template created: {relPath}";
        if (vars.Count > 0)
        {
            result += $"\n   Variables: {string.Join(", ", vars.Select(v => "{{" + v + "}}"))}";
        }

        return result;
    }

    // extract_action_items

    [McpServerTool, Description(
        "Extracts all unchecked task checkboxes from a note and optionally consolidates them " +
        "into a new action items note. Returns the found tasks even in dry-run mode.")]
    public async Task<string> extract_action_items(
        [Description("Name or path of the note to scan for action items.")] string note,
        [Description("If provided, creates a new note at this path containing the extracted action items.")] string output_note = "",
        [Description("If true, only reports found action items without creating the output note.")] bool dry_run = false)
    {
        var found = NoteHelpers.ResolveNote(note, vault);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'";
        }

        var rawContent = await File.ReadAllTextAsync(found.FilePath, Encoding.UTF8);
        var openTasks = CheckboxRegex()
            .Matches(rawContent)
            .Where(m => m.Groups["state"].Value == " ")
            .Select(m => m.Groups["text"].Value.Trim())
            .ToList();

        if (openTasks.Count == 0)
        {
            return $"[info] No open action items found in '{found.Name}'.";
        }

        var sb = new StringBuilder($"[ok] Found {openTasks.Count} open action item(s) in '{found.Name}':\n\n");
        foreach (var task in openTasks)
        {
            sb.AppendLine($"  - [ ] {task}");
        }

        if (dry_run || string.IsNullOrWhiteSpace(output_note))
        {
            return sb.ToString();
        }

        // Build output note content
        var noteSb = new StringBuilder();
        noteSb.AppendLine("---");
        noteSb.AppendLine("tags:");
        noteSb.AppendLine("  - action-items");
        noteSb.AppendLine("  - tasks");
        noteSb.AppendLine($"type: note");
        noteSb.AppendLine($"status: draft");
        noteSb.AppendLine($"date: {DateTime.Now:yyyy-MM-dd}");
        noteSb.AppendLine("---");
        noteSb.AppendLine();
        noteSb.AppendLine($"# Action Items — {found.Name}");
        noteSb.AppendLine();
        noteSb.AppendLine($"> Extracted from [[{found.Name}]] on {DateTime.Now:yyyy-MM-dd HH:mm}");
        noteSb.AppendLine();

        foreach (var task in openTasks)
        {
            noteSb.AppendLine($"- [ ] {task}");
        }

        var outputFilePath = BuildFilePath(output_note);
        if (File.Exists(outputFilePath))
        {
            return $"[error] Output note already exists: '{output_note}'. Use a different path or delete it first.";
        }

        var outputDir = Path.GetDirectoryName(outputFilePath)!;
        Directory.CreateDirectory(outputDir);
        await File.WriteAllTextAsync(outputFilePath, noteSb.ToString(), Encoding.UTF8);

        var relPath = Path.GetRelativePath(config.VaultPath, outputFilePath);
        sb.AppendLine();
        sb.Append($"[ok] Action items saved to: {relPath}");

        return sb.ToString();
    }

    // generate_digest

    [McpServerTool, Description(
        "Generates a digest note summarizing recent vault activity: notes created or modified, " +
        "overdue and upcoming tasks, newly orphaned notes, and draft/inbox notes awaiting review. " +
        "period='day' (default) covers today since local midnight; period='week' covers the last " +
        "7 days. Written as 'Digest {yyyy-MM-dd}.md' in the 'daily' folder (folders.daily in " +
        ".kioku/config.yml, falling back to target_folder, then the vault root) — re-running on " +
        "the same day replaces the note, since it's fully regenerated each time. If local " +
        "generation (KIOKU_GEN_MODEL) is available, adds a short AI-generated Summary section; " +
        "otherwise the digest is purely structural. Set dry_run=true to preview the markdown " +
        "without writing anything.")]
    public async Task<string> generate_digest(
        [Description("Digest period: 'day' (default, since local midnight) or 'week' (last 7 days).")] string period = "day",
        [Description("Destination folder (relative to vault root) used only if folders.daily isn't configured. Leave empty for the vault root.")] string target_folder = "",
        [Description("If true, returns the digest markdown without writing any file.")] bool dry_run = false)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var isWeek = period.Trim().Equals("week", StringComparison.OrdinalIgnoreCase);
        var periodDays = isWeek ? 7 : 1;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var periodStart = DateTime.Now.Date.AddDays(-(periodDays - 1));

        var recentNotes = vault.GetAllNotes()
            .Where(n => n.LastModified.LocalDateTime >= periodStart)
            .OrderByDescending(n => n.LastModified)
            .ToList();

        var allTasks = await tasks.GetAllTasksAsync();
        var overdueTasks = allTasks.Where(t => t.IsOverdue).OrderBy(t => t.DueDate).ToList();
        var dueSoonTasks = allTasks
            .Where(t => !t.IsCompleted && t.DueDate.HasValue &&
                        t.DueDate.Value >= today && t.DueDate.Value <= today.AddDays(periodDays))
            .OrderBy(t => t.DueDate)
            .ToList();

        var orphanNotes = recentNotes
            .Where(n => !n.OutgoingLinks.Any() && !vault.GetBacklinks(n.Name).Any())
            .ToList();

        var reviewNotes = recentNotes
            .Where(n => n.Metadata.Status is not null &&
                        (n.Metadata.Status.Equals("draft", StringComparison.OrdinalIgnoreCase) ||
                         n.Metadata.Status.Equals("inbox", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var aiSummary = await GenerateDigestSummaryAsync(recentNotes);

        var markdown = BuildDigestMarkdown(
            isWeek, periodStart, today, recentNotes, overdueTasks, dueSoonTasks, orphanNotes, reviewNotes, aiSummary);

        if (dry_run)
        {
            return $"[info] Dry run — digest not written.\n\n{markdown}";
        }

        var folder = vaultConfig.GetFolder("daily")
            ?? (string.IsNullOrWhiteSpace(target_folder) ? null : target_folder);
        var folderPath = string.IsNullOrWhiteSpace(folder)
            ? config.VaultPath
            : NoteHelpers.EnsureInsideVault(config.VaultPath, Path.Combine(config.VaultPath, folder));
        Directory.CreateDirectory(folderPath);

        var fileName = $"Digest {today:yyyy-MM-dd}.md";
        var filePath = Path.Combine(folderPath, fileName);
        var replaced = File.Exists(filePath);

        var frontmatter = NoteHelpers.BuildFrontmatter(tags: ["digest"], type: "log", status: null, date: today);
        await File.WriteAllTextAsync(filePath, frontmatter + "\n" + markdown, Encoding.UTF8);

        var relPath = Path.GetRelativePath(config.VaultPath, filePath);
        return $"[ok] Digest {(replaced ? "regenerated" : "generated")}: {relPath}";
    }

    // Private helpers

    private async Task<string?> GenerateDigestSummaryAsync(IReadOnlyList<Note> recentNotes)
    {
        if (!generation.IsAvailable || recentNotes.Count == 0)
        {
            return null;
        }

        var snippets = recentNotes
            .Take(15)
            .Select(n => $"- {n.Name}: {Truncate(n.PlainText, 200)}");

        var prompt = "Summarize the recent vault activity below in 3-4 short lines, highlighting " +
                     "themes and notable items. Plain prose, no headings or bullet points.\n\n" +
                     string.Join("\n", snippets);

        return await generation.GenerateAsync(prompt);
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";

    private static string BuildDigestMarkdown(
        bool isWeek,
        DateTime periodStart,
        DateOnly today,
        IReadOnlyList<Note> recentNotes,
        IReadOnlyList<TaskItem> overdueTasks,
        IReadOnlyList<TaskItem> dueSoonTasks,
        IReadOnlyList<Note> orphanNotes,
        IReadOnlyList<Note> reviewNotes,
        string? aiSummary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {(isWeek ? "Weekly" : "Daily")} Digest — {today:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine(isWeek
            ? $"> Covers the last 7 days ({DateOnly.FromDateTime(periodStart):yyyy-MM-dd} to {today:yyyy-MM-dd})."
            : $"> Covers today, since local midnight ({periodStart:yyyy-MM-dd HH:mm}).");
        sb.AppendLine();

        if (aiSummary is not null)
        {
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine(aiSummary.Trim());
            sb.AppendLine();
        }

        AppendSection(sb, "Activity", 2, recentNotes
            .Select(n => $"- [[{n.Name}]] (modified: {n.LastModified.LocalDateTime:yyyy-MM-dd HH:mm})"));

        sb.AppendLine("## Tasks");
        sb.AppendLine();
        AppendSection(sb, "Overdue", 3, overdueTasks
            .Select(t => $"- [ ] {t.Text} (due: {t.DueDate:yyyy-MM-dd}) — [[{t.NoteName}]]"));
        AppendSection(sb, "Due soon", 3, dueSoonTasks
            .Select(t => $"- [ ] {t.Text} (due: {t.DueDate:yyyy-MM-dd}) — [[{t.NoteName}]]"));

        AppendSection(sb, "New orphaned notes", 2, orphanNotes
            .Select(n => $"- [[{n.Name}]]"));

        AppendSection(sb, "To review", 2, reviewNotes
            .Select(n => $"- [[{n.Name}]] (status: {n.Metadata.Status})"));

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string heading, int level, IEnumerable<string> lines)
    {
        sb.AppendLine($"{new string('#', level)} {heading}");
        sb.AppendLine();

        var items = lines.ToList();
        if (items.Count == 0)
        {
            sb.AppendLine("_Nothing to report._");
        }
        else
        {
            foreach (var line in items)
            {
                sb.AppendLine(line);
            }
        }

        sb.AppendLine();
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
