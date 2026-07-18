# Filesystem security boundary

Kioku treats the configured Obsidian vault as its default filesystem boundary. MCP tools must not read, write, move, restore, index, or delete files outside that boundary unless a narrowly scoped external read has been explicitly enabled.

## Default behavior

Given:

```bash
export KIOKU_VAULT_PATH=/home/user/notes
```

Kioku applies these rules:

- Relative paths are always resolved relative to `/home/user/notes`.
- Relative paths are never resolved from the server process working directory.
- Absolute paths outside the vault are denied.
- `..` traversal that would leave the vault is denied.
- Symbolic links and reparse points are resolved before authorization.
- Recursive vault and asset enumeration does not traverse linked directories.
- Both the source and destination are validated before a move, restore, rename, or soft delete.
- Permanent deletion is disabled.
- Filesystem authorization errors use `ACCESS_DENIED` and do not include absolute host paths.

These defaults apply to stdio and Streamable HTTP transports.

## External read-only imports

External reads are disabled by default. This primarily affects file-based BibTeX imports. Raw BibTeX content and `.bib` files stored inside the vault continue to work without additional configuration.

To allow reads from a specific external directory, both settings are required:

```bash
export KIOKU_ALLOW_EXTERNAL_READS=true
export KIOKU_EXTERNAL_READ_ROOTS=/home/user/reference-library
```

Multiple roots use the operating system path-list separator:

```bash
# Linux and macOS
export KIOKU_EXTERNAL_READ_ROOTS=/home/user/reference-library:/mnt/shared/bibliography

# Windows PowerShell
$env:KIOKU_EXTERNAL_READ_ROOTS = 'C:\Users\me\Reference;D:\Shared\Bibliography'
```

The allowlist grants **read-only** access. It does not authorize Kioku to create, modify, move, or delete files in those directories.

Use the narrowest practical roots. Do not allowlist a home directory, drive root, credentials folder, source-code checkout, or other broad location.

## Permanent deletion

Kioku uses soft deletion by default by moving notes to the vault trash. Irreversible deletion requires explicit opt-in:

```bash
export KIOKU_ALLOW_PERMANENT_DELETE=true
```

Even with the flag enabled, the target must resolve inside the configured vault. External files can never be permanently deleted through Kioku.

For routine use, leave permanent deletion disabled and restore files through `manage_trash` when necessary.

## Symbolic links and reparse points

Kioku canonicalizes path segments and resolves existing links before checking containment. A link inside the vault that targets an external directory does not extend the vault boundary.

The indexer and asset scanners skip linked directories during recursive enumeration. Direct attempts to access a linked path that resolves outside the vault return `ACCESS_DENIED`.

This policy also protects against:

- symlink-based path traversal on Linux and macOS;
- junctions and reparse points on Windows;
- nested links that ultimately resolve outside the vault;
- a source path inside the vault paired with an external move destination.

## Error behavior

Filesystem authorization failures return a stable response such as:

```text
[error] [ACCESS_DENIED] The requested filesystem operation is outside Kioku's configured security boundary.
```

The response intentionally omits the requested absolute path and the configured roots. Operational logs may contain administrator-facing diagnostics, but MCP clients do not receive unrelated host filesystem details.

## Deployment guidance

- Run Kioku as a dedicated, unprivileged operating-system user.
- Grant that user access only to the vault and intentionally allowlisted import roots.
- Prefer read-only permissions for external roots.
- Do not expose Streamable HTTP publicly without authentication and a trusted reverse proxy.
- Review vault symlinks before deployment.
- Keep soft delete enabled and permanent delete disabled unless there is a documented operational need.

The filesystem policy is a defense-in-depth boundary. Operating-system permissions remain the final security control.