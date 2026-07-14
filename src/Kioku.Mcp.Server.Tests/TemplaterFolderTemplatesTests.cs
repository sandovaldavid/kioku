using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Reading/writing Templater's own folder-template settings ({vault}/.obsidian/plugins/
/// templater-obsidian/data.json), and VaultConfigService.ResolveFolderTemplateAsync, which
/// merges Kioku's own template_folders override with whatever Templater already has configured.
/// </summary>
public class TemplaterFolderTemplatesTests : IAsyncLifetime
{
    private string _vaultPath = null!;

    public Task InitializeAsync()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), $"kioku-templater-test-{Guid.NewGuid():N}");
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

    private string TemplaterSettingsPath =>
        Path.Combine(_vaultPath, ".obsidian", "plugins", "templater-obsidian", "data.json");

    private void WriteTemplaterSettings(string json)
    {
        var dir = Path.GetDirectoryName(TemplaterSettingsPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(TemplaterSettingsPath, json, Encoding.UTF8);
    }

    // ReadAsync

    [Fact]
    public async Task ReadAsync_NoDataJson_ReturnsEmpty()
    {
        var result = await TemplaterFolderTemplates.ReadAsync(_vaultPath);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadAsync_FolderTemplatesDisabled_ReturnsEmpty()
    {
        WriteTemplaterSettings("""
            { "enable_folder_templates": false, "folder_templates": [{"folder": "Daily", "template": "Templates/daily.md"}] }
            """);

        var result = await TemplaterFolderTemplates.ReadAsync(_vaultPath);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadAsync_EnabledWithEntries_ReturnsThem()
    {
        WriteTemplaterSettings("""
            {
              "enable_folder_templates": true,
              "folder_templates": [
                {"folder": "Daily", "template": "Templates/daily.md"},
                {"folder": "", "template": ""}
              ]
            }
            """);

        var result = await TemplaterFolderTemplates.ReadAsync(_vaultPath);

        var pair = Assert.Single(result);
        Assert.Equal("Daily", pair.Folder);
        Assert.Equal("Templates/daily.md", pair.Template);
    }

    [Fact]
    public async Task ReadAsync_MalformedJson_ReturnsEmptyWithoutThrowing()
    {
        WriteTemplaterSettings("{ not valid json");

        var result = await TemplaterFolderTemplates.ReadAsync(_vaultPath);

        Assert.Empty(result);
    }

    // RegisterFolderTemplatesAsync

    [Fact]
    public async Task RegisterFolderTemplatesAsync_NoDataJson_ReturnsZeroWithoutCreatingFile()
    {
        var added = await TemplaterFolderTemplates.RegisterFolderTemplatesAsync(
            _vaultPath, [("Projects/demo/decisions", "Templates/kioku/adr.md")]);

        Assert.Equal(0, added);
        Assert.False(File.Exists(TemplaterSettingsPath));
    }

    [Fact]
    public async Task RegisterFolderTemplatesAsync_AddsNewEntriesAndEnablesFeature()
    {
        WriteTemplaterSettings("""{ "enable_folder_templates": false, "folder_templates": [] }""");

        var added = await TemplaterFolderTemplates.RegisterFolderTemplatesAsync(
            _vaultPath,
            [
                ("Projects/demo/decisions", "Templates/kioku/adr.md"),
                ("Projects/demo/bugs", "Templates/kioku/bug.md"),
            ]);

        Assert.Equal(2, added);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(TemplaterSettingsPath));
        Assert.True(doc.RootElement.GetProperty("enable_folder_templates").GetBoolean());
        var entries = doc.RootElement.GetProperty("folder_templates").EnumerateArray().ToList();
        Assert.Contains(entries, e => e.GetProperty("folder").GetString() == "Projects/demo/decisions");
        Assert.Contains(entries, e => e.GetProperty("folder").GetString() == "Projects/demo/bugs");
    }

    [Fact]
    public async Task RegisterFolderTemplatesAsync_NeverOverwritesExistingUserMapping()
    {
        WriteTemplaterSettings("""
            {
              "enable_folder_templates": true,
              "folder_templates": [{"folder": "Projects/demo/decisions", "template": "MyOwn/custom.md"}]
            }
            """);

        var added = await TemplaterFolderTemplates.RegisterFolderTemplatesAsync(
            _vaultPath, [("Projects/demo/decisions", "Templates/kioku/adr.md")]);

        Assert.Equal(0, added);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(TemplaterSettingsPath));
        var entries = doc.RootElement.GetProperty("folder_templates").EnumerateArray().ToList();
        var entry = Assert.Single(entries);
        Assert.Equal("MyOwn/custom.md", entry.GetProperty("template").GetString());
    }

    [Fact]
    public async Task RegisterFolderTemplatesAsync_IsIdempotent()
    {
        WriteTemplaterSettings("""{ "enable_folder_templates": true, "folder_templates": [] }""");
        var entries = new[] { ("Projects/demo/decisions", "Templates/kioku/adr.md") };

        var first = await TemplaterFolderTemplates.RegisterFolderTemplatesAsync(_vaultPath, entries);
        var second = await TemplaterFolderTemplates.RegisterFolderTemplatesAsync(_vaultPath, entries);

        Assert.Equal(1, first);
        Assert.Equal(0, second);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(TemplaterSettingsPath));
        Assert.Single(doc.RootElement.GetProperty("folder_templates").EnumerateArray());
    }

    [Fact]
    public async Task RegisterFolderTemplatesAsync_NeverWritesAByteOrderMark()
    {
        // Obsidian writes its own settings files without a BOM; if Kioku's rewrite introduced
        // one, Obsidian/Node's JSON.parse could fail to load Templater's settings on next start.
        WriteTemplaterSettings("""{ "enable_folder_templates": false, "folder_templates": [] }""");

        await TemplaterFolderTemplates.RegisterFolderTemplatesAsync(
            _vaultPath, [("Projects/demo/decisions", "Templates/kioku/adr.md")]);

        var bytes = await File.ReadAllBytesAsync(TemplaterSettingsPath);
        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "rewritten Templater settings file must not start with a UTF-8 BOM");
    }

    [Fact]
    public async Task RegisterFolderTemplatesAsync_PreservesOtherExistingSettings()
    {
        WriteTemplaterSettings("""
            { "enable_folder_templates": false, "folder_templates": [], "command_timeout": 5, "templates_folder": "Templates" }
            """);

        await TemplaterFolderTemplates.RegisterFolderTemplatesAsync(
            _vaultPath, [("Projects/demo/decisions", "Templates/kioku/adr.md")]);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(TemplaterSettingsPath));
        Assert.Equal(5, doc.RootElement.GetProperty("command_timeout").GetInt32());
        Assert.Equal("Templates", doc.RootElement.GetProperty("templates_folder").GetString());
    }

    // VaultConfigService.ResolveFolderTemplateAsync

    private VaultConfigService CreateVaultConfig(string? configYaml = null)
    {
        var config = new KiokuConfiguration { VaultPath = _vaultPath };
        if (configYaml is not null)
        {
            var configDir = Path.Combine(_vaultPath, ".kioku");
            Directory.CreateDirectory(configDir);
            File.WriteAllText(Path.Combine(configDir, "config.yml"), configYaml, Encoding.UTF8);
        }

        return new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
    }

    [Fact]
    public async Task ResolveFolderTemplateAsync_NoConfigNoTemplater_ReturnsNull()
    {
        var vaultConfig = CreateVaultConfig();

        var result = await vaultConfig.ResolveFolderTemplateAsync("Journal/Daily");

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveFolderTemplateAsync_KiokuOverride_Wins()
    {
        WriteTemplaterSettings("""
            {
              "enable_folder_templates": true,
              "folder_templates": [{"folder": "Journal/Daily", "template": "Templates/from-templater.md"}]
            }
            """);
        var vaultConfig = CreateVaultConfig("""
            template_folders:
              "Journal/Daily": "Templates/from-kioku-config.md"
            """);

        var result = await vaultConfig.ResolveFolderTemplateAsync("Journal/Daily");

        Assert.Equal("Templates/from-kioku-config.md", result);
    }

    [Fact]
    public async Task ResolveFolderTemplateAsync_FallsBackToTemplaterWhenNoKiokuOverride()
    {
        WriteTemplaterSettings("""
            {
              "enable_folder_templates": true,
              "folder_templates": [{"folder": "Journal/Daily", "template": "Templates/from-templater.md"}]
            }
            """);
        var vaultConfig = CreateVaultConfig();

        var result = await vaultConfig.ResolveFolderTemplateAsync("Journal/Daily");

        Assert.Equal("Templates/from-templater.md", result);
    }

    [Fact]
    public async Task ResolveFolderTemplateAsync_LongestPrefixWins()
    {
        var vaultConfig = CreateVaultConfig("""
            template_folders:
              "Journal": "Templates/generic.md"
              "Journal/Daily": "Templates/daily-specific.md"
            """);

        var result = await vaultConfig.ResolveFolderTemplateAsync("Journal/Daily");

        Assert.Equal("Templates/daily-specific.md", result);
    }

    [Fact]
    public async Task ResolveFolderTemplateAsync_NoMatchingPrefix_ReturnsNull()
    {
        var vaultConfig = CreateVaultConfig("""
            template_folders:
              "Journal/Daily": "Templates/daily.md"
            """);

        var result = await vaultConfig.ResolveFolderTemplateAsync("Areas/Work");

        Assert.Null(result);
    }
}
