# 01 — Diagnosis & Bug Inventory

Diagnostic only — nothing here has been changed. Each entry has a severity, a verified
`file:line`, the impact, how to trigger it, and a fix sketch. The end of the doc groups everything
into a P0/P1/P2 backlog.

> Severity: 🔴 high · 🟡 medium · 🟢 low · ✅ verified safe (kept for the record)

---

## 🔴 BUG-1 — Path traversal in `move_note` / `rename_note`

**Where:** `src/Kioku.Mcp.Server/Tools/NoteCommandTools.cs:235` (move) and `:266` (rename, via
`BuildFilePath`); root cause in `src/Kioku.Mcp.Server/Services/NoteHelpers.cs:52`.

```csharp
// NoteCommandTools.cs:235  — move_note
var destDir = Path.Combine(config.VaultPath, destination_folder);   // unchecked
// NoteHelpers.cs:52  — BuildFilePath
return Path.Combine(vaultPath, normalized);                          // unchecked
```

`Path.Combine(vaultPath, x)` returns `x` unchanged if `x` is absolute, and happily walks out with
`../`. Neither path is canonicalized or checked to remain inside the vault.

**Impact:** An agent (or a prompt-injected instruction inside a note the agent is reading) can move
or write files **outside** the vault — e.g. `destination_folder = "../../.ssh"` or
`new_name = "/home/user/.bashrc"`. For a tool whose entire job is to mutate the filesystem on behalf
of an LLM, this is the most important issue to close.

**Trigger:** `move_note(note: "x", destination_folder: "../../tmp/evil")`.

**Fix sketch:** add one containment helper and call it from every write/move/rename/create/delete
tool:

```csharp
static string EnsureInsideVault(string vaultRoot, string candidate)
{
    var root = Path.GetFullPath(vaultRoot);
    var full = Path.GetFullPath(candidate);
    var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
    if (!full.StartsWith(rootWithSep, StringComparison.Ordinal) && full != root)
        throw new InvalidOperationException("Path escapes the vault.");
    return full;
}
```

Audit **all** write tools (`create_note`, `update_*`, `move_note`, `rename_note`, `delete_note`,
`delete_folder`, asset/attachment movers) to route through `BuildFilePath` → `EnsureInsideVault`.

---

## 🟡 BUG-2 — Fire-and-forget reindex swallows exceptions

**Where:** `src/Kioku.Mcp.Server/Services/VaultIndexService.cs:388`

```csharp
_ = Task.Delay(DebounceDelay, cts.Token)
    .ContinueWith(async t =>
    {
        if (t.IsCanceled) return;
        _debouncers.TryRemove(filePath, out _);
        await IndexFileAsync(filePath);      // exceptions here vanish
        _logger.Debug("Re-indexed: {File}", Path.GetFileName(filePath));
    }, TaskScheduler.Default);
```

Two problems: (a) the discarded task (`_ =`) means any exception is unobserved, and (b)
`ContinueWith(async …)` returns a `Task<Task>`, so the inner async work isn't awaited by the outer
continuation — failures inside `IndexFileAsync` are silently lost. The index can drift stale with no
log line.

**Impact:** Search returns results from an out-of-date index after a file write fails to reindex; no
signal to the user. Medium because `IndexFileAsync` already catches `IOException`/
`UnauthorizedAccessException` (`:315`), but anything else (parse, OOM, cancellation races) disappears.

**Fix sketch:** use `async`/`await` with `Task.Run` and a top-level try/catch that logs:

```csharp
_ = Task.Run(async () =>
{
    try { await Task.Delay(DebounceDelay, cts.Token); }
    catch (TaskCanceledException) { return; }
    _debouncers.TryRemove(filePath, out _);
    try { await IndexFileAsync(filePath); _logger.Debug("Re-indexed: {File}", ...); }
    catch (Exception ex) { _logger.Error(ex, "Re-index failed: {File}", filePath); }
});
```

---

## 🟡 BUG-3 — Embedding dimension/model mismatch silently truncates

**Where:** `src/Kioku.Mcp.Server/Services/EmbeddingService.cs:271-273` (cosine) and the binary
cache in `EmbeddingPersistence`.

```csharp
private static float CosineSimilarity(float[] a, float[] b)
{
    var len = Math.Min(a.Length, b.Length);   // silently truncates to the shorter vector
    ...
}
```

