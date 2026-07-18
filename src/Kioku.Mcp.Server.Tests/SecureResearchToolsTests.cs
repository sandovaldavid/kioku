using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class SecureResearchToolsTests : IAsyncLifetime
{
    private const string OneEntry = """
        @article{sandbox2026,
          author = {Safe, Reader},
          title = {Sandboxed Imports},
          year = {2026},
        }
        """;

    private readonly string _externalRoot = Path.Combine(
        Path.GetTempPath(), $"kioku-secure-research-{Guid.NewGuid():N}");
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_externalRoot);
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
        if (Directory.Exists(_externalRoot))
        {
            Directory.Delete(_externalRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ImportBibtex_VaultRelativeFile_IsAllowed()
    {
        var importFolder = Path.Combine(_fixture.VaultPath, "Imports");
        Directory.CreateDirectory(importFolder);
        await File.WriteAllTextAsync(Path.Combine(importFolder, "library.bib"), OneEntry);

        var result = await CreateTools().import_bibtex("Imports/library.bib");
        await _fixture.Index.RebuildIndexAsync();

        Assert.Contains("[ok] Imported 1 entries", result);
        Assert.Contains(_fixture.Index.GetAllNotes(), note =>
            note.Metadata.ExtraFields.GetValueOrDefault("citekey") == "sandbox2026");
    }

    [Fact]
    public async Task ImportBibtex_AbsoluteExternalFile_IsDeniedByDefaultWithoutPathDisclosure()
    {
        var source = Path.Combine(_externalRoot, "private-library.bib");
        await File.WriteAllTextAsync(source, OneEntry);

        var result = await CreateTools().import_bibtex(source);

        Assert.StartsWith("[error] [ACCESS_DENIED]", result);
        Assert.DoesNotContain(_externalRoot, result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(source, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportBibtex_ExternalFile_RequiresEnabledAllowlistedRoot()
    {
        var source = Path.Combine(_externalRoot, "allowed-library.bib");
        await File.WriteAllTextAsync(source, OneEntry);

        var result = await CreateTools(allowExternalReads: true, externalRoots: [_externalRoot])
            .import_bibtex(source);
        await _fixture.Index.RebuildIndexAsync();

        Assert.Contains("[ok] Imported 1 entries", result);
        Assert.Contains(_fixture.Index.GetAllNotes(), note =>
            note.Metadata.ExtraFields.GetValueOrDefault("citekey") == "sandbox2026");
    }

    [Fact]
    public async Task ImportBibtex_ExternalFileOutsideAllowlist_IsDenied()
    {
        var allowedRoot = Path.Combine(_externalRoot, "allowed");
        var deniedRoot = Path.Combine(_externalRoot, "denied");
        Directory.CreateDirectory(allowedRoot);
        Directory.CreateDirectory(deniedRoot);
        var source = Path.Combine(deniedRoot, "library.bib");
        await File.WriteAllTextAsync(source, OneEntry);

        var result = await CreateTools(allowExternalReads: true, externalRoots: [allowedRoot])
            .import_bibtex(source);

        Assert.StartsWith("[error] [ACCESS_DENIED]", result);
    }

    [Fact]
    public async Task ImportBibtex_FileSourceRequiresBibExtension()
    {
        var source = Path.Combine(_fixture.VaultPath, "Imports", "library.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllTextAsync(source, OneEntry);

        var result = await CreateTools().import_bibtex("Imports/library.txt");

        Assert.StartsWith("[error] [INVALID_ARGUMENT]", result);
        Assert.Contains(".bib", result);
    }

    [Fact]
    public async Task ImportBibtex_InlineContent_DoesNotRequireFilesystemPermission()
    {
        var result = await CreateTools().import_bibtex(OneEntry, dry_run: true);

        Assert.Contains("[dry-run]", result);
        Assert.DoesNotContain("ACCESS_DENIED", result);
    }

    private SecureResearchTools CreateTools(
        bool allowExternalReads = false,
        IReadOnlyList<string>? externalRoots = null)
    {
        var config = new KiokuConfiguration
        {
            VaultPath = _fixture.VaultPath,
            AllowExternalReads = allowExternalReads,
            ExternalReadRoots = externalRoots ?? [],
        };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        return new SecureResearchTools(
            _fixture.Index,
            config,
            vaultConfig,
            new VaultPathPolicy(config));
    }
}