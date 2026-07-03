using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for the wikilink auto-update behavior of move_note/rename_note.
/// Each test gets its own temporary vault (not shared via IClassFixture) since these
/// operations mutate files on disk.
/// </summary>
public class NoteCommandToolsWikilinkTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private NoteCommandTools CreateTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        return new NoteCommandTools(_fixture.Index, config, vaultConfig);
    }

    [Fact]
    public async Task RenameNote_UpdatesBareNameBacklinksByDefault()
    {
        var tools = CreateTools();

        var result = await tools.rename_note("Note One", "Note One Renamed");

        Assert.Contains("[ok] Note renamed", result);
        Assert.Contains("Updated 1 wikilink(s) in 1 note(s).", result);

        var body = await _fixture.ReadNoteBodyAsync("Note Three");
        Assert.Contains("[[Note One Renamed]]", body);
        Assert.DoesNotContain("[[Note One]]", body);
        Assert.Contains("[[Note Two]]", body);

        var backlinks = _fixture.Index.GetBacklinks("Note One Renamed").ToList();
        Assert.Contains(backlinks, n => n.Name == "Note Three");
    }

    [Fact]
    public async Task RenameNote_DryRun_DoesNotModifyAnyFile()
    {
        var tools = CreateTools();
        var beforeBody = await _fixture.ReadNoteBodyAsync("Note Three");

        var result = await tools.rename_note("Note One", "Note One Renamed", dry_run: true);

        Assert.Contains("[info] Dry run", result);
        Assert.Contains("Would update 1 wikilink(s) in 1 note(s)", result);
        Assert.True(_fixture.NoteExists("Note One"));
        Assert.False(_fixture.NoteExists("Note One Renamed"));

        var afterBody = await _fixture.ReadNoteBodyAsync("Note Three");
        Assert.Equal(beforeBody, afterBody);
    }

    [Fact]
    public async Task RenameNote_UpdateLinksFalse_LeavesBacklinksUnchanged()
    {
        var tools = CreateTools();

        var result = await tools.rename_note("Note One", "Note One Renamed", update_links: false);

        Assert.Contains("[ok] Note renamed", result);
        Assert.DoesNotContain("wikilink", result);

        var body = await _fixture.ReadNoteBodyAsync("Note Three");
        Assert.Contains("[[Note One]]", body);
    }

    [Fact]
    public async Task RenameNote_AmbiguousBareName_SkipsAndReports()
    {
        await _fixture.CreateNoteAsync("Duplicate", "Root duplicate.");
        await _fixture.CreateNoteAsync("Folder/Duplicate", "Folder duplicate.");
        await _fixture.CreateNoteAsync("Linker", "See [[Duplicate]] and [[Folder/Duplicate]].");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var result = await tools.rename_note("Folder/Duplicate", "Folder/Duplicate Renamed");

        Assert.Contains("ambiguous", result, StringComparison.OrdinalIgnoreCase);

        var body = await _fixture.ReadNoteBodyAsync("Linker");
        Assert.Contains("[[Duplicate]]", body);
        Assert.Contains("[[Folder/Duplicate Renamed]]", body);
    }

    [Fact]
    public async Task MoveNote_UpdatesFullPathLinks_LeavesBareNameLinksUntouched()
    {
        await _fixture.CreateNoteAsync("Linker", "Full: [[Projects/Project Alpha]]. Bare: [[Project Alpha]].");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var result = await tools.move_note("Projects/Project Alpha", "Archive/2024");

        Assert.Contains("[ok] Note moved", result);
        Assert.Contains("Updated 1 wikilink(s) in 1 note(s).", result);

        var body = await _fixture.ReadNoteBodyAsync("Linker");
        Assert.Contains("[[Archive/2024/Project Alpha]]", body);
        Assert.Contains("[[Project Alpha]]", body);
    }

    [Fact]
    public async Task MoveNote_DryRun_DoesNotModifyAnyFile()
    {
        await _fixture.CreateNoteAsync("Linker", "Full: [[Projects/Project Alpha]].");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var result = await tools.move_note("Projects/Project Alpha", "Archive/2024", dry_run: true);

        Assert.Contains("[info] Dry run", result);
        Assert.True(_fixture.NoteExists("Projects/Project Alpha"));
        Assert.False(_fixture.NoteExists("Archive/2024/Project Alpha"));

        var body = await _fixture.ReadNoteBodyAsync("Linker");
        Assert.Contains("[[Projects/Project Alpha]]", body);
    }
}
