using System.Text;
using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class TemplaterTemplateRootTests : IAsyncLifetime
{
    private string _vaultPath = null!;

    public Task InitializeAsync()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), $"kioku-templater-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_vaultPath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            Directory.Delete(_vaultPath, recursive: true);
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ReadTemplatesFolderAsync_NoSettings_ReturnsNull()
    {
        var result = await TemplaterFolderTemplates.ReadTemplatesFolderAsync(_vaultPath);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadTemplatesFolderAsync_ReturnsRoot_when_folder_templates_are_disabled()
    {
        WriteSettings("""
            {
              "templates_folder": "99-system/templates",
              "enable_folder_templates": false,
              "folder_templates": []
            }
            """);

        var result = await TemplaterFolderTemplates.ReadTemplatesFolderAsync(_vaultPath);

        Assert.Equal("99-system/templates", result);
    }

    [Fact]
    public async Task ReadTemplatesFolderAsync_BlankRoot_ReturnsNull()
    {
        WriteSettings("""{ "templates_folder": "   " }""");

        var result = await TemplaterFolderTemplates.ReadTemplatesFolderAsync(_vaultPath);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadTemplatesFolderAsync_MalformedSettings_ReturnsNull()
    {
        WriteSettings("{ not valid json");

        var result = await TemplaterFolderTemplates.ReadTemplatesFolderAsync(_vaultPath);

        Assert.Null(result);
    }

    private void WriteSettings(string json)
    {
        var path = Path.Combine(_vaultPath, ".obsidian", "plugins", "templater-obsidian", "data.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json, Encoding.UTF8);
    }
}
