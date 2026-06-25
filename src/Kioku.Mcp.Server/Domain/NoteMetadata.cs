namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Metadata extracted from the YAML frontmatter of an Obsidian note.
/// Supports the most common fields of the Obsidian standard.
/// </summary>
public sealed class NoteMetadata
{
    public static readonly NoteMetadata Empty = new();

    /// <summary>Alternative aliases of the note (`aliases` field).</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Tags of the note (`tags` field). Supports list and inline format.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Creation date (`date` or `created` field).</summary>
    public DateOnly? Date { get; init; }

    /// <summary>Last modification date declared in frontmatter (`updated` field).</summary>
    public DateOnly? Updated { get; init; }

    /// <summary>Status of the note (`status` field). E.g. draft, published, archived.</summary>
    public string? Status { get; init; }

    /// <summary>Type of note (`type` field). E.g. note, project, area, resource.</summary>
    public string? NoteType { get; init; }

    /// <summary>
    /// All key-value pairs of the frontmatter that were not recognized
    /// as standard fields. Preserved to avoid losing user information.
    /// </summary>
    public IReadOnlyDictionary<string, string> ExtraFields { get; init; }
        = new Dictionary<string, string>();
}
