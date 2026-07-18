using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class PermanentDeletePolicyTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task DeleteNote_PermanentDeleteIsDeniedByDefault()
    {
        await _fixture.CreateNoteAsync("Delete/Protected", "Important content.");
        await _fixture.Index.RebuildIndexAsync();
        var filePath = _fixture.GetNotePath("Delete/Protected");

        var result = await CreateTools(allowPermanentDelete: false)
            .delete_note("Delete/Protected", permanent: true);

        Assert.StartsWith("[error] [ACCESS_DENIED]", result);
        Assert.True(File.Exists(filePath));
        Assert.NotNull(_fixture.Index.GetNote("Delete/Protected"));
    }

    [Fact]
    public async Task DeleteNote_PermanentDeleteRequiresExplicitOptIn()
    {
        await _fixture.CreateNoteAsync("Delete/Allowed", "Disposable content.");
        await _fixture.Index.RebuildIndexAsync();
        var filePath = _fixture.GetNotePath("Delete/Allowed");

        var result = await CreateTools(allowPermanentDelete: true)
            .delete_note("Delete/Allowed", permanent: true);

        Assert.StartsWith("[ok]", result);
        Assert.False(File.Exists(filePath));
        Assert.Null(_fixture.Index.GetNote("Delete/Allowed"));
    }

    [Fact]
    public async Task DeleteNote_SoftDeleteRemainsAvailableWithoutOptIn()
    {
        await _fixture.CreateNoteAsync("Delete/Recoverable", "Recoverable content.");
        await _fixture.Index.RebuildIndexAsync();

        var result = await CreateTools(allowPermanentDelete: false)
            .delete_note("Delete/Recoverable");

        Assert.StartsWith("[ok]", result);
        Assert.Contains("moved to trash", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.EnumerateFiles(
                Path.Combine(_fixture.VaultPath, ".trash"),
                "Recoverable*.md",
                SearchOption.AllDirectories)
            .Any());
    }

    private NoteCommandTools CreateTools(bool allowPermanentDelete)
    {
        var config = new KiokuConfiguration
        {
            VaultPath = _fixture.VaultPath,
            AllowPermanentDelete = allowPermanentDelete,
        };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        return new NoteCommandTools(
            _fixture.Index,
            config,
            vaultConfig,
            pathPolicy: new VaultPathPolicy(config));
    }
}