The cache (`.kioku/embeddings.bin`) does not record the embedding **model name** or **dimension**.
If a user changes `KIOKU_EMBEDDING_MODEL` (e.g. `nomic-embed-text` 768-dim → `mxbai-embed-large`
1024-dim), old vectors are loaded and compared via `Math.Min`, producing meaningless similarities
with no error.

**Impact:** Semantic search returns plausible-but-wrong results after a model change — hard to
notice, erodes trust in the flagship feature.

**Fix sketch:** stamp the cache header with `{modelName, dim}`. On load, if either differs from the
configured model, discard the cache and re-embed (or refuse and warn). Optionally assert
`a.Length == b.Length` and skip mismatched entries.

---

## 🟡 BUG-4 — Static `HttpClient` is disposed; `ResearchTools` news up its own

**Where:** `EmbeddingService.cs:19` (static client), `:310` (`Dispose`), `Tools/ResearchTools.cs:328`.

```csharp
private static readonly HttpClient _http = new(new SocketsHttpHandler { ... });   // :19
public void Dispose() => _http.Dispose();                                          // :310
// ResearchTools.cs:328
using var http = new HttpClient();                                                 // new per call
```

`EmbeddingService` is a DI **singleton**, so in practice `Dispose` rarely runs — but if it ever does
(test teardown, host shutdown ordering), it disposes a `static` client shared process-wide, breaking
any later use. Separately, `ResearchTools` creating a fresh `HttpClient` per call risks socket
exhaustion under load and ignores the tuned `SocketsHttpHandler`.

**Impact:** Low-to-medium; latent footgun rather than an active failure.

**Fix sketch:** register `IHttpClientFactory` (`builder.Services.AddHttpClient("ollama", …)`), inject
named clients into `EmbeddingService` and `ResearchTools`, and drop the manual static + `Dispose`.

---

## 🟡 BUG-5 — Plugin: bridge-startup failure is silent (regression)

**Where:** `src/obsidian-kioku-mcp/src/bridge.ts:52`

```ts
this.wss.on("error", (err) => {
  log.error(`Could not start the bridge: ${err.message}`);   // console only — no Notice
});
```

A prior version surfaced a `Notice` to the user on startup failure; the refactor dropped it. The most
common failure (port 7765 already in use) now fails invisibly — the user thinks the bridge is up.

**Impact:** Confusing "why won't the agent open my note?" support burden. The plugin already uses
`Notice` elsewhere (`main.ts:24` on restart) and has a settings tab, so the pattern exists.

**Fix sketch:** in the `error` handler, if `settings.showNotifications`, raise
`new Notice("[error] Kioku bridge: " + err.message)` — especially for `EADDRINUSE`.

---

## 🟡 BUG-6 — Plugin: `open-file` is fire-and-forget in a sync handler

**Where:** `src/obsidian-kioku-mcp/src/handlers.ts:69`

```ts
void app.workspace.openLinkText(path, "", false);   // promise discarded; handler is not async
return { requestId, success: true };                 // reports success before the open resolves
```

If `openLinkText` rejects (missing file, workspace busy), the rejection is swallowed and the bridge
still reports `success: true`.

**Fix sketch:** make `cmdOpenFile` `async` and `await` the call inside try/catch, returning
`success: false` with the error on failure. (`CommandHandler` already allows `Promise<BridgeResponse>`.)

---

## 🟡 BUG-7 — Plugin: no payload validation; unchecked `as` casts

**Where:** `handlers.ts:10,22,34-48` (registry) and the `cmd*` bodies, e.g. `:62`.

```ts
"open-file": (p, requestId) => cmdOpenFile(app, settings, p as { path: string }, requestId),
```

Every handler casts `payload` with `as { … }` and trusts it. A malformed/empty payload
(`{"command":"open-file"}`) makes `const { path } = payload` yield `undefined`, and downstream
Obsidian calls throw. Errors are caught by the outer dispatch try/catch (`bridge.ts:86`), so it won't
crash Obsidian — but the error messages are cryptic (`Cannot destructure property 'path'…`).

**Fix sketch:** a tiny per-command validator (or `zod`-style guards) that returns a clean
`{ success: false, error: "missing required field: path" }`. Pairs naturally with BUG-8.

---

## 🟡 BUG-8 — Bridge protocol types are duplicated across C# and TS (drift risk)

**Where:** `src/obsidian-kioku-mcp/src/types.ts:40-53` ↔
`src/Kioku.Mcp.Server/Services/ObsidianBridgeService.cs:249-270`.

