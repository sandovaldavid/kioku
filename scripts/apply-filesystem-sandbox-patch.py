from pathlib import Path


def replace_once(path: str, old: str, new: str, label: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    if new in text:
        print(f"already applied: {label}")
        return
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one match for {label} in {path}, found {count}")
    file.write_text(text.replace(old, new), encoding="utf-8")
    print(f"applied: {label}")


index_path = "src/Kioku.Mcp.Server/Services/VaultIndexService.cs"
replace_once(
    index_path,
    "    private readonly ILogger<VaultIndexService> _logger;\n    private readonly string _vaultPath;",
    "    private readonly ILogger<VaultIndexService> _logger;\n    private readonly VaultPathPolicy _paths;\n    private readonly string _vaultPath;",
    "index policy field",
)
replace_once(
    index_path,
    """    public VaultIndexService(ILogger<VaultIndexService> logger, KiokuConfiguration config, EmbeddingService? embedding = null, VaultConfigService? vaultConfig = null)
    {
        _logger = logger;
        _vaultPath = config.VaultPath;
        _embedding = embedding;
""",
    """    public VaultIndexService(
        ILogger<VaultIndexService> logger,
        KiokuConfiguration config,
        EmbeddingService? embedding = null,
        VaultConfigService? vaultConfig = null,
        VaultPathPolicy? pathPolicy = null)
    {
        _logger = logger;
        _paths = pathPolicy ?? new VaultPathPolicy(config);
        _vaultPath = _paths.VaultRoot;
        _embedding = embedding;
""",
    "index constructor",
)
replace_once(
    index_path,
    """    public Note? GetNote(string path)
    {
        var absPath = Path.GetFullPath(ResolveAbsolutePath(path));
        return _notesByPath.TryGetValue(absPath, out var note) ? note : null;
    }
""",
    """    public Note? GetNote(string path)
    {
        try
        {
            var absPath = _paths.ResolveVaultReadPath(path);
            return _notesByPath.TryGetValue(absPath, out var note) ? note : null;
        }
        catch (Exception exception) when (
            exception is VaultAccessDeniedException or ArgumentException or IOException)
        {
            return null;
        }
    }
""",
    "secure GetNote",
)
replace_once(
    index_path,
    """    public IEnumerable<Note> GetNotesInFolder(string folderPath)
    {
        var absFolder = Path.IsPathRooted(folderPath)
            ? folderPath
            : Path.Combine(_vaultPath, folderPath);
        absFolder = Path.GetFullPath(absFolder);
        var folderPrefix = absFolder.EndsWith(Path.DirectorySeparatorChar)
            ? absFolder
            : absFolder + Path.DirectorySeparatorChar;

        return _notesByPath.Values
            .Where(n => n.FilePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase));
    }
""",
    """    public IEnumerable<Note> GetNotesInFolder(string folderPath)
    {
        string absFolder;
        try
        {
            absFolder = _paths.ResolveVaultReadPath(folderPath);
        }
        catch (Exception exception) when (
            exception is VaultAccessDeniedException or ArgumentException or IOException)
        {
            return [];
        }

        return _notesByPath.Values.Where(note => IsPathWithin(absFolder, note.FilePath));
    }
""",
    "secure folder lookup",
)
replace_once(
    index_path,
    """    private async Task IndexVaultAsync(CancellationToken cancellationToken)
    {
        var mdFiles = Directory.EnumerateFiles(_vaultPath, "*.md", SearchOption.AllDirectories)
            .Where(p => !IsExcludedPath(p));
        var tasks = mdFiles.Select(path => IndexFileAsync(path, cancellationToken));
        await Task.WhenAll(tasks);
        _lastIndexed = DateTimeOffset.UtcNow;
    }
""",
    """    private async Task IndexVaultAsync(CancellationToken cancellationToken)
    {
        var mdFiles = _paths.EnumerateVaultFiles("*.md", recursive: true)
            .Where(path => !IsExcludedPath(path));
        var tasks = mdFiles.Select(path => IndexFileAsync(path, cancellationToken));
        await Task.WhenAll(tasks);
        _lastIndexed = DateTimeOffset.UtcNow;
    }
""",
    "safe initial enumeration",
)
replace_once(
    index_path,
    """    private async Task IndexFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
""",
    """    private async Task IndexFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            filePath = _paths.ResolveVaultReadPath(filePath);
            if (!File.Exists(filePath))
            {
                return;
            }

            var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
""",
    "safe per-file indexing",
)
replace_once(
    index_path,
    """    public async Task SynchronizeFileMoveAsync(string oldPath, string newPath)
    {
        try
        {
            _embedding?.Move(oldPath, newPath);
            RemoveFromIndex(oldPath, removeEmbedding: false);
            await IndexFileAsync(newPath);
        }
""",
    """    public async Task SynchronizeFileMoveAsync(string oldPath, string newPath)
    {
        try
        {
            var move = _paths.ResolveVaultMove(oldPath, newPath);
            _embedding?.Move(move.Source, move.Destination);
            RemoveFromIndex(move.Source, removeEmbedding: false);
            await IndexFileAsync(move.Destination);
        }
""",
    "validate indexed moves",
)
replace_once(
    index_path,
    """    public void SynchronizeFileDelete(string filePath)
    {
        RemoveFromIndex(filePath);
    }
""",
    """    public void SynchronizeFileDelete(string filePath)
    {
        if (_paths.IsInsideVault(filePath))
        {
            RemoveFromIndex(filePath);
        }
    }
""",
    "validate indexed deletes",
)
replace_once(
    index_path,
    """    private string ResolveAbsolutePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(_vaultPath, path);
    }
""",
    """    private static bool IsPathWithin(string directory, string candidate)
    {
        var relative = Path.GetRelativePath(directory, candidate);
        return relative == "." ||
            (!Path.IsPathRooted(relative) &&
             relative != ".." &&
             !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }
""",
    "folder containment helper",
)
replace_once(
    index_path,
    """    private bool IsExcludedPath(string filePath)
    {
        var relative = Path.GetRelativePath(_vaultPath, filePath);
""",
    """    private bool IsExcludedPath(string filePath)
    {
        if (!_paths.IsInsideVault(filePath))
        {
            return true;
        }

        var relative = Path.GetRelativePath(_vaultPath, filePath);
""",
    "reject watcher escapes",
)

commands_path = "src/Kioku.Mcp.Server/Tools/NoteCommandTools.cs"
replace_once(
    commands_path,
    """public sealed partial class NoteCommandTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    VaultConfigService vaultConfig,
    ZettelkastenTools? zettelkasten = null,
    MetricsService? metrics = null)
""",
    """public sealed partial class NoteCommandTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    VaultConfigService vaultConfig,
    ZettelkastenTools? zettelkasten = null,
    MetricsService? metrics = null,
    VaultPathPolicy? pathPolicy = null)
""",
    "tool policy dependency",
)
replace_once(
    commands_path,
    "    private static readonly UTF8Encoding Utf8NoBom = NoteHelpers.Utf8NoBom;",
    "    private static readonly UTF8Encoding Utf8NoBom = NoteHelpers.Utf8NoBom;\n    private readonly VaultPathPolicy _paths = pathPolicy ?? new VaultPathPolicy(config);",
    "tool policy field",
)
replace_once(
    commands_path,
    """        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        if (dry_run)
""",
    """        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        if (permanent && !_paths.AllowPermanentDelete)
        {
            return KiokuError.AccessDenied(
                "Permanent deletion is disabled. Use soft delete or explicitly enable KIOKU_ALLOW_PERMANENT_DELETE.");
        }

        if (dry_run)
""",
    "permanent delete feature gate",
)
replace_once(
    commands_path,
    "        var filePath = found.FilePath;",
    """        string filePath;
        try
        {
            filePath = _paths.ResolveVaultDeletePath(found.FilePath);
        }
        catch (VaultAccessDeniedException)
        {
            return KiokuError.AccessDenied();
        }
""",
    "delete source validation",
)
replace_once(
    commands_path,
    "            var trashDir = Path.Combine(config.VaultPath, \".trash\");",
    "            var trashDir = _paths.ResolveVaultWritePath(\".trash\");",
    "trash boundary",
)
replace_once(
    commands_path,
    "                File.Move(filePath, trashPath, overwrite: false);",
    """                var move = _paths.ResolveVaultMove(filePath, trashPath);
                File.Move(move.Source, move.Destination, overwrite: false);
                trashPath = move.Destination;""",
    "soft-delete move validation",
)
replace_once(
    commands_path,
    """                    var destPath = string.IsNullOrWhiteSpace(destination)
                        ? Path.Combine(config.VaultPath, Path.GetFileName(trashFile))
                        : Path.Combine(config.VaultPath, destination, Path.GetFileName(trashFile));
                    destPath = NoteHelpers.EnsureInsideVault(config.VaultPath, destPath);
""",
    """                    string sourcePath;
                    try
                    {
                        sourcePath = _paths.ResolveVaultDeletePath(trashFile);
                    }
                    catch (VaultAccessDeniedException)
                    {
                        return KiokuError.AccessDenied();
                    }

                    var destPath = string.IsNullOrWhiteSpace(destination)
                        ? Path.Combine(config.VaultPath, Path.GetFileName(sourcePath))
                        : Path.Combine(config.VaultPath, destination, Path.GetFileName(sourcePath));
                    destPath = _paths.ResolveVaultWritePath(destPath);
""",
    "restore path validation",
)
replace_once(
    commands_path,
    "                    File.Move(trashFile, destPath);",
    """                    var restore = _paths.ResolveVaultMove(sourcePath, destPath);
                    File.Move(restore.Source, restore.Destination);""",
    "restore move validation",
)

policy_path = "src/Kioku.Mcp.Server/Services/VaultPathPolicy.cs"
replace_once(
    policy_path,
    "    private readonly StringComparison _pathComparison;\n",
    "",
    "remove unused comparison field",
)
replace_once(
    policy_path,
    """        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
""",
    "",
    "remove unused comparison assignment",
)
