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
        "Supports filtering by completion status. " +
        "Returns task text, note name, line number, due date, and inline tags.")]
    public async Task<string> list_tasks(
        [Description("Name or path of a specific note to scan. Leave empty to scan the entire vault.")] string note = "",
        [Description("Filter by completion status: 'open' (default), 'done', or 'all'.")] string status = "open",
        [Description("Folder to restrict the search (relative to vault root). Only used when 'note' is empty.")] string folder = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        IReadOnlyList<TaskItem> allTasks;

        if (!string.IsNullOrWhiteSpace(note))
        {
            var found = ResolveNote(note);
            if (found is null)
            {
                return $"[error] Note not found: '{note}'. Use list_notes to see available notes.";
            }

            allTasks = await tasks.ParseTasksFromFileAsync(found.FilePath, found.VaultRelativePath, found.Name);
        }
        else
        {
            allTasks = await tasks.GetAllTasksAsync(string.IsNullOrWhiteSpace(folder) ? null : folder);
        }

        var filtered = status.ToLowerInvariant() switch
        {
            "done" or "completed" => allTasks.Where(t => t.IsCompleted),
            "all" => allTasks,
            _ => allTasks.Where(t => !t.IsCompleted),
        };

        var list = filtered.OrderBy(t => t.VaultRelativePath).ThenBy(t => t.LineNumber).ToList();

        if (list.Count == 0)
        {
            return string.IsNullOrWhiteSpace(note)
                ? $"No {(status == "all" ? "" : status + " ")}tasks found in the vault."
                : $"No {(status == "all" ? "" : status + " ")}tasks found in '{note}'.";
        }

        return FormatTaskList(list);
    }

    // complete_task

    [McpServerTool, Description(
        "Marks a task as completed ('- [x]') at the specified line in a note. " +
        "Use list_tasks first to find the note name and line number of the task.")]
    public async Task<string> complete_task(
        [Description("Name or path of the note containing the task.")] string note,
        [Description("1-based line number of the task within the note.")] int line_number)
    {
        var found = ResolveNote(note);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'.";
        }

        var result = await tasks.SetTaskCompletionAsync(found.FilePath, line_number, complete: true);

        if (result is null)
        {
            return $"[error] Line {line_number} in '{note}' is not a valid task. Use list_tasks to find task line numbers.";
        }

        return $"[ok] Task marked as complete in '{found.VaultRelativePath}' (line {line_number}):\n" +
               $"  ☑ {result.Text}";
    }

    // reopen_task

    [McpServerTool, Description(
        "Reopens a completed task by changing '- [x]' back to '- [ ]'. " +
        "Use list_tasks with status='done' first to find the note and line number.")]
    public async Task<string> reopen_task(
        [Description("Name or path of the note containing the task.")] string note,
        [Description("1-based line number of the task within the note.")] int line_number)
    {
        var found = ResolveNote(note);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'.";
        }

        var result = await tasks.SetTaskCompletionAsync(found.FilePath, line_number, complete: false);

        if (result is null)
        {
            return $"[error] Line {line_number} in '{note}' is not a valid task. Use list_tasks with status='done' to find completed task line numbers.";
        }

        return $"[ok] Task reopened in '{found.VaultRelativePath}' (line {line_number}):\n" +
               $"  ☐ {result.Text}";
    }

    // list_tasks_by_tag

    [McpServerTool, Description(
        "Lists all open tasks that match a given tag. " +
        "Matches both frontmatter tags of the note and inline '#tag' annotations in the task text. " +
        "Returns task text, note, line number, and due date.")]
    public async Task<string> list_tasks_by_tag(
        [Description("Tag to filter by (without the '#' prefix). E.g. 'project', 'urgent'.")] string tag,
        [Description("Include completed tasks too (default: false — open tasks only).")] bool include_done = false)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            return "[error] 'tag' parameter is required.";
        }

        var allTasks = await tasks.GetAllTasksAsync();

        // Match tasks that have the tag inline OR whose note has the tag in its frontmatter
        var taggedNotes = vault.GetAllNotes()
            .Where(n => n.Metadata.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            .Select(n => n.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matched = allTasks
            .Where(t =>
                (include_done || !t.IsCompleted) &&
                (t.InlineTags.Any(it => it.Equals(tag, StringComparison.OrdinalIgnoreCase)) ||
                 taggedNotes.Contains(t.FilePath)))
            .OrderBy(t => t.VaultRelativePath)
            .ThenBy(t => t.LineNumber)
            .ToList();

        if (matched.Count == 0)
        {
            return $"No {(include_done ? "" : "open ")}tasks found with tag '#{tag}'.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Tasks with tag '#{tag}' ({matched.Count} found):\n");
        sb.Append(FormatTaskList(matched));
        return sb.ToString();
    }

    // list_overdue_tasks

    [McpServerTool, Description(
        "Lists all open tasks whose due date (📅 YYYY-MM-DD in Obsidian Tasks format) is in the past. " +
        "Only scans open tasks — completed tasks are never overdue.")]
    public async Task<string> list_overdue_tasks(
        [Description("Folder to restrict the search (relative to vault root). Leave empty to scan the entire vault.")] string folder = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var allTasks = await tasks.GetAllTasksAsync(string.IsNullOrWhiteSpace(folder) ? null : folder);

        var overdue = allTasks
            .Where(t => t.IsOverdue)
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.VaultRelativePath)
            .ToList();

        if (overdue.Count == 0)
        {
            return "No overdue tasks found. All caught up!";
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var sb = new StringBuilder();
        sb.AppendLine($"Overdue tasks as of {today:yyyy-MM-dd} ({overdue.Count} found):\n");
        sb.Append(FormatTaskList(overdue, showDueDate: true, highlightOverdue: true));
        return sb.ToString();
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
                sb.AppendLine($"\n📄 {currentNote}");
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
            sb.AppendLine($"  {checkbox} {task.Text}{extraStr}  [{lineRef}]");
        }

        return sb.ToString().TrimStart('\n');
    }
}
