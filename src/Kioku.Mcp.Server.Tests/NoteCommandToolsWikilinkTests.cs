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

        var result = await tools.move_note("Note One", new_name: "Note One Renamed");

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

        var result = await tools.move_note("Note One", new_name: "Note One Renamed", dry_run: true);

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

        var result = await tools.move_note("Note One", new_name: "Note One Renamed", update_links: false);

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

        var result = await tools.move_note("Folder/Duplicate", new_name: "Folder/Duplicate Renamed");

        Assert.Contains("ambiguous", result, StringComparison.OrdinalIgnoreCase);

        var body = await _fixture.ReadNoteBodyAsync("Linker");
        Assert.Contains("[[Duplicate]]", body);
        Assert.Contains("[[Folder/Duplicate Renamed]]", body);
    }

    [Fact]
    public async Task RenameNote_LiteralHashSibling_IsNotRewrittenAsFragment()
    {
        await _fixture.CreateNoteAsync("Old", "Target note.");
        await _fixture.CreateNoteAsync("Old#suffix", "Distinct literal-hash note.");
        await _fixture.CreateNoteAsync(
            "Linker",
            "[[Old]] [[Old#Heading]] [[Old#^block]] [[Old#suffix]]");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var before = await _fixture.ReadNoteBodyAsync("Linker");
        var dryRun = await tools.move_note("Old", new_name: "New", dry_run: true);

        Assert.Contains("Would update 3 wikilink(s) in 1 note(s)", dryRun);
        Assert.Equal(before, await _fixture.ReadNoteBodyAsync("Linker"));
        Assert.True(_fixture.NoteExists("Old"));
        Assert.True(_fixture.NoteExists("Old#suffix"));

        var applied = await tools.move_note("Old", new_name: "New");

        Assert.Contains("Updated 3 wikilink(s) in 1 note(s).", applied);
        Assert.False(_fixture.NoteExists("Old"));
        Assert.True(_fixture.NoteExists("New"));
        Assert.True(_fixture.NoteExists("Old#suffix"));

        var body = await _fixture.ReadNoteBodyAsync("Linker");
        Assert.Contains("[[New]]", body);
        Assert.Contains("[[New#Heading]]", body);
        Assert.Contains("[[New#^block]]", body);
        Assert.Contains("[[Old#suffix]]", body);
        Assert.DoesNotContain("[[New#suffix]]", body);
    }

    [Fact]
    public async Task RenameNote_FullPathLiteralHashSibling_IsNotRewrittenAsFragment()
    {
        await _fixture.CreateNoteAsync("Folder/Old", "Target note.");
        await _fixture.CreateNoteAsync("Folder/Old#suffix", "Distinct literal-hash note.");
        await _fixture.CreateNoteAsync(
            "Linker",
            "[[Folder/Old#Heading]] [[Folder/Old#suffix]]");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var result = await tools.move_note("Folder/Old", new_name: "Folder/New");

        Assert.Contains("Updated 1 wikilink(s) in 1 note(s).", result);
        var body = await _fixture.ReadNoteBodyAsync("Linker");
        Assert.Contains("[[Folder/New#Heading]]", body);
        Assert.Contains("[[Folder/Old#suffix]]", body);
        Assert.DoesNotContain("[[Folder/New#suffix]]", body);
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
    public async Task MoveNote_RewritesInboundRelativeParentTraversalLinks()
    {
        await _fixture.CreateNoteAsync("A/Source", "See [[../B/Target]].");
        await _fixture.CreateNoteAsync("B/Target", "Target note.");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var result = await tools.move_note("B/Target", new_name: "B/Renamed");

        Assert.Contains("[ok] Note renamed", result);
        Assert.Contains("Updated 1 wikilink(s) in 1 note(s).", result);

        var body = await _fixture.ReadNoteBodyAsync("A/Source");
        Assert.Contains("[[B/Renamed]]", body);
        Assert.DoesNotContain("[[../B/Target]]", body);
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
