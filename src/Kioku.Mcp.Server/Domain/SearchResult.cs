namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Result of a search operation on the vault.
/// </summary>
public sealed record SearchResult(Note Note, float Score, NoteMatchType MatchType, string? Snippet);

/// <summary>Type of search that produced the result.</summary>
public enum NoteMatchType
{
    /// <summary>Match by title or filename.</summary>
    TitleMatch,

    /// <summary>Match by tag.</summary>
    TagMatch,

    /// <summary>Match by note body content.</summary>
    ContentMatch,

    /// <summary>Match by alias declared in frontmatter.</summary>
    AliasMatch,

    /// <summary>Match by custom frontmatter field.</summary>
    MetadataMatch,
}
