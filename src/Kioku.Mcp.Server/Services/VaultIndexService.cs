using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Logging;
using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Central indexing service for the Obsidian vault.
/// Maintains an in-memory index of all .md notes and updates it
/// in real-time using FileSystemWatcher with debouncing.
/// </summary>
public sealed class VaultIndexService : IDisposable
{
    private readonly ILogger<VaultIndexService> _logger;
    private readonly string _vaultPath;

    // Main index: absolute path -> note
    private readonly ConcurrentDictionary<string, Note> _notesByPath = new(StringComparer.OrdinalIgnoreCase);

    // Inverted word index: word -> set of note paths
    private readonly ConcurrentDictionary<string, HashSet<string>> _wordIndex = new(StringComparer.OrdinalIgnoreCase);

    // Tag index: tag -> set of note paths
    private readonly ConcurrentDictionary<string, HashSet<string>> _tagIndex = new(StringComparer.OrdinalIgnoreCase);

    // Backlinks: target note name -> set of source file paths that link to it
    private readonly ConcurrentDictionary<string, HashSet<string>> _backlinkIndex = new(StringComparer.OrdinalIgnoreCase);

    // FileSystemWatcher and debouncing
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debouncers = new();
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    // Index state
    private int _indexedCount;
    private DateTimeOffset _lastIndexed;
    private bool _isReady;

    private readonly EmbeddingService? _embedding;
    private readonly HashSet<string> _excludeFolders = [];

    public VaultIndexService(ILogger<VaultIndexService> logger, KiokuConfiguration config, EmbeddingService? embedding = null, VaultConfigService? vaultConfig = null)
    {
        _logger = logger;
        _vaultPath = config.VaultPath;
        _embedding = embedding;
        if (vaultConfig is not null)
        {
            _excludeFolders = vaultConfig.ExcludeFolders;
        }
    }

    /// <summary>Total number of indexed notes.</summary>
    public int IndexedCount => _indexedCount;

    /// <summary>Date of the last full indexing.</summary>
    public DateTimeOffset LastIndexed => _lastIndexed;

    /// <summary>Indicates if the index has completed its initial load.</summary>
    public bool IsReady => _isReady;

    // Public API

    /// <summary>
    /// Initial load of all notes in the vault and starts the watcher.
    /// Must be called on server startup.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("Starting vault indexing: {Path}", _vaultPath);

        if (!Directory.Exists(_vaultPath))
        {
            _logger.Error("Vault path does not exist: {Path}", _vaultPath);
            throw new DirectoryNotFoundException($"Vault path not found: {_vaultPath}");
        }

        await IndexVaultAsync(cancellationToken);
        StartWatcher();

        if (_embedding is not null)
        {
            await _embedding.InitializeAsync(_notesByPath.Values, cancellationToken);
        }

