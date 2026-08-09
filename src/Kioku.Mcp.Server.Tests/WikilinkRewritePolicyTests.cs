using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class WikilinkRewritePolicyTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Decide_DistinguishesLiteralHashSiblingFromRealFragment()
    {
        await _fixture.CreateNoteAsync("Old", "Target.");
        await _fixture.CreateNoteAsync("Old#suffix", "Distinct note.");
        await _fixture.CreateNoteAsync("Linker", "Links.");
        await _fixture.Index.RebuildIndexAsync();

        var source = _fixture.Index.ResolveNote("Linker")!;
        var plan = RenamePlan("Old", "New");

        var heading = WikilinkRewritePolicy.Decide(_fixture.Index, source, "Old#Heading", plan);
        var literalHash = WikilinkRewritePolicy.Decide(_fixture.Index, source, "Old#suffix", plan);

        Assert.Equal(WikilinkRewriter.TargetRewriteAction.Rewrite, heading.Action);
        Assert.Equal("New#Heading", heading.ReplacementTarget);
        Assert.Equal(WikilinkRewriter.TargetRewriteAction.LeaveUnchanged, literalHash.Action);
        Assert.Null(literalHash.ReplacementTarget);
    }

    [Fact]
    public async Task Decide_LeavesAliasAndRelativeSpellingsUncanonicalized()
    {
        await _fixture.CreateNoteAsync("Target", "Target.");
        await _fixture.CreateNoteAsync("Folder/Linker", "Links.");
        await File.WriteAllTextAsync(
            _fixture.GetNotePath("Alias Target"),
            "---\naliases:\n  - target-alias\n---\nAlias target.");
        await _fixture.Index.RebuildIndexAsync();

        var relativeSource = _fixture.Index.ResolveNote("Folder/Linker")!;
        var targetPlan = RenamePlan("Target", "Renamed");
        var relative = WikilinkRewritePolicy.Decide(_fixture.Index, relativeSource, "../Target", targetPlan);

        var aliasSource = _fixture.Index.ResolveNote("Linker") ?? relativeSource;
        var aliasPlan = RenamePlan("Alias Target", "Alias Renamed");
        var alias = WikilinkRewritePolicy.Decide(_fixture.Index, aliasSource, "target-alias", aliasPlan);

        Assert.Equal(WikilinkRewriter.TargetRewriteAction.LeaveUnchanged, relative.Action);
        Assert.Equal(WikilinkRewriter.TargetRewriteAction.LeaveUnchanged, alias.Action);
    }

    [Fact]
    public async Task Decide_LeavesMalformedTraversalUntouched()
    {
        await _fixture.CreateNoteAsync("Old", "Target.");
        await _fixture.CreateNoteAsync("Folder/Linker", "Links.");
        await _fixture.Index.RebuildIndexAsync();

        var source = _fixture.Index.ResolveNote("Folder/Linker")!;
        var decision = WikilinkRewritePolicy.Decide(
            _fixture.Index,
            source,
            "../../../Old",
            RenamePlan("Old", "New"));

        Assert.Equal(WikilinkRewriter.TargetRewriteAction.LeaveUnchanged, decision.Action);
    }

    [Fact]
    public async Task Decide_ReportsAmbiguousHistoricalBareNameWithoutGuessing()
    {
        await _fixture.CreateNoteAsync("Duplicate", "Root.");
        await _fixture.CreateNoteAsync("Folder/Duplicate", "Folder.");
        await _fixture.CreateNoteAsync("Linker", "Links.");
        await _fixture.Index.RebuildIndexAsync();

        var source = _fixture.Index.ResolveNote("Linker")!;
        var plan = new WikilinkRewriter.RewritePlan(
            "Duplicate",
            "Renamed",
            "Folder/Duplicate",
            "Folder/Renamed",
            RewriteShortNameLinks: true,
            ShortNameAmbiguous: true);

        var decision = WikilinkRewritePolicy.Decide(_fixture.Index, source, "Duplicate", plan);

        Assert.Equal(WikilinkRewriter.TargetRewriteAction.Ambiguous, decision.Action);
    }

    private static WikilinkRewriter.RewritePlan RenamePlan(string oldName, string newName) =>
        new(
            oldName,
            newName,
            oldName,
            newName,
            RewriteShortNameLinks: true,
            ShortNameAmbiguous: false);
}
