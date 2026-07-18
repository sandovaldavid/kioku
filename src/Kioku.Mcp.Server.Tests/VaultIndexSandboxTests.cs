using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class VaultIndexSandboxTests
{
    [Fact]
    public async Task Initialize_DoesNotIndexMarkdownThroughExternalDirectoryLink()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kioku-index-sandbox-{Guid.NewGuid():N}");
        var vaultPath = Path.Combine(root, "vault");
        var externalPath = Path.Combine(root, "external");
        var linkPath = Path.Combine(vaultPath, "linked-external");
        Directory.CreateDirectory(vaultPath);
        Directory.CreateDirectory(externalPath);
        await File.WriteAllTextAsync(Path.Combine(vaultPath, "Safe.md"), "# Safe\ninside vault");
        await File.WriteAllTextAsync(Path.Combine(externalPath, "Secret.md"), "# Secret\nexternal-marker-unique");

        try
        {
            try
            {
                Directory.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var config = new KiokuConfiguration { VaultPath = vaultPath };
            var policy = new VaultPathPolicy(config);
            using var index = new VaultIndexService(
                NullLogger<VaultIndexService>.Instance,
                config,
                pathPolicy: policy);

            await index.InitializeAsync();

            Assert.NotNull(index.GetNoteByName("Safe"));
            Assert.Null(index.GetNoteByName("Secret"));
            Assert.Null(index.GetNote(Path.Combine(externalPath, "Secret.md")));
            Assert.Empty(index.Search("external-marker-unique"));
            Assert.All(index.GetAllNotes(), note => Assert.True(policy.IsInsideVault(note.FilePath)));
        }
        finally
        {
            try
            {
                if (Directory.Exists(linkPath))
                {
                    Directory.Delete(linkPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best-effort link cleanup before deleting the temporary root.
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
