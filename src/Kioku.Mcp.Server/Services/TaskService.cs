using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Domain;
using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Service for parsing, querying, and modifying Markdown task items in an Obsidian vault.
/// Supports the Obsidian Tasks plugin format with emoji date annotations.
/// </summary>
public sealed partial class TaskService(
    ILogger<TaskService> logger,
    KiokuConfiguration config,
    VaultIndexService? vault = null,
    VaultConfigService? vaultConfig = null,
    IVaultMutationService? mutations = null)
{
    // Matches '- [ ] text' (open) and '- [x] text' / '- [X] text' (done)
    [GeneratedRegex(@"^(?<indent>\s*)- \[(?<state>[ xX])\] (?<text>.+)$", RegexOptions.Multiline)]
    private static partial Regex TaskLineRegex();

    // Obsidian Tasks plugin date emojis
    // 📅 due  ⏳ scheduled  🛫 start
    [GeneratedRegex(@"📅\s*(\d{4}-\d{2}-\d{2})")]
    private static partial Regex DueDateRegex();

    [GeneratedRegex(@"⏳\s*(\d{4}-\d{2}-\d{2})")]
    private static partial Regex ScheduledDateRegex();

    [GeneratedRegex(@"🛫\s*(\d{4}-\d{2}-\d{2})")]
    private static partial Regex StartDateRegex();

    // Inline tags like #project or #urgent (not at the start of a word preceded by [[)
    [GeneratedRegex(@"(?<!\[)#([A-Za-z0-9_\-/]+)")]
    private static partial Regex InlineTagRegex();

    // Date emoji pattern to strip from display text
    [GeneratedRegex(@"[📅⏳🛫✅]\s*\d{4}-\d{2}-\d{2}")]
    private static partial Regex DateEmojiRegex();

    private readonly string _vaultPath = config.VaultPath;

    /// <summary>
    /// Parses all tasks from a specific note file.
    /// </summary>
    /// <param name="filePath">Absolute path to the .md file.</param>
    /// <param name="vaultRelativePath">Vault-relative path (for display).</param>
    /// <param name="noteName">Note name without extension.</param>
    /// <returns>All task items found in the file.</returns>
    public async Task<IReadOnlyList<TaskItem>> ParseTasksFromFileAsync(
        string filePath,
        string vaultRelativePath,
        string noteName)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(filePath);
        }
        catch (Exception ex)
        {
            logger.Warn("Could not read file for task parsing: {Path} — {Message}", filePath, ex.Message);
            return [];
        }

        return ParseTasksFromContent(content, filePath, vaultRelativePath, noteName);
    }

    /// <summary>
    /// Scans all indexed notes in the vault and extracts all task items.
    /// </summary>
    /// <param name="folderFilter">Optional vault-relative folder to restrict the scan.</param>
    /// <returns>All task items found across the vault (or folder).</returns>
    public async Task<IReadOnlyList<TaskItem>> GetAllTasksAsync(string? folderFilter = null)
    {
        if (vault is null)
        {
            return [];
        }

        var tasks = new List<TaskItem>();
        IEnumerable<Note> notes;
        if (string.IsNullOrWhiteSpace(folderFilter))
        {
            notes = vault.GetAllNotes();
        }
        else
        {
            var folderPath = NoteHelpers.EnsureInsideVault(
                _vaultPath,
                Path.Combine(_vaultPath, folderFilter));
            notes = vault.GetNotesInFolder(folderPath);
        }

        foreach (var note in notes.OrderBy(n => n.VaultRelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var noteTasks = await ParseTasksFromFileAsync(note.FilePath, note.VaultRelativePath, note.Name);
            tasks.AddRange(noteTasks);
        }

        return tasks;
    }

    /// <summary>
    /// Marks a specific task as completed or reopens it by editing the file in-place.
    /// Locates the task by note name and 1-based line number.
    /// </summary>
    /// <param name="filePath">Absolute path to the note.</param>
    /// <param name="lineNumber">1-based line number of the task.</param>
    /// <param name="complete">True to mark complete ('- [x]'), false to reopen ('- [ ]').</param>
    /// <returns>The updated task item, or null if the line was not a valid task.</returns>
    public async Task<TaskItem?> SetTaskCompletionAsync(
        string filePath,
        int lineNumber,
        bool complete,
        VaultMutationPreconditions? preconditions = null)
    {
        var lines = (await File.ReadAllLinesAsync(filePath)).ToList();

        if (lineNumber < 1 || lineNumber > lines.Count)
        {
            logger.Warn("Line number {Line} is out of range for file {Path}", lineNumber, filePath);
            return null;
        }

        var zeroIndex = lineNumber - 1;
        var line = lines[zeroIndex];
        var match = TaskLineRegex().Match(line);

        if (!match.Success)
        {
            logger.Warn("Line {Line} in {Path} is not a task: {Content}", lineNumber, filePath, line);
            return null;
        }

        var newState = complete ? "x" : " ";
        var updatedLine = $"{match.Groups["indent"].Value}- [{newState}] {match.Groups["text"].Value}";
        lines[zeroIndex] = updatedLine;

        var updatedContent = string.Join(Environment.NewLine, lines);
        updatedContent = NoteHelpers.TouchUpdated(
            updatedContent, DateOnly.FromDateTime(DateTime.Today), vaultConfig?.MaintainUpdated == true);
        if (mutations is null)
        {
            await File.WriteAllTextAsync(filePath, updatedContent, NoteHelpers.Utf8NoBom);
        }
        else
        {
            await mutations.WriteTextAsync(filePath, updatedContent, preconditions);
        }
        logger.Info("Task at line {Line} in {Path} marked as {State}", lineNumber, filePath, complete ? "complete" : "open");

        var vaultRelative = Path.GetRelativePath(_vaultPath, filePath).Replace('\\', '/');
        var noteName = Path.GetFileNameWithoutExtension(filePath);

        var parsed = ParseTasksFromContent(updatedLine, filePath, vaultRelative, noteName, startLine: lineNumber);
        return parsed.Count == 0 ? null : parsed[0];
    }

    // Internal parsing helpers

    private static List<TaskItem> ParseTasksFromContent(
        string content,
        string filePath,
        string vaultRelativePath,
        string noteName,
        int startLine = 1)
    {
        var tasks = new List<TaskItem>();
        var lines = content.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var match = TaskLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var rawText = match.Groups["text"].Value;
            var isCompleted = match.Groups["state"].Value is "x" or "X";

            tasks.Add(new TaskItem
            {
                FilePath = filePath,
                VaultRelativePath = vaultRelativePath,
                NoteName = noteName,
                LineNumber = startLine + i,
                RawLine = line,
                Text = ExtractCleanText(rawText),
                IsCompleted = isCompleted,
                DueDate = ParseDate(DueDateRegex(), rawText),
                ScheduledDate = ParseDate(ScheduledDateRegex(), rawText),
                StartDate = ParseDate(StartDateRegex(), rawText),
                InlineTags = ExtractInlineTags(rawText),
            });
        }

        return tasks;
    }

    private static string ExtractCleanText(string rawText)
    {
        // Remove date emoji annotations from display text
        var clean = DateEmojiRegex().Replace(rawText, "").Trim();
        return clean;
    }

    private static DateOnly? ParseDate(Regex regex, string text)
    {
        var match = regex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        return DateOnly.TryParse(match.Groups[1].Value, out var date) ? date : null;
    }

    private static List<string> ExtractInlineTags(string text)
    {
        return InlineTagRegex()
            .Matches(text)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
