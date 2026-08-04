using System.ComponentModel;
using System.Text;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for managing Markdown task items across the Obsidian vault.
/// Supports the Obsidian Tasks plugin format with emoji date annotations.
/// </summary>
[McpServerToolType]
public sealed class TaskManagementTools(VaultIndexService vault, TaskService tasks)
{
    // list_tasks

    [McpServerTool, Description(
        "Lists all tasks (open and completed) across the vault or within a specific note. " +
        "Supports filtering by completion status, tag, and overdue date. " +
        "Supports pagination with stable ordering and returns task text, note name, line number, " +
        "due date, and inline tags.")]
    public async Task<string> list_tasks(
        [Description("Name or path of a specific note to scan. Leave empty to scan the entire vault.")] string note = "",
        [Description("Filter by completion status: 'open' (default), 'done', or 'all'.")] string status = "open",
        [Description("Folder to restrict the search (relative to vault root). Only used when 'note' is empty.")] string folder = "",
        [Description("Optional tag to match in task text or note frontmatter, without the '#' prefix.")] string tag = "",
        [Description("Only return open tasks whose due date is in the past.")] bool overdue_only = false,
        [Description("Maximum tasks to return (default: 50).")] int limit = 50,
        [Description("Number of matching tasks to skip for pagination.")] int offset = 0)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (offset < 0)
        {
            return KiokuError.InvalidArgument("'offset' must be 0 or greater.");
        }

        if (limit <= 0)
        {
            return KiokuError.InvalidArgument("'limit' must be greater than 0.");
        }

        limit = Math.Min(limit, 50);

        var normalizedStatus = status?.Trim().ToLowerInvariant() ?? "";
        if (normalizedStatus is not ("open" or "done" or "completed" or "all"))
        {
            return KiokuError.InvalidArgument($"Invalid status '{status}'. Use 'open', 'done', or 'all'.");
        }

        IReadOnlyList<TaskItem> allTasks;

        if (!string.IsNullOrWhiteSpace(note))
        {
            var found = ResolveNote(note);
            if (found is null)
            {
                return KiokuError.NotFound($"Note not found: '{note}'. Use list_notes to see available notes.");
            }

            allTasks = await tasks.ParseTasksFromFileAsync(found.FilePath, found.VaultRelativePath, found.Name);
        }
        else
        {
            try
            {
                allTasks = await tasks.GetAllTasksAsync(string.IsNullOrWhiteSpace(folder) ? null : folder);
            }
            catch (InvalidOperationException)
            {
                return KiokuError.InvalidArgument("The 'folder' parameter must resolve inside the vault.");
            }
        }

        IEnumerable<TaskItem> filtered = normalizedStatus switch
        {
            "done" or "completed" => allTasks.Where(t => t.IsCompleted),
            "all" => allTasks,
            _ => allTasks.Where(t => !t.IsCompleted),
        };

        var tagValue = tag?.Trim() ?? "";
        if (tagValue.Length > 0)
        {
            var taggedNotes = vault.GetAllNotes()
                .Where(n => n.Metadata.Tags.Any(t => t.Equals(tagValue, StringComparison.OrdinalIgnoreCase)))
                .Select(n => n.FilePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            filtered = filtered.Where(t =>
                t.InlineTags.Any(it => it.Equals(tagValue, StringComparison.OrdinalIgnoreCase)) ||
                taggedNotes.Contains(t.FilePath));
        }

        if (overdue_only)
        {
            filtered = filtered.Where(t => t.IsOverdue);
        }

        var sorted = (overdue_only
                ? filtered.OrderBy(t => t.DueDate).ThenBy(t => t.VaultRelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.LineNumber)
                : filtered.OrderBy(t => t.VaultRelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.LineNumber))
            .ToList();
        var total = sorted.Count;
        var page = sorted.Skip(offset).Take(limit).ToList();
        var pageMetadata = $"total: {total}, offset: {offset}, limit: {limit}, returned: {page.Count}";

        if (page.Count == 0)
        {
            if (overdue_only)
            {
                return $"No overdue tasks found. All caught up! ({pageMetadata})";
            }

            if (tagValue.Length > 0)
            {
                return $"No {(normalizedStatus == "all" ? "" : status + " ")}tasks found with tag '#{tagValue}'. ({pageMetadata})";
            }

            return (string.IsNullOrWhiteSpace(note)
                ? $"No {(normalizedStatus == "all" ? "" : status + " ")}tasks found in the vault."
                : $"No {(normalizedStatus == "all" ? "" : status + " ")}tasks found in '{note}'.") +
                $" ({pageMetadata})";
        }

        if (overdue_only)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Overdue tasks as of {today:yyyy-MM-dd} ({pageMetadata}):\n");
            sb.Append(FormatTaskList(page, showDueDate: true, highlightOverdue: true));
            return sb.ToString();
        }

