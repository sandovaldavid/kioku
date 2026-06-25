namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Represents a Markdown task item parsed from an Obsidian note.
/// Supports the Obsidian Tasks plugin format with emoji date annotations.
/// </summary>
public sealed class TaskItem
{
    /// <summary>Absolute path to the note file that contains this task.</summary>
    public required string FilePath { get; init; }

    /// <summary>Vault-relative path of the note containing this task.</summary>
    public required string VaultRelativePath { get; init; }

    /// <summary>Name of the note (without extension) that contains this task.</summary>
    public required string NoteName { get; init; }

    /// <summary>1-based line number within the note where the task appears.</summary>
    public required int LineNumber { get; init; }

    /// <summary>Full raw text of the task line (e.g., '- [ ] Do something 📅 2025-01-15').</summary>
    public required string RawLine { get; init; }

    /// <summary>The text content of the task, stripped of Markdown task syntax.</summary>
    public required string Text { get; init; }

    /// <summary>Whether the task has been completed ('- [x]' or '- [X]').</summary>
    public required bool IsCompleted { get; init; }

    /// <summary>
    /// Due date parsed from the Obsidian Tasks plugin emoji format (📅 YYYY-MM-DD).
    /// Null if no due date annotation is present.
    /// </summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>
    /// Scheduled date parsed from the Obsidian Tasks plugin emoji format (⏳ YYYY-MM-DD).
    /// Null if no scheduled date annotation is present.
    /// </summary>
    public DateOnly? ScheduledDate { get; init; }

    /// <summary>
    /// Start date parsed from the Obsidian Tasks plugin emoji format (🛫 YYYY-MM-DD).
    /// Null if no start date annotation is present.
    /// </summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>
    /// Inline tags found in the task text (e.g., '#project', '#urgent').
    /// Excludes the '#' prefix.
    /// </summary>
    public IReadOnlyList<string> InlineTags { get; init; } = [];

    /// <summary>Indicates whether the task has a due date that is in the past (and is not completed).</summary>
    public bool IsOverdue =>
        !IsCompleted &&
        DueDate.HasValue &&
        DueDate.Value < DateOnly.FromDateTime(DateTime.Today);
}
