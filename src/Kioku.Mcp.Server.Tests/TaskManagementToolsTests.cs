using System.Text;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class TaskManagementToolsTests : IAsyncLifetime
{
    private string _vaultPath = null!;
    private VaultIndexService _index = null!;
    private TaskManagementTools _tools = null!;

    public async Task InitializeAsync()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), $"kioku-task-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_vaultPath);

        var config = new KiokuConfiguration { VaultPath = _vaultPath };
        _index = new VaultIndexService(NullLogger<VaultIndexService>.Instance, config);
        var taskService = new TaskService(NullLogger<TaskService>.Instance, config);
        _tools = new TaskManagementTools(_index, taskService);

        var yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
        var tomorrow = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        await WriteNoteAsync("Tasks/Planning", ["project"], $"""
            - [ ] Frontmatter task
            - [ ] Inline task #urgent
            - [x] Completed task #project
            - [ ] Overdue task 📅 {yesterday:yyyy-MM-dd}
            - [ ] Future task 📅 {tomorrow:yyyy-MM-dd}
            """);
        await WriteNoteAsync("State", [], "- [ ] Change me");

        await _index.RebuildIndexAsync();
    }

    public Task DisposeAsync()
    {
        _index.Dispose();
        if (Directory.Exists(_vaultPath))
        {
            Directory.Delete(_vaultPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ListTasks_FiltersByStatusAndFolder()
    {
        var result = await _tools.list_tasks(status: "done", folder: "Tasks");

        Assert.Contains("Completed task", result);
        Assert.DoesNotContain("Frontmatter task", result);
        Assert.DoesNotContain("State", result);
    }

    [Fact]
    public async Task ListTasks_TagMatchesFrontmatterAndInlineTags()
    {
        var frontmatterResult = await _tools.list_tasks(tag: "project");
        var inlineResult = await _tools.list_tasks(tag: "URGENT");

        Assert.Contains("Frontmatter task", frontmatterResult);
        Assert.DoesNotContain("Completed task", frontmatterResult);
        Assert.Contains("Inline task", inlineResult);
        Assert.DoesNotContain("Frontmatter task", inlineResult);
    }

    [Fact]
    public async Task ListTasks_InvalidStatusReturnsError()
    {
        var result = await _tools.list_tasks(status: "pending");

        Assert.StartsWith("[error] Invalid status 'pending'.", result);
    }

    [Fact]
    public async Task ListTasks_OverdueOnlyExcludesCompletedAndFutureTasks()
    {
        var result = await _tools.list_tasks(overdue_only: true);

        Assert.Contains("Overdue tasks as of", result);
        Assert.Contains("Overdue task", result);
        Assert.DoesNotContain("Completed task", result);
        Assert.DoesNotContain("Future task", result);
        Assert.Contains("OVERDUE", result);
    }

    [Fact]
    public async Task SetTaskState_SupportsBothStateDirections()
    {
        var completed = await _tools.set_task_state("State", 5, completed: true);
        var reopened = await _tools.set_task_state("State", 5, completed: false);

        Assert.Contains("Task marked as complete", completed);
        Assert.Contains("Task reopened", reopened);
        Assert.Contains("- [ ] Change me", await File.ReadAllTextAsync(Path.Combine(_vaultPath, "State.md")));
    }

    private async Task WriteNoteAsync(string name, string[] tags, string body)
    {
        var path = NoteHelpers.BuildFilePath(name, _vaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = NoteHelpers.BuildFrontmatter(tags, status: "draft") + "\n" + body;
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
    }
}