Both sides hand-declare `BridgeMessage`/`BridgeResponse` (`command`, `payload`, `requestId`,
`success`, `data`, `error`). They agree **today**, but there is no single source of truth — a field
rename on one side silently breaks the bridge with no compile-time signal.

**Fix sketch:** define the protocol once (a small JSON Schema, or generate TS from the C# records /
vice-versa) and emit both. At minimum, add a `protocolVersion` field and a contract test (see
[06](./06-testing-strategy.md)).

---

## 🟢 BUG-9 — `Console.Error.WriteLine` for early config error

**Where:** `Program.cs:17` — runs before DI/logging is built, so it's defensible, but it's the one
place that bypasses `ILogger<T>`. Low priority; consider a minimal bootstrap logger or just annotate
it as intentional.

---

## 🟢 BUG-10 — `get-app-version` reads version from the plugin registry

**Where:** `handlers.ts` (`cmdGetAppVersion`) looks up `manifests["kioku-mcp"]?.version ?? "unknown"`
instead of the `Plugin.manifest` the class already holds. Slightly more fragile; returns `"unknown"`
if the registry shape changes. Trivial fix: thread the manifest in from `main.ts`.

---

## ✅ Verified safe (not bugs) — corrections to earlier suspicions

- **Frontmatter date parsing is safe.** `FrontmatterParser` uses `DateOnly.TryParse` via
  `TryParseDate` (`FrontmatterParser.cs:335`), not `DateOnly.Parse`. Bad YAML dates degrade to
  `null`, they do not throw. (An earlier pass flagged this as a risk — it is not.)
- **WebSocket is localhost-only.** `bridge.ts:19` binds `127.0.0.1`; the HTTP transport also binds
  `localhost` (`Program.cs:126`). No 0.0.0.0 exposure by default. ✅
- **JSON parsing is guarded.** `bridge.ts:34` wraps `JSON.parse` in try/catch and returns a clean
  error. ✅
- **Plugin lifecycle is clean.** `onunload` closes clients and the server, clears collections
  (`bridge.ts`), no obvious leak. ✅
- **Indices are thread-safe.** `ConcurrentDictionary` + `Interlocked` + per-bucket locks. ✅

---

## Concurrency notes (watch, not bugs yet)

- `RebuildIndexAsync` clears dictionaries while the `FileSystemWatcher` may still fire; mitigated by
  the single-threaded tool-execution model but not hardened. Consider a rebuild lock / generation
  counter.
- On some Linux/network filesystems `FileSystemWatcher` can miss events; document/enable
  `DOTNET_USE_POLLING_FILE_WATCHER=1` as a fallback (see [03](./03-mcp-server-improvements.md)).

---

## Prioritized backlog

| ID | Title | Sev | Priority | Effort |
|----|-------|:---:|:--------:|:------:|
| BUG-1 | Vault path-traversal containment | 🔴 | **P0** | S |
| BUG-5 | Restore bridge-startup `Notice` | 🟡 | **P0** | XS |
| BUG-2 | Reindex exception handling | 🟡 | P1 | S |
| BUG-3 | Embedding model/dim stamping + invalidation | 🟡 | P1 | M |
| BUG-6 | `open-file` async/await + error | 🟡 | P1 | XS |
| BUG-7 | Payload validation layer | 🟡 | P1 | S |
| BUG-8 | Shared/versioned bridge protocol | 🟡 | P1 | M |
| BUG-4 | `IHttpClientFactory` everywhere | 🟡 | P2 | S |
| BUG-9 | Bootstrap logger for early errors | 🟢 | P2 | XS |
| BUG-10 | `get-app-version` from `Plugin.manifest` | 🟢 | P2 | XS |

P0 items should land before any wider distribution push (see [07](./07-production-readiness.md)).

---

## Implementation Status

| ID | Task | Branch | PR | Status |
|----|------|--------|----|--------|
| BUG-1 | Vault path-traversal containment | `fix/p0-path-traversal` | — | pending |
| BUG-5 | Restore bridge-startup Notice | `fix/p0-bridge-notice` | — | pending |
| BUG-2 | Reindex exception handling | — | — | pending |
| BUG-3 | Embedding model/dim stamping | — | — | pending |
| BUG-6 | open-file async/await | — | — | pending |
| BUG-7 | Payload validation layer | — | — | pending |
| BUG-8 | Shared/versioned bridge protocol | — | — | pending |
| BUG-4 | IHttpClientFactory everywhere | — | — | pending |
| BUG-9 | Bootstrap logger | — | — | pending |
| BUG-10 | get-app-version from manifest | — | — | pending |