        _isReady = true;
        _logger.Info("Index ready. {Count} notes indexed.", _indexedCount);
    }

    /// <summary>Gets a note by its absolute path or vault-relative path.</summary>
    public Note? GetNote(string path)
    {
        var absPath = ResolveAbsolutePath(path);
        return _notesByPath.TryGetValue(absPath, out var note) ? note : null;
    }

    /// <summary>Gets a note by its name (without extension).</summary>
    public Note? GetNoteByName(string name)
    {
        return _notesByPath.Values
            .FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns all indexed notes.</summary>
    public IEnumerable<Note> GetAllNotes() => _notesByPath.Values;

    /// <summary>Returns all notes in a specific folder.</summary>
    public IEnumerable<Note> GetNotesInFolder(string folderPath)
    {
        var absFolder = Path.IsPathRooted(folderPath)
            ? folderPath
            : Path.Combine(_vaultPath, folderPath);

        return _notesByPath.Values
            .Where(n => n.FilePath.StartsWith(absFolder, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Full-text search in the inverted index.
    /// Searches in titles, content, tags, and aliases.
    /// </summary>
    public IEnumerable<SearchResult> Search(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var queryWords = TokenizeQuery(query);
        var scores = new Dictionary<string, (float score, NoteMatchType matchType, string? snippet)>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in queryWords)
        {
            // Search in word index (content + title)
            if (_wordIndex.TryGetValue(word, out var contentPaths))
            {
                foreach (var path in contentPaths)
                {
                    if (!scores.TryGetValue(path, out var current))
                    {
                        scores[path] = (1.0f, NoteMatchType.ContentMatch, null);
                    }
                    else
                    {
                        scores[path] = (current.score + 1.0f, current.matchType, current.snippet);
                    }
                }
            }

            // Search in tag index (scores higher)
            if (_tagIndex.TryGetValue(word, out var tagPaths))
            {
                foreach (var path in tagPaths)
                {
                    if (!scores.TryGetValue(path, out var current))
                    {
                        scores[path] = (2.0f, NoteMatchType.TagMatch, null);
                    }
                    else
                    {
                        scores[path] = (current.score + 2.0f, NoteMatchType.TagMatch, current.snippet);
                    }
                }
            }
        }

        // Bonus for match in note title
        foreach (var (path, note) in _notesByPath)
        {
            if (queryWords.Any(w => note.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                if (scores.TryGetValue(path, out var current))
                {
                    scores[path] = (current.score + 3.0f, NoteMatchType.TitleMatch, current.snippet);
                }
                else
                {
                    scores[path] = (3.0f, NoteMatchType.TitleMatch, null);
                }
            }
        }

        return scores
            .Where(kv => _notesByPath.ContainsKey(kv.Key))
            .OrderByDescending(kv => kv.Value.score)
            .Take(maxResults)
            .Select(kv =>
            {
                var note = _notesByPath[kv.Key];
                return new SearchResult
                {
                    Note = note,
                    Score = Math.Min(1.0f, kv.Value.score / (queryWords.Count * 3.0f)),
                    MatchType = kv.Value.matchType,
                    Snippet = BuildSnippet(note.PlainText, queryWords),
                };
            });
    }

    /// <summary>Filters notes by frontmatter field.</summary>
    public IEnumerable<Note> FilterByMetadata(
        string? tag = null,
        string? status = null,
        string? noteType = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null)
    {
        return _notesByPath.Values.Where(note =>
        {
            if (tag is not null && !note.Metadata.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            if (status is not null && !string.Equals(note.Metadata.Status, status, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (noteType is not null && !string.Equals(note.Metadata.NoteType, noteType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (dateFrom.HasValue && note.Metadata.Date.HasValue && note.Metadata.Date < dateFrom)
            {
                return false;
            }

            if (dateTo.HasValue && note.Metadata.Date.HasValue && note.Metadata.Date > dateTo)
            {
                return false;
            }

            return true;
        });
    }

    /// <summary>Returns notes linking to the note with the given name.</summary>
    public IEnumerable<Note> GetBacklinks(string noteName)
    {
        if (!_backlinkIndex.TryGetValue(noteName.ToLowerInvariant(), out var paths))
        {
            return [];
        }

        return paths
            .Where(p => _notesByPath.ContainsKey(p))
            .Select(p => _notesByPath[p]);
    }

    /// <summary>Forces a full re-indexing of the vault.</summary>
    public async Task RebuildIndexAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("Full re-indexing requested.");
        _notesByPath.Clear();
        _wordIndex.Clear();
        _tagIndex.Clear();
        _backlinkIndex.Clear();
        _indexedCount = 0;
        _isReady = false;

        await IndexVaultAsync(cancellationToken);
        if (_embedding is not null)
        {
            await _embedding.SaveAsync();
        }

        _isReady = true;
        _logger.Info("Full re-indexing complete. {Count} notes.", _indexedCount);
    }

    // Indexing

    private async Task IndexVaultAsync(CancellationToken cancellationToken)
    {
        var mdFiles = Directory.EnumerateFiles(_vaultPath, "*.md", SearchOption.AllDirectories)
            .Where(p => !IsExcludedPath(p));
        var tasks = mdFiles.Select(path => IndexFileAsync(path, cancellationToken));
        await Task.WhenAll(tasks);
        _lastIndexed = DateTimeOffset.UtcNow;
    }

    private async Task IndexFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
            var note = BuildNote(filePath, content);

            _notesByPath[filePath] = note;
            AddToWordIndex(filePath, note.PlainText);
            AddToWordIndex(filePath, note.Name);

            foreach (var tag in note.Metadata.Tags)
            {
                AddToTagIndex(filePath, tag);
            }

            foreach (var link in note.OutgoingLinks)
            {
                AddToBacklinkIndex(note.FilePath, link);
            }

            if (_embedding is not null)
            {
                await _embedding.IndexNoteAsync(note);
            }

            Interlocked.Increment(ref _indexedCount);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("Could not index {File}: {Error}", filePath, ex.Message);
        }
    }

    private Note BuildNote(string filePath, string content)
    {
        var bodyStart = FrontmatterParser.GetBodyStart(content);
        var metadata = FrontmatterParser.Parse(content);
        var plainText = MarkdownTextExtractor.Extract(content, bodyStart);
        var outgoingLinks = MarkdownTextExtractor.ExtractWikilinks(content);
        var name = Path.GetFileNameWithoutExtension(filePath);
        var relativePath = Path.GetRelativePath(_vaultPath, filePath);

        return new Note
        {
            FilePath = filePath,
            VaultRelativePath = relativePath,
            Name = name,
            Metadata = metadata,
            RawContent = content,
            PlainText = plainText,
            OutgoingLinks = outgoingLinks,
            LastModified = File.GetLastWriteTimeUtc(filePath),
            ContentHash = ComputeHash(content),
        };
    }

    // FileSystemWatcher

    private void StartWatcher()
    {
        _watcher = new FileSystemWatcher(_vaultPath)
        {
            Filter = "*.md",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            InternalBufferSize = 65536, // 64KB — maximum recommended by .NET docs
            EnableRaisingEvents = true,
        };

        _watcher.Changed += (_, e) => ScheduleReindex(e.FullPath);
        _watcher.Created += (_, e) => ScheduleReindex(e.FullPath);
        _watcher.Deleted += (_, e) => RemoveFromIndex(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            if (IsExcludedPath(e.FullPath))
            {
                RemoveFromIndex(e.OldFullPath);
                return;
            }

            // Content is unchanged on a rename: re-key the embedding instead of dropping it,
            // so the re-index sees a matching hash and skips the Ollama round-trip.
            _embedding?.Move(e.OldFullPath, e.FullPath);
            RemoveFromIndex(e.OldFullPath, removeEmbedding: false);
            ScheduleReindex(e.FullPath);
        };
        _watcher.Error += (_, e) =>
            _logger.Warn("FileSystemWatcher error: {Error}", e.GetException().Message);

        _logger.Info("FileSystemWatcher active on: {Path}", _vaultPath);
    }

    private void ScheduleReindex(string filePath)
    {
        if (IsExcludedPath(filePath))
        {
            return;
        }

        // Cancel previous debouncer for this file (if any)
        if (_debouncers.TryRemove(filePath, out var existing))
        {
            existing.Cancel();
        }

        var cts = new CancellationTokenSource();
        _debouncers[filePath] = cts;

        // Re-index after the debounce period
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            _debouncers.TryRemove(filePath, out _);

            try
            {
                await IndexFileAsync(filePath);
                _logger.Debug("Re-indexed: {File}", Path.GetFileName(filePath));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Re-index failed: {File}", filePath);
            }
        });
    }

    private void RemoveFromIndex(string filePath, bool removeEmbedding = true)
    {
        if (!_notesByPath.TryRemove(filePath, out var note))
        {
            return;
        }

        // Remove from word index
        foreach (var (word, paths) in _wordIndex)
        {
            lock (paths)
            {
                paths.Remove(filePath);
            }
        }

        // Remove from tag index
        foreach (var (tag, paths) in _tagIndex)
        {
            lock (paths)
            {
                paths.Remove(filePath);
            }
        }

        // Remove from backlinks
        foreach (var link in note.OutgoingLinks)
        {
            if (_backlinkIndex.TryGetValue(link, out var backlinkPaths))
            {
                lock (backlinkPaths)
                {
                    backlinkPaths.Remove(filePath);
                }
            }
        }

        if (removeEmbedding)
        {
            _embedding?.Remove(filePath);
        }

        Interlocked.Decrement(ref _indexedCount);
        _logger.Debug("Removed from index: {File}", Path.GetFileName(filePath));
    }

    /// <summary>
    /// Synchronously updates the index after a file rename or move.
    /// Removes the old path and re-indexes the new path.
    /// Use this from tools that move/rename files to avoid race conditions with FileSystemWatcher.
    /// </summary>
    public async Task SynchronizeFileMoveAsync(string oldPath, string newPath)
    {
        _embedding?.Move(oldPath, newPath);
        RemoveFromIndex(oldPath, removeEmbedding: false);
        await IndexFileAsync(newPath);
    }

    /// <summary>
    /// Synchronously removes a file from the index after deletion.
    /// Use from tools that delete files to avoid race conditions with FileSystemWatcher.
    /// </summary>
    public void SynchronizeFileDelete(string filePath)
    {
        RemoveFromIndex(filePath);
    }

    /// <summary>
    /// Synchronously re-indexes a file (removes old entry, re-reads from disk).
    /// Use after reverting a file via git to refresh the in-memory index.
    /// </summary>
    public async Task SynchronizeFileReindexAsync(string filePath)
    {
        RemoveFromIndex(filePath);
        await IndexFileAsync(filePath);
    }

    // Indexes

    private void AddToWordIndex(string filePath, string text)
    {
        foreach (var word in Tokenize(text))
        {
            var paths = _wordIndex.GetOrAdd(word, _ => []);
            lock (paths)
            {
                paths.Add(filePath);
            }
        }
    }

    private void AddToTagIndex(string filePath, string tag)
    {
        var normalizedTag = tag.ToLowerInvariant();
        var paths = _tagIndex.GetOrAdd(normalizedTag, _ => []);
        lock (paths)
        {
            paths.Add(filePath);
        }
    }

    private void AddToBacklinkIndex(string sourceFilePath, string targetNoteName)
    {
        var paths = _backlinkIndex.GetOrAdd(targetNoteName.ToLowerInvariant(), _ => []);
        lock (paths)
        {
            paths.Add(sourceFilePath);
        }
    }

    // Helpers

    private string ResolveAbsolutePath(string path)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.Combine(_vaultPath, path);
    }

    private static IReadOnlyList<string> TokenizeQuery(string query) =>
        query.Split([' ', '\t', '\n', '\r', ',', '.', '!', '?'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Where(w => w.Length >= 2)
             .ToList();

    private static IEnumerable<string> Tokenize(string text) =>
        text.Split([' ', '\t', '\n', '\r', '-', '_', '.', ',', '!', '?', ':', ';', '(', ')', '[', ']', '{', '}'],
                   StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 2)
            .Select(w => w.ToLowerInvariant());

    private static string? BuildSnippet(string plainText, IReadOnlyList<string> queryWords)
    {
        if (plainText.Length == 0)
        {
            return null;
        }

        foreach (var word in queryWords)
        {
            int idx = plainText.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            int start = Math.Max(0, idx - 60);
            int end = Math.Min(plainText.Length, idx + word.Length + 60);
            var snippet = plainText[start..end].Trim();
            return (start > 0 ? "…" : "") + snippet + (end < plainText.Length ? "…" : "");
        }

        return plainText.Length > 120 ? plainText[..120].Trim() + "…" : plainText;
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(MD5.HashData(bytes));
    }

    private bool IsExcludedPath(string filePath)
    {
        var relative = Path.GetRelativePath(_vaultPath, filePath);

        // Exclude hidden paths (starting with .)
        if (relative.Split(Path.DirectorySeparatorChar).Any(segment => segment.StartsWith('.')))
        {
            return true;
        }

        // Exclude user-configured folders from .kioku/config.yml
        foreach (var exclude in _excludeFolders)
        {
            var normalized = exclude.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (relative.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                relative.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        foreach (var cts in _debouncers.Values)
        {
            cts.Cancel();
        }

        _watcher?.Dispose();
    }
}
