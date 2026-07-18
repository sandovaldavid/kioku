namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Typed projection of the frontmatter fields Kioku understands directly.
/// Unknown fields remain available through <see cref="ExtraFields"/> so callers can
/// mutate known properties without discarding data owned by users or other plugins.
/// </summary>
public sealed class NoteFrontmatter
{
    public IReadOnlyList<string> Tags { get; init; } = [];

    public IReadOnlyList<string> Aliases { get; init; } = [];

    public IReadOnlyList<string> CssClasses { get; init; } = [];

    public string? NoteType { get; init; }

    public string? Status { get; init; }

    public string? Domain { get; init; }

    public DateOnly? Date { get; init; }

    public DateOnly? Updated { get; init; }

    public string? ZettelId { get; init; }

    /// <summary>
    /// Frontmatter entries not represented by a typed Kioku property. Values may be
    /// scalars, lists, or nested dictionaries and are serialized back without flattening.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ExtraFields { get; init; }
        = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}
