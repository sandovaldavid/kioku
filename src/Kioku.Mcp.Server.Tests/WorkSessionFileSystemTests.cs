using Kioku.Mcp.Server.Infrastructure;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class WorkSessionFileSystemTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"kioku-session-fs-{Guid.NewGuid():N}");

    [Fact]
    public async Task CancelledCreate_DoesNotCreateDirectoryOrFile()
    {
        var fileSystem = new WorkSessionFileSystem();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fileSystem.WriteNewSessionFileAsync(
                _root,
                "preferred.md",
                "fallback.md",
                "content",
                cancellation.Token));

        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task PreferredNameCollision_UsesFallbackWithoutOverwritingExistingFile()
    {
        Directory.CreateDirectory(_root);
        var preferred = Path.Combine(_root, "preferred.md");
        await File.WriteAllTextAsync(preferred, "existing");
        var fileSystem = new WorkSessionFileSystem();

        var created = await fileSystem.WriteNewSessionFileAsync(
            _root,
            "preferred.md",
            "fallback.md",
            "new session",
            CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "fallback.md"), created);
        Assert.Equal("existing", await File.ReadAllTextAsync(preferred));
        Assert.Equal("new session", await File.ReadAllTextAsync(created));
    }

    [Fact]
    public async Task CancelledAtomicWrite_PreservesOriginalAndLeavesNoTemporaryFile()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "session.md");
        await File.WriteAllTextAsync(target, "original");
        var fileSystem = new WorkSessionFileSystem();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fileSystem.WriteAtomicallyAsync(
                target,
                "replacement",
                cancellation.Token));

        Assert.Equal("original", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
