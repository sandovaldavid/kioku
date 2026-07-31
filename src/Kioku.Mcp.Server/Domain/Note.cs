namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Represents a Markdown note from the Obsidian vault.
/// </summary>
public sealed class Note
{
    /// <summary>Absolute path to the .md file on disk.</summary>
    public required string FilePath { get; init; }

    /// <summary>Relative path to the root of the vault.</summary>
    public required string VaultRelativePath { get; init; }

    /// <summary>Filename without extension (title of the note).</summary>
    public required string Name { get; init; }

    /// <summary>Metadata extracted from the YAML frontmatter.</summary>
    public NoteMetadata Metadata { get; init; } = NoteMetadata.Empty;

    /// <summary>Full content of the file (including frontmatter).</summary>
    public required string RawContent { get; init; }

    /// <summary>Clean text without Markdown syntax or frontmatter (for indexing).</summary>
    public required string PlainText { get; init; }

    /// <summary>Wikilinks that this note references (outgoing).</summary>
    public IReadOnlyList<string> OutgoingLinks { get; init; } = [];

    /// <summary>Date of the last modification of the file.</summary>
    public DateTimeOffset LastModified { get; init; }

    /// <summary>MD5 hash of the content — to detect changes without reading the full file.</summary>
    public required string ContentHash { get; init; }

    /// <summary>SHA-256 revision token used for optimistic concurrency checks.</summary>
    public string Revision => VaultRevision.Compute(RawContent);
}