        if (tagValue.Length > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Tasks with tag '#{tagValue}' ({pageMetadata}):\n");
            sb.Append(FormatTaskList(page));
            return sb.ToString();
        }

        return $"Tasks ({pageMetadata}):\n\n{FormatTaskList(page)}";
    }

    // set_task_state

    [McpServerTool, Description(
        "Sets a task's completion state at the specified line in a note. " +
        "Use list_tasks first to find the note name and line number of the task.")]
    public async Task<string> set_task_state(
        [Description("Name or path of the note containing the task.")] string note,
        [Description("1-based line number of the task within the note.")] int line_number,
        [Description("True to mark the task complete ('- [x]'); false to reopen it ('- [ ]').")] bool completed,
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the resource, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "")
    {
        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'.");
        }

        var result = await tasks.SetTaskCompletionAsync(
            found.FilePath,
            line_number,
            completed,
            VaultMutationPreconditions.FromToolArguments(
                expected_revision,
                expected_hash,
                claim_id,
                fence_generation,
                resource_key,
                mutation_id));

        if (result is null)
        {
            var hint = completed
                ? "Use list_tasks to find task line numbers."
                : "Use list_tasks with status='done' to find completed task line numbers.";
            return KiokuError.InvalidArgument($"Line {line_number} in '{note}' is not a valid task. {hint}");
        }

        return completed
            ? $"[ok] Task marked as complete in '{found.VaultRelativePath}' (line {line_number}):\n" +
              $"  ☑ {result.Text}"
            : $"[ok] Task reopened in '{found.VaultRelativePath}' (line {line_number}):\n" +
              $"  ☐ {result.Text}";
    }

    // Private helpers

    private Note? ResolveNote(string noteInput) => NoteHelpers.ResolveNote(noteInput, vault);

    private static string FormatTaskList(
        IReadOnlyList<TaskItem> taskList,
        bool showDueDate = false,
        bool highlightOverdue = false)
    {
        var sb = new StringBuilder();
        string? currentNote = null;

        foreach (var task in taskList)
        {
            if (task.VaultRelativePath != currentNote)
            {
                currentNote = task.VaultRelativePath;
                sb.AppendLine(CultureInfo.InvariantCulture, $"\n📄 {currentNote}");
            }

            var checkbox = task.IsCompleted ? "[x]" : "[ ]";
            var lineRef = $"L{task.LineNumber}";

            var extras = new List<string>();

            if (task.DueDate.HasValue)
            {
                var duePart = $"📅 {task.DueDate:yyyy-MM-dd}";
                if (highlightOverdue && task.IsOverdue)
                {
                    duePart += " ⚠ OVERDUE";
                }

                extras.Add(duePart);
            }

            if (showDueDate && task.ScheduledDate.HasValue)
            {
                extras.Add($"⏳ {task.ScheduledDate:yyyy-MM-dd}");
            }

            if (task.InlineTags.Count > 0)
            {
                extras.Add(string.Join(" ", task.InlineTags.Select(t => $"#{t}")));
            }

            var extraStr = extras.Count > 0 ? $"  ({string.Join(" | ", extras)})" : "";
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {checkbox} {task.Text}{extraStr}  [{lineRef}]");
        }

        return sb.ToString().TrimStart('\n');
    }
}
