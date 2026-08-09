using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Kioku.Mcp.Server.Domain;
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
    private readonly VaultPathPolicy _paths;
    private readonly VaultLinkResolver _linkResolver;
    private readonly string _vaultPath;

    // Main index: absolute path -> note
    private readonly ConcurrentDictionary<string, Note> _notesByPath = new(StringComparer.OrdinalIgnoreCase);

    // Inverted word index: word -> postings (note path -> term frequency)
    private readonly ConcurrentDictionary<string, Dictionary<string, int>> _wordIndex = new(StringComparer.OrdinalIgnoreCase);

    // Indexed token count per note path, for BM25 document-length normalization
    private readonly ConcurrentDictionary<string, int> _docLengths = new(StringComparer.OrdinalIgnoreCase);

    // Tag index: tag -> set of note paths
    private readonly ConcurrentDictionary<string, HashSet<string>> _tagIndex = new(StringComparer.OrdinalIgnoreCase);

    // Backlinks are keyed by canonical target identity, never by raw wikilink spelling.
    private readonly object _backlinkIndexGate = new();
    private Dictionary<string, HashSet<string>> _backlinkIndex = new(StringComparer.OrdinalIgnoreCase);

    // FileSystemWatcher and debouncing
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debouncers = new();
    private readonly ConcurrentDictionary<string, PendingWatcherDelete> _pendingWatcherDeletes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    internal Func<string, Task>? WatcherDeleteBeforeEmbeddingRemoveAsync { get; set; }

    // Index state
    private int _indexedCount;
    private DateTimeOffset _lastIndexed;
    private bool _isReady;

    private readonly EmbeddingService? _embedding;
    private readonly HashSet<string> _excludeFolders = [];

    public VaultIndexService(
        ILogger<VaultIndexService> logger,
        KiokuConfiguration config,
        EmbeddingService? embedding = null,
        VaultConfigService? vaultConfig = null,
        VaultPathPolicy? pathPolicy = null)
    {
        _logger = logger;
        _paths = pathPolicy ?? new VaultPathPolicy(config);
        _vaultPath = _paths.VaultRoot;
        _linkResolver = new VaultLinkResolver(_paths, () => _notesByPath.Values.ToArray());
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
    public bool IsReady => Volatile.Read(ref _isReady);

    /// <summary>Synchronizes readiness with the bounded indexing pipeline.</summary>
    internal void SetReady(bool ready) => Volatile.Write(ref _isReady, ready);

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

        SetReady(true);
        _logger.Info("Index ready. {Count} notes indexed.", _indexedCount);
    }

    /// <summary>Gets a note by its absolute path or vault-relative path.</summary>
    public Note? GetNote(string path)
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

    /// <summary>Gets a note by its name (without extension).</summary>
    public Note? GetNoteByName(string name)
    {
        return GetNotesByName(name) is [var note] ? note : null;
    }

    /// <summary>Gets all notes with the given basename.</summary>
    public IReadOnlyList<Note> GetNotesByName(string name) =>
        _notesByPath.Values
            .Where(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// Resolves note lookup input through the same canonical identity resolver used by graph,
    /// audit, and backlinks. Ambiguous inputs intentionally do not select an arbitrary note.
    /// </summary>
    public Note? ResolveNote(string nameOrPath) => _linkResolver.ResolveNote(nameOrPath).Note;

    /// <summary>Returns the typed canonical classification for a wikilink target.</summary>
    public VaultLinkResolution ResolveLinkResult(Note source, string link) => _linkResolver.Resolve(source, link);

    /// <summary>Compatibility wrapper that returns only uniquely resolved indexed notes.</summary>
    public Note? ResolveLink(Note source, string link) => ResolveLinkResult(source, link).Note;

    /// <summary>Returns all indexed notes.</summary>
    public IEnumerable<Note> GetAllNotes() => _notesByPath.Values;

    /// <summary>Returns all notes in a specific folder.</summary>
    public IEnumerable<Note> GetNotesInFolder(string folderPath)
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

    // Okapi BM25 constants: k1 controls term-frequency saturation, b controls how much
    // document length normalizes the score.
    private const float Bm25K1 = 1.2f;
    private const float Bm25B = 0.75f;

    // Title/tag bonuses, relative to the strongest BM25 content score of the query so they
    // stay meaningful at any score scale.
    private const float TitleBoost = 0.5f;
    private const float TagBoost = 0.3f;

    /// <summary>
    /// Full-text search over the inverted index using Okapi BM25 scoring
    /// (IDF-weighted, term-frequency-saturated, document-length-normalized),
    /// with relative bonuses for tag and title matches.
    /// Scores are normalized to [0, 1] relative to the best candidate.
    /// </summary>
    public IEnumerable<SearchResult> Search(string query, int maxResults = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var queryWords = TokenizeQuery(query);
        var totalDocs = Math.Max(1, _notesByPath.Count);
        var avgDocLength = _docLengths.IsEmpty ? 1f : (float)_docLengths.Values.Average();

        var bm25 = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var tagMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in queryWords)
        {
            if (_wordIndex.TryGetValue(word, out var postings))
            {
                KeyValuePair<string, int>[] snapshot;
                lock (postings)
                {
                    snapshot = [.. postings];
                }

                if (snapshot.Length > 0)
                {
                    var idf = MathF.Log(1f + (totalDocs - snapshot.Length + 0.5f) / (snapshot.Length + 0.5f));
                    foreach (var (path, tf) in snapshot)
                    {
                        var docLength = _docLengths.TryGetValue(path, out var dl) ? dl : avgDocLength;
                        var norm = tf * (Bm25K1 + 1f) / (tf + Bm25K1 * (1f - Bm25B + Bm25B * docLength / avgDocLength));
                        bm25[path] = bm25.GetValueOrDefault(path) + idf * norm;
                    }
                }
            }

            if (_tagIndex.TryGetValue(word, out var tagPaths))
            {
                lock (tagPaths)
                {
                    tagMatches.UnionWith(tagPaths);
                }
            }
        }

        var titleMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, note) in _notesByPath)
        {
            if (queryWords.Any(w => note.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                titleMatches.Add(path);
            }
        }

        // Tag/title bonuses scale with the query's strongest content score so they neither
        // drown in it nor dominate it; when nothing matched content, they rank on their own.
        var boostUnit = bm25.Count > 0 ? Math.Max(bm25.Values.Max(), 1e-6f) : 1f;

        var scores = new Dictionary<string, (float score, NoteMatchType matchType)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in bm25.Keys.Concat(tagMatches).Concat(titleMatches))
        {
            if (scores.ContainsKey(path) || !_notesByPath.ContainsKey(path))
            {
                continue;
            }

            var score = bm25.GetValueOrDefault(path);
            var matchType = NoteMatchType.ContentMatch;
            if (tagMatches.Contains(path))
            {
                score += TagBoost * boostUnit;
                matchType = NoteMatchType.TagMatch;
            }

            if (titleMatches.Contains(path))
            {
                score += TitleBoost * boostUnit;
                matchType = NoteMatchType.TitleMatch;
            }

            scores[path] = (score, matchType);
        }

        if (scores.Count == 0)
        {
            return [];
        }

        var maxScore = scores.Values.Max(s => s.score);
        return scores
            .OrderByDescending(kv => kv.Value.score)
            .Take(maxResults)
            .Select(kv =>
            {
                var note = _notesByPath[kv.Key];
                return new SearchResult(
                    note,
                    maxScore > 0f ? kv.Value.score / maxScore : 0f,
                    kv.Value.matchType,
                    BuildSnippet(note.PlainText, queryWords));
            })
            .ToList();
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

            if (dateFrom.HasValue && (!note.Metadata.Date.HasValue || note.Metadata.Date < dateFrom))
            {
                return false;
            }

            if (dateTo.HasValue && (!note.Metadata.Date.HasValue || note.Metadata.Date > dateTo))
            {
                return false;
            }

            return true;
        });
    }

    /// <summary>
    /// Returns notes linking to the uniquely resolved note with the given name or path. If the
    /// target spelling was just invalidated by a move/rename, a compatibility scan finds only
    /// unresolved raw links that still refer to that historical spelling so link rewriting can
    /// complete without changing canonical backlink indexing semantics.
    /// </summary>
    public IReadOnlyList<Note> GetBacklinks(string noteNameOrPath)
    {
        var target = ResolveNote(noteNameOrPath);
        if (target is not null)
        {
            return GetBacklinks(target);
        }

        var query = NormalizeVaultPath(noteNameOrPath);
        if (query.Length == 0)
        {
            return [];
        }

        return _notesByPath.Values
            .Where(source => source.OutgoingLinks.Any(link =>
            {
                // Never reinterpret a spelling that currently resolves to a valid target. This
                // protects literal '#' filenames from being mistaken for historical fragments.
                if (ResolveLinkResult(source, link).Status == VaultLinkResolutionStatus.Resolved)
                {
                    return false;
                }

                var raw = NormalizeVaultPath(link);
                return raw.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                       raw.StartsWith(query + "#", StringComparison.OrdinalIgnoreCase);
            }))
            .ToList();
    }

    /// <summary>Returns notes linking to a specific canonical note identity.</summary>
    public IReadOnlyList<Note> GetBacklinks(Note target)
    {
        var targetPath = NormalizeVaultPath(target.VaultRelativePath);
        HashSet<string>? sourcePaths;
        lock (_backlinkIndexGate)
        {
            sourcePaths = _backlinkIndex.TryGetValue(targetPath, out var paths)
                ? new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase)
                : null;
        }

        return sourcePaths is null
            ? []
            : MaterializeBacklinks(sourcePaths);
    }

    internal List<Note> FindConnectedComponent(Note startNote, HashSet<string> visited)
    {
        var component = new List<Note>();
        var queue = new Queue<Note>();
        queue.Enqueue(startNote);
        visited.Add(startNote.FilePath);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            component.Add(current);

            foreach (var link in current.OutgoingLinks)
            {
                var linkedNote = ResolveLink(current, link);
                if (linkedNote is not null && visited.Add(linkedNote.FilePath))
                {
                    queue.Enqueue(linkedNote);
                }
            }

            foreach (var backlink in GetBacklinks(current))
            {
                if (visited.Add(backlink.FilePath))
                {
                    queue.Enqueue(backlink);
                }
            }
        }

        return component;
    }

    private List<Note> MaterializeBacklinks(IEnumerable<string> sourcePaths) =>
        sourcePaths
            .Select(path => _notesByPath.TryGetValue(path, out var note) ? note : null)
            .Where(note => note is not null)
            .Cast<Note>()
            .ToList();

    /// <summary>Forces a full re-indexing of the vault.</summary>
    public async Task RebuildIndexAsync(CancellationToken cancellationToken = default)
    {
        _logger.Info("Full re-indexing requested.");
        _notesByPath.Clear();
        _wordIndex.Clear();
        _docLengths.Clear();
        _tagIndex.Clear();
        lock (_backlinkIndexGate)
        {
            _backlinkIndex = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        }
        _indexedCount = 0;
        SetReady(false);

        await IndexVaultAsync(cancellationToken);
        if (_embedding is not null)
        {
            await _embedding.SaveAsync(cancellationToken);
        }

        SetReady(true);
        _logger.Info("Full re-indexing complete. {Count} notes.", _indexedCount);
    }

    // Indexing

    private async Task IndexVaultAsync(CancellationToken cancellationToken)
    {
        var mdFiles = _paths.EnumerateVaultFiles("*.md", recursive: true)
            .Where(path => !IsExcludedPath(path));
        var tasks = mdFiles.Select(path => IndexFileAsync(path, rebuildBacklinks: false, cancellationToken));
        await Task.WhenAll(tasks);
        RebuildBacklinkIndex();
        _lastIndexed = DateTimeOffset.UtcNow;
    }

    private async Task IndexFileAsync(
        string filePath,
        bool rebuildBacklinks = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            filePath = _paths.ResolveVaultReadPath(filePath);
            if (!File.Exists(filePath))
            {
                return;
            }

            var content = await NoteHelpers.ReadAllTextAsync(filePath, cancellationToken);
            var note = BuildNote(filePath, content);

            // Purge stale postings first, so an edited note doesn't keep matching words it
            // no longer contains (keeps BM25 term/length statistics exact after edits).
            if (_notesByPath.ContainsKey(filePath))
            {
                RemoveFromIndex(filePath, removeEmbedding: false, rebuildBacklinks: false);
            }

            _notesByPath[filePath] = note;
            var tokenCount = AddToWordIndex(filePath, note.PlainText);
            tokenCount += AddToWordIndex(filePath, note.Name);
            _docLengths[filePath] = tokenCount;

            foreach (var tag in note.Metadata.Tags)
            {
                AddToTagIndex(filePath, tag);
            }

            if (_embedding is not null)
            {
                await _embedding.IndexNoteAsync(note, cancellationToken);
            }

            Interlocked.Increment(ref _indexedCount);
            if (rebuildBacklinks)
            {
                RebuildBacklinkIndex();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not index {File}", filePath);
        }
    }

    private Note BuildNote(string filePath, string content)
    {
        var bodyStart = FrontmatterParser.GetBodyStart(content);
        var metadata = FrontmatterParser.Parse(content);
        var plainText = MarkdownTextExtractor.Extract(content, bodyStart);
        var outgoingLinks = MarkdownTextExtractor.ExtractWikilinks(content);
        var name = Path.GetFileNameWithoutExtension(filePath);
        var relativePath = Path.GetRelativePath(_vaultPath, filePath).Replace('\\', '/');

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
        _watcher.Deleted += (_, e) => ScheduleWatcherDelete(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            if (IsExcludedPath(e.FullPath))
            {
                CancelPendingWatcherDelete(e.OldFullPath);
                RemoveFromIndex(e.OldFullPath);
                return;
            }

            // Content is unchanged on a rename: re-key the embedding instead of dropping it,
            // so the re-index sees a matching hash and skips the Ollama round-trip.
            CancelPendingWatcherDelete(e.OldFullPath);
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

    private void ScheduleWatcherDelete(string filePath)
    {
        if (IsExcludedPath(filePath))
        {
            return;
        }

        // A move can surface as delete/create on some platforms. Keep the embedding briefly so
        // SynchronizeFileMoveAsync can re-key it before a real deletion is finalized.
        RemoveFromIndex(filePath, removeEmbedding: false);
        if (_pendingWatcherDeletes.TryRemove(filePath, out var previous))
        {
            CancelPendingWatcherDelete(previous);
        }

        var pending = new PendingWatcherDelete();
        _pendingWatcherDeletes[filePath] = pending;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, pending.Cancellation.Token);
                if (WatcherDeleteBeforeEmbeddingRemoveAsync is { } beforeRemove)
                {
                    await beforeRemove(filePath);
                }

                lock (pending.Gate)
                {
                    pending.Cancellation.Token.ThrowIfCancellationRequested();
                    if (!File.Exists(filePath))
                    {
                        _embedding?.Remove(filePath);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (_pendingWatcherDeletes.TryGetValue(filePath, out var current) &&
                    ReferenceEquals(current, pending))
                {
                    _pendingWatcherDeletes.TryRemove(filePath, out _);
                }

                pending.Dispose();
            }
        });
    }

    private void CancelPendingWatcherDelete(string filePath)
    {
        if (_pendingWatcherDeletes.TryRemove(filePath, out var pending))
        {
            CancelPendingWatcherDelete(pending);
        }
    }

    private static void CancelPendingWatcherDelete(PendingWatcherDelete pending)
    {
        lock (pending.Gate)
        {
            pending.Cancellation.Cancel();
        }
    }

    private void RemoveFromIndex(
        string filePath,
        bool removeEmbedding = true,
        bool rebuildBacklinks = true)
    {
        if (!_notesByPath.TryRemove(filePath, out var note))
        {
            return;
        }

        // Remove from word index — only the note's own tokens, not the whole vocabulary
        var noteWords = Tokenize(note.PlainText)
            .Concat(Tokenize(note.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var word in noteWords)
        {
            if (_wordIndex.TryGetValue(word, out var postings))
            {
                lock (postings)
                {
                    postings.Remove(filePath);
                }
            }
        }

        _docLengths.TryRemove(filePath, out _);

        // Remove from tag index
        foreach (var (_, paths) in _tagIndex)
        {
            lock (paths)
            {
                paths.Remove(filePath);
            }
        }

        if (removeEmbedding)
        {
            _embedding?.Remove(filePath);
        }

        Interlocked.Decrement(ref _indexedCount);
        if (rebuildBacklinks)
        {
            RebuildBacklinkIndex();
        }
        _logger.Debug("Removed from index: {File}", Path.GetFileName(filePath));
    }

    /// <summary>
    /// Synchronously updates the index after a file rename or move.
    /// Removes the old path and re-indexes the new path.
    /// Never throws — same contract as <see cref="SynchronizeFileReindexAsync"/>.
    /// </summary>
    public async Task SynchronizeFileMoveAsync(string oldPath, string newPath)
    {
        try
        {
            var move = _paths.ResolveVaultMove(oldPath, newPath);
            CancelPendingWatcherDelete(move.Source);
            CancelPendingWatcherDelete(move.Destination);
            _embedding?.Move(move.Source, move.Destination);
            RemoveFromIndex(move.Source, removeEmbedding: false, rebuildBacklinks: false);
            await IndexFileAsync(move.Destination);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "SynchronizeFileMoveAsync failed for {OldPath} -> {NewPath}. Index may be stale.", oldPath, newPath);
        }
    }

    /// <summary>
    /// Synchronously removes a file from the index after deletion.
    /// Use from tools that delete files to avoid race conditions with FileSystemWatcher.
    /// </summary>
    public void SynchronizeFileDelete(string filePath)
    {
        if (_paths.IsInsideVault(filePath))
        {
            CancelPendingWatcherDelete(filePath);
            RemoveFromIndex(filePath);
        }
    }

    /// <summary>
    /// Synchronously re-indexes a file (removes old entry, re-reads from disk).
    /// Use after reverting a file via git to refresh the in-memory index.
    /// Never throws — callers (MCP tools that already wrote the file) must not crash
    /// from a re-index failure. The index self-heals via FileSystemWatcher debouncing.
    /// </summary>
    public async Task SynchronizeFileReindexAsync(string filePath)
    {
        try
        {
            CancelPendingWatcherDelete(filePath);
            RemoveFromIndex(filePath, rebuildBacklinks: false);
            await IndexFileAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "SynchronizeFileReindexAsync failed for {File}. Index may be stale until the next FileSystemWatcher debounce.", filePath);
        }
    }

    // Indexes

    private int AddToWordIndex(string filePath, string text)
    {
        var count = 0;
        foreach (var word in Tokenize(text))
        {
            var postings = _wordIndex.GetOrAdd(word, _ => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
            lock (postings)
            {
                postings[filePath] = postings.GetValueOrDefault(filePath) + 1;
            }

            count++;
        }

        return count;
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

    private void RebuildBacklinkIndex()
    {
        var rebuilt = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var notes = _notesByPath.Values.ToArray();
        foreach (var source in notes)
        {
            foreach (var link in source.OutgoingLinks)
            {
                var resolution = ResolveLinkResult(source, link);
                if (!resolution.IsResolved || string.IsNullOrWhiteSpace(resolution.CanonicalTargetPath))
                {
                    continue;
                }

                var targetPath = NormalizeVaultPath(resolution.CanonicalTargetPath);
                if (!rebuilt.TryGetValue(targetPath, out var sources))
                {
                    sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    rebuilt[targetPath] = sources;
                }

                sources.Add(source.FilePath);
            }
        }

        lock (_backlinkIndexGate)
        {
            _backlinkIndex = rebuilt;
        }
    }

    // Helpers

    private static bool IsPathWithin(string directory, string candidate)
    {
        var relative = Path.GetRelativePath(directory, candidate);
        return relative == "." ||
            (!Path.IsPathRooted(relative) &&
             relative != ".." &&
             !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string NormalizeVaultPath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/').TrimStart('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^3]
            : normalized;
    }

    private static List<string> TokenizeQuery(string query) =>
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
        // MD5 is a persisted non-security content identity for the embedding cache.
        var bytes = Encoding.UTF8.GetBytes(content);
        return Convert.ToHexString(MD5.HashData(bytes));
    }

    private bool IsExcludedPath(string filePath)
    {
        if (!_paths.IsInsideVault(filePath))
        {
            return true;
        }

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

        foreach (var pending in _pendingWatcherDeletes.Values)
        {
            CancelPendingWatcherDelete(pending);
        }

        _watcher?.Dispose();
    }

    private sealed class PendingWatcherDelete : IDisposable
    {
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly object Gate = new();

        public void Dispose() => Cancellation.Dispose();
    }
}
