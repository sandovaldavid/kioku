using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class VaultPathPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"kioku-path-policy-{Guid.NewGuid():N}");
    private readonly string _vault;
    private readonly string _externalAllowed;
    private readonly string _externalDenied;
    private string? _createdLink;

    public VaultPathPolicyTests()
    {
        _vault = Path.Combine(_root, "vault");
        _externalAllowed = Path.Combine(_root, "imports");
        _externalDenied = Path.Combine(_root, "private");
        Directory.CreateDirectory(_vault);
        Directory.CreateDirectory(_externalAllowed);
        Directory.CreateDirectory(_externalDenied);
    }

    [Fact]
    public void ResolveVaultReadPath_RelativePathResolvesInsideVault()
    {
        var note = Path.Combine(_vault, "Notes", "Safe.md");
        Directory.CreateDirectory(Path.GetDirectoryName(note)!);
        File.WriteAllText(note, "safe");
        var policy = CreatePolicy();

        var resolved = policy.ResolveVaultReadPath("Notes/Safe.md");

        Assert.Equal(Path.GetFullPath(note), resolved);
    }

    [Fact]
    public void ResolveVaultWritePath_TraversalIsDeniedWithoutLeakingHostPaths()
    {
        var policy = CreatePolicy();
        var exception = Assert.Throws<VaultAccessDeniedException>(() =>
            policy.ResolveVaultWritePath("../private/secret.md"));

        Assert.Equal(VaultAccessDeniedException.ErrorCode, "ACCESS_DENIED");
        Assert.DoesNotContain(_vault, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_externalDenied, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outside the configured vault", exception.Message);
    }

    [Fact]
    public void ResolveVaultReadPath_AbsoluteOutsidePathIsDenied()
    {
        var secret = Path.Combine(_externalDenied, "secret.md");
        File.WriteAllText(secret, "secret");
        var policy = CreatePolicy();

        Assert.Throws<VaultAccessDeniedException>(() => policy.ResolveVaultReadPath(secret));
    }

    [Fact]
    public void ResolveExternalReadPath_IsDeniedByDefault()
    {
        var bib = Path.Combine(_externalAllowed, "library.bib");
        File.WriteAllText(bib, "@article{safe}");
        var policy = CreatePolicy();

        Assert.Throws<VaultAccessDeniedException>(() => policy.ResolveExternalReadPath(bib));
    }

    [Fact]
    public void ResolveExternalReadPath_RequiresEnabledAllowlistedRoot()
    {
        var bib = Path.Combine(_externalAllowed, "library.bib");
        File.WriteAllText(bib, "@article{safe}");
        var policy = CreatePolicy(allowExternalReads: true, externalRoots: [_externalAllowed]);

        var resolved = policy.ResolveExternalReadPath(bib);

        Assert.Equal(Path.GetFullPath(bib), resolved);
    }

    [Fact]
    public void ResolveExternalReadPath_RejectsPathOutsideAllowlist()
    {
        var secret = Path.Combine(_externalDenied, "secret.bib");
        File.WriteAllText(secret, "@article{secret}");
        var policy = CreatePolicy(allowExternalReads: true, externalRoots: [_externalAllowed]);

        Assert.Throws<VaultAccessDeniedException>(() => policy.ResolveExternalReadPath(secret));
    }

    [Fact]
    public void ResolveExternalReadPath_RelativePathsAreVaultRelativeNotCwdRelative()
    {
        var policy = CreatePolicy(allowExternalReads: true, externalRoots: [_externalAllowed]);

        var resolved = policy.ResolveExternalReadPath("imports/library.bib");

        Assert.Equal(Path.GetFullPath(Path.Combine(_vault, "imports", "library.bib")), resolved);
    }

    [Fact]
    public void ResolveVaultMove_ValidatesSourceAndDestination()
    {
        var source = Path.Combine(_vault, "source.md");
        File.WriteAllText(source, "source");
        var policy = CreatePolicy();

        var move = policy.ResolveVaultMove(source, "Archive/source.md");

        Assert.Equal(Path.GetFullPath(source), move.Source);
        Assert.Equal(Path.GetFullPath(Path.Combine(_vault, "Archive", "source.md")), move.Destination);
        Assert.Throws<VaultAccessDeniedException>(() =>
            policy.ResolveVaultMove(source, Path.Combine(_externalDenied, "source.md")));
    }

    [Fact]
    public void PermanentDelete_IsDisabledByDefaultAndMustBeExplicitlyEnabled()
    {
        Assert.False(CreatePolicy().AllowPermanentDelete);
        Assert.True(CreatePolicy(allowPermanentDelete: true).AllowPermanentDelete);
    }

    [Fact]
    public void SymbolicLinkEscape_IsDeniedAndNotEnumerated()
    {
        var externalNote = Path.Combine(_externalDenied, "Outside.md");
        File.WriteAllText(externalNote, "outside");
        _createdLink = Path.Combine(_vault, "linked-private");
        try
        {
            Directory.CreateSymbolicLink(_createdLink, _externalDenied);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Some local Windows environments do not grant symlink privileges. The same test runs
            // on GitHub-hosted Windows, Linux, and macOS runners where support is available.
            return;
        }

        var policy = CreatePolicy();

        Assert.Throws<VaultAccessDeniedException>(() =>
            policy.ResolveVaultReadPath(Path.Combine(_createdLink, "Outside.md")));
        Assert.DoesNotContain(
            policy.EnumerateVaultFiles("*.md", recursive: true),
            path => Path.GetFileName(path).Equals("Outside.md", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_createdLink is not null)
        {
            try
            {
                Directory.Delete(_createdLink);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup for platform-specific link behavior.
            }
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private VaultPathPolicy CreatePolicy(
        bool allowExternalReads = false,
        IReadOnlyList<string>? externalRoots = null,
        bool allowPermanentDelete = false) =>
        new(new KiokuConfiguration
        {
            VaultPath = _vault,
            AllowExternalReads = allowExternalReads,
            ExternalReadRoots = externalRoots ?? [],
            AllowPermanentDelete = allowPermanentDelete,
        });
}