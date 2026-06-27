using System.Text;
using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// xUnit test fixture that creates a temporary vault directory with sample notes.
/// Implements IAsyncLifetime for proper setup/teardown.
/// Use with IClassFixture&lt;VaultFixture&gt; in test classes.
/// </summary>
public sealed class VaultFixture : IAsyncLifetime
{
    public string VaultPath { get; private set; } = null!;
    public VaultIndexService Index { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        VaultPath = Path.Combine(Path.GetTempPath(), $"kioku-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(VaultPath);

        Index = new VaultIndexService(
            new Microsoft.Extensions.Logging.Abstractions.NullLogger<VaultIndexService>(),
            new KiokuConfiguration { VaultPath = VaultPath });

        await CreateNoteAsync("Note One", "Body of note one.", tags: ["alpha", "beta"]);
        await CreateNoteAsync("Note Two", "Body of note two.", tags: ["beta", "gamma"]);
        await CreateNoteAsync("Note Three", "References [[Note One]] and [[Note Two]].", tags: ["delta"]);
        await CreateNoteAsync("Projects/Project Alpha", "Alpha project note.", tags: ["project"]);
        await CreateNoteAsync("Projects/Project Beta", "Beta project note.", tags: ["project"]);
        await CreateNoteAsync("Archive/Old Note", "This is archived.", tags: ["archive"]);

        await Index.RebuildIndexAsync();
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(VaultPath))
            {
                Directory.Delete(VaultPath, recursive: true);
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    public async Task CreateNoteAsync(
        string name,
        string body,
        string[]? tags = null,
        string? type = null,
        string? status = null,
        DateOnly? date = null)
    {
        var filePath = NoteHelpers.BuildFilePath(name, VaultPath);
        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);

        var frontmatter = NoteHelpers.BuildFrontmatter(
            tags ?? [],
            type: type ?? "",
            status: status ?? "draft",
            date: date);

        var content = frontmatter + "\n" + body;
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
    }

    public async Task<string> ReadNoteBodyAsync(string name)
    {
        var filePath = NoteHelpers.BuildFilePath(name, VaultPath);
        var raw = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
        var bodyStart = FrontmatterParser.GetBodyStart(raw);
        return raw[bodyStart..];
    }

    public bool NoteExists(string name)
    {
        var filePath = NoteHelpers.BuildFilePath(name, VaultPath);
        return File.Exists(filePath);
    }

    public string GetNotePath(string name) => NoteHelpers.BuildFilePath(name, VaultPath);

    public string GetFolderPath(string folder) => Path.Combine(VaultPath, folder);
}
