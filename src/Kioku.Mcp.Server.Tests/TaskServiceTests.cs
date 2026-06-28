using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class TaskServiceTests
{
    private readonly TaskService _service;

    public TaskServiceTests()
    {
        var config = new KiokuConfiguration { VaultPath = Path.GetTempPath() };
        _service = new TaskService(NullLogger<TaskService>.Instance, config);
    }

    [Fact]
    public async Task ParseTasksFromFileAsync_NoTasks_ReturnsEmpty()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "# Hello\n\nNo tasks here.");

        try
        {
            var tasks = await _service.ParseTasksFromFileAsync(tmpFile, "test.md", "test");
            Assert.Empty(tasks);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task ParseTasksFromFileAsync_OpenTask_ParsesCorrectly()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "- [ ] Buy groceries");

        try
        {
            var tasks = await _service.ParseTasksFromFileAsync(tmpFile, "test.md", "test");
            Assert.Single(tasks);
            Assert.Equal("Buy groceries", tasks[0].Text);
            Assert.False(tasks[0].IsCompleted);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task ParseTasksFromFileAsync_CompletedTask_ParsesCorrectly()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "- [x] Buy groceries");

        try
        {
            var tasks = await _service.ParseTasksFromFileAsync(tmpFile, "test.md", "test");
            Assert.Single(tasks);
            Assert.Equal("Buy groceries", tasks[0].Text);
            Assert.True(tasks[0].IsCompleted);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task ParseTasksFromFileAsync_CompletedUppercaseX_ParsesAsCompleted()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "- [X] Buy groceries");

        try
        {
            var tasks = await _service.ParseTasksFromFileAsync(tmpFile, "test.md", "test");
            Assert.Single(tasks);
            Assert.True(tasks[0].IsCompleted);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task ParseTasksFromFileAsync_DueDate_ParsesCorrectly()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "- [ ] Buy groceries \U0001f4c5 2025-01-15");

        try
        {
            var tasks = await _service.ParseTasksFromFileAsync(tmpFile, "test.md", "test");
            Assert.Single(tasks);
            Assert.NotNull(tasks[0].DueDate);
            Assert.Equal(new DateOnly(2025, 1, 15), tasks[0].DueDate);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task ParseTasksFromFileAsync_ScheduledDate_ParsesCorrectly()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "- [ ] Buy groceries \u23f3 2025-02-20");

        try
        {
            var tasks = await _service.ParseTasksFromFileAsync(tmpFile, "test.md", "test");
            Assert.Single(tasks);
            Assert.NotNull(tasks[0].ScheduledDate);
            Assert.Equal(new DateOnly(2025, 2, 20), tasks[0].ScheduledDate);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task ParseTasksFromFileAsync_StartDate_ParsesCorrectly()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "- [ ] Buy groceries \U0001f6eb 2025-03-10");

        try
        {
            var tasks = await _service.ParseTasksFromFileAsync(tmpFile, "test.md", "test");
            Assert.Single(tasks);
            Assert.NotNull(tasks[0].StartDate);
            Assert.Equal(new DateOnly(2025, 3, 10), tasks[0].StartDate);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task ParseTasksFromFileAsync_InlineTags_ParsesCorrectly()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "- [ ] Buy groceries #shopping #urgent");

        try
        {
            var tasks = await _service.ParseTasksFromFileAsync(tmpFile, "test.md", "test");
            Assert.Single(tasks);
            Assert.Equal(2, tasks[0].InlineTags.Count);
            Assert.Contains("shopping", tasks[0].InlineTags);
            Assert.Contains("urgent", tasks[0].InlineTags);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task ParseTasksFromFileAsync_MultipleTasks_ParsesAll()
    {
        var tmpFile = Path.GetTempFileName();
        var content = """
            # Tasks

            - [ ] Buy groceries
            - [x] Clean house
            - [ ] Write report

            Some text in between.

            - [ ] Call dentist
            """;
        await File.WriteAllTextAsync(tmpFile, content);

        try
        {
            var tasks = await _service.ParseTasksFromFileAsync(tmpFile, "test.md", "test");
            Assert.Equal(4, tasks.Count);
            Assert.Equal(3, tasks.Count(t => !t.IsCompleted));
            Assert.Single(tasks, t => t.IsCompleted);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task ParseTasksFromFileAsync_IndentedTask_ParsesCorrectly()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "  - [ ] Indented task");

        try
        {
            var tasks = await _service.ParseTasksFromFileAsync(tmpFile, "test.md", "test");
            Assert.Single(tasks);
            Assert.Equal("Indented task", tasks[0].Text);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task ParseTasksFromFileAsync_LineNumber_IsCorrect()
    {
        var tmpFile = Path.GetTempFileName();
        var content = "# Title\n\nSome text\n\n- [ ] Task on line 5";
        await File.WriteAllTextAsync(tmpFile, content);

        try
        {
            var tasks = await _service.ParseTasksFromFileAsync(tmpFile, "test.md", "test");
            Assert.Single(tasks);
            Assert.Equal(5, tasks[0].LineNumber);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task ParseTasksFromFileAsync_DueDateStrippedFromText()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "- [ ] Buy groceries \U0001f4c5 2025-01-15");

        try
        {
            var tasks = await _service.ParseTasksFromFileAsync(tmpFile, "test.md", "test");
            Assert.Single(tasks);
            Assert.DoesNotContain("\U0001f4c5", tasks[0].Text);
            Assert.DoesNotContain("2025-01-15", tasks[0].Text);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task SetTaskCompletionAsync_MarksTaskComplete()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "- [ ] Buy groceries\n- [ ] Clean house");

        try
        {
            var result = await _service.SetTaskCompletionAsync(tmpFile, 1, true);
            Assert.NotNull(result);
            Assert.True(result.IsCompleted);

            var content = await File.ReadAllTextAsync(tmpFile);
            Assert.Contains("- [x] Buy groceries", content);
            Assert.Contains("- [ ] Clean house", content);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task SetTaskCompletionAsync_ReopensTask()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "- [x] Buy groceries");

        try
        {
            var result = await _service.SetTaskCompletionAsync(tmpFile, 1, false);
            Assert.NotNull(result);
            Assert.False(result.IsCompleted);

            var content = await File.ReadAllTextAsync(tmpFile);
            Assert.Contains("- [ ] Buy groceries", content);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task SetTaskCompletionAsync_InvalidLine_ReturnsNull()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "# Not a task");

        try
        {
            var result = await _service.SetTaskCompletionAsync(tmpFile, 1, true);
            Assert.Null(result);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task SetTaskCompletionAsync_OutOfRangeLine_ReturnsNull()
    {
        var tmpFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmpFile, "- [ ] Task");

        try
        {
            var result = await _service.SetTaskCompletionAsync(tmpFile, 99, true);
            Assert.Null(result);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }
}
