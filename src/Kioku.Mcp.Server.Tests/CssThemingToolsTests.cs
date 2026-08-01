using System.Text.Json;
using Kioku.Mcp.Server.Tools;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class CssThemingToolsTests : IAsyncLifetime
{
    private string _vaultPath = null!;

    public Task InitializeAsync()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), $"kioku-css-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_vaultPath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_vaultPath))
        {
            Directory.Delete(_vaultPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    private CssThemingTools CreateTools() =>
        new(new KiokuConfiguration { VaultPath = _vaultPath });

    [Fact]
    public async Task ManageCssSnippets_ApplyListAndRemove_PreservesEnabledState()
    {
        var obsidianFolder = Path.Combine(_vaultPath, ".obsidian");
        Directory.CreateDirectory(obsidianFolder);
        await File.WriteAllTextAsync(
            Path.Combine(obsidianFolder, "app.json"),
            "{\"theme\":\"obsidian\",\"enabledCssSnippets\":[\"existing\",\"custom\"]}");

        var tools = CreateTools();
        var applyResult = await tools.manage_css_snippets(
            action: "apply", name: "custom", css_content: ".custom { color: red; }");
        var disabledResult = await tools.manage_css_snippets(
            action: "apply", name: "disabled", css_content: ".disabled { color: blue; }", enable: false);

        Assert.StartsWith("[ok]", applyResult);
        Assert.StartsWith("[ok]", disabledResult);
        Assert.True(File.Exists(Path.Combine(_vaultPath, ".obsidian", "snippets", "custom.css")));

        var listResult = await tools.manage_css_snippets(action: "list");
        Assert.Contains("[✓ enabled] custom.css", listResult);
        Assert.Contains("[○ disabled] disabled.css", listResult);

        // Disabling an existing snippet preserves the existing app.json enabled state.
        await tools.manage_css_snippets(
            action: "apply", name: "custom", css_content: ".custom { color: green; }", enable: false);

        var removeResult = await tools.manage_css_snippets(action: "remove", name: "custom");

        Assert.StartsWith("[ok]", removeResult);
        Assert.False(File.Exists(Path.Combine(_vaultPath, ".obsidian", "snippets", "custom.css")));
        Assert.Equal(["existing"], await ReadEnabledSnippetsAsync());
    }

    [Fact]
    public async Task ManageCssSnippets_ApplyWithoutEnable_CreatesEnabledStateByDefault()
    {
        var result = await CreateTools().manage_css_snippets(
            action: "apply", name: "default-enabled", css_content: ".default-enabled {};");

        Assert.Contains("added to enabledCssSnippets", result);
        Assert.Equal(["default-enabled"], await ReadEnabledSnippetsAsync());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    public async Task ManageCssSnippets_InvalidAction_ReturnsError(string action)
    {
        var result = await CreateTools().manage_css_snippets(action);

        Assert.StartsWith("[error]", result);
        Assert.Contains("list, apply, remove", result);
    }

    [Fact]
    public async Task ManageCssSnippets_MissingOrIrrelevantParameters_ReturnErrors()
    {
        var tools = CreateTools();

        Assert.StartsWith("[error]", await tools.manage_css_snippets(action: "apply"));
        Assert.StartsWith("[error]", await tools.manage_css_snippets(
            action: "apply", name: "snippet"));
        Assert.StartsWith("[error]", await tools.manage_css_snippets(
            action: "remove", css_content: "unexpected"));
        Assert.StartsWith("[error]", await tools.manage_css_snippets(
            action: "list", name: "unexpected"));
    }

    [Fact]
    public void CssThemingTools_ExposesOnlyConsolidatedToolMethods()
    {
        var publicMethods = typeof(CssThemingTools).GetMethods()
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(CssThemingTools.manage_css_snippets), publicMethods);
        Assert.DoesNotContain("apply_css_snippet", publicMethods);
        Assert.DoesNotContain("list_css_snippets", publicMethods);
        Assert.DoesNotContain("remove_css_snippet", publicMethods);
        Assert.DoesNotContain("reload_css_snippets", publicMethods);
    }

    private async Task<string[]> ReadEnabledSnippetsAsync()
    {
        var appJsonPath = Path.Combine(_vaultPath, ".obsidian", "app.json");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(appJsonPath));
        return document.RootElement.GetProperty("enabledCssSnippets")
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .ToArray();
    }
}
