using Kioku.Mcp.Server.Domain;
using YamlDotNet.Core;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Compatibility facade used by the indexer and existing tools. Parsing is delegated to
/// <see cref="FrontmatterDocument"/> so reads and writes share the same YAML semantics.
/// </summary>
public static class FrontmatterParser
{
    /// <summary>
    /// Extracts metadata from a note. Invalid or incomplete frontmatter is treated as empty for
    /// indexing purposes; mutation paths use <see cref="FrontmatterDocument.Parse"/> directly and
    /// therefore reject invalid YAML instead of rewriting it.
    /// </summary>
    public static NoteMetadata Parse(string content)
    {
        try
        {
            var document = FrontmatterDocument.Parse(content);
            return document.HasFrontmatter
                ? document.ToNoteMetadata()
                : NoteMetadata.Empty;
        }
        catch (Exception exception) when (exception is YamlException or InvalidDataException)
        {
            return NoteMetadata.Empty;
        }
    }

    /// <summary>
    /// Returns the index where the Markdown body starts, or zero when no complete frontmatter
    /// block is present.
    /// </summary>
    public static int GetBodyStart(string content) => FrontmatterDocument.GetBodyStart(content);
}
