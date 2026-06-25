namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Result of a search operation on the vault.
/// </summary>
public sealed class SearchResult
{
    public required Note Note { get; init; }

    /// <summary>Relevance score [0.0 – 1.0]. Higher = more relevant.</summary>
    public required float Score { get; init; }

    /// <summary>Type of match that generated this result.</summary>
    public required NoteMatchType MatchType { get; init; }

    /// <summary>Snippet of the content that matched the search (to display to the user).</summary>
    public string? Snippet { get; init; }
}

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
