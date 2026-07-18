using System.ComponentModel;
using System.Text;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// Filesystem-safe MCP adapter for research workflows. File sources are resolved and read through
/// <see cref="VaultPathPolicy"/> before the existing research engine receives in-memory content.
/// This prevents the engine's legacy path compatibility behavior from being reachable over MCP.
/// </summary>
[McpServerToolType]
public sealed class SecureResearchTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    VaultConfigService vaultConfig,
    VaultPathPolicy paths)
{
    private readonly ResearchTools _inner = new(vault, config, vaultConfig);

    [McpServerTool, Description(
        "Imports a vault-local BibTeX (.bib) file, an explicitly allowlisted external .bib file, " +
        "or raw BibTeX content as literature notes. External file reads are denied by default. " +
        "Use dry_run=true to preview before writing.")]
    public async Task<string> import_bibtex(
        [Description("Vault-relative .bib path, allowlisted absolute .bib path, or raw BibTeX content. Relative paths never use the server CWD.")] string source,
        [Description("Folder to create literature notes in. Default: the configured 'literature' folder, or 'Literature'.")] string folder = "",
        [Description("If a note with the same citekey already exists, refresh its frontmatter fields while preserving its body.")] bool update_existing = false,
        [Description("Preview what would be created, updated, or skipped without writing files.")] bool dry_run = false)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return KiokuError.InvalidArgument("The BibTeX source cannot be empty.");
        }

        if (LooksLikeInlineBibtex(source))
        {
            return await _inner.import_bibtex(source, folder, update_existing, dry_run);
        }

        if (!LooksLikeFileSource(source))
        {
            return await _inner.import_bibtex(source, folder, update_existing, dry_run);
        }

        string sourcePath;
        try
        {
            sourcePath = paths.ResolveExternalReadPath(source);
        }
        catch (VaultAccessDeniedException)
        {
            return KiokuError.AccessDenied(
                "BibTeX file access is limited to the vault and explicitly allowlisted external roots.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return KiokuError.AccessDenied("The BibTeX source could not be resolved within the configured security boundary.");
        }

        if (!Path.GetExtension(sourcePath).Equals(".bib", StringComparison.OrdinalIgnoreCase))
        {
            return KiokuError.InvalidArgument("File-based BibTeX imports require a .bib file.");
        }

        if (!File.Exists(sourcePath))
        {
            return KiokuError.NotFound("The requested BibTeX source file was not found.");
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(sourcePath, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return KiokuError.AccessDenied("The requested BibTeX source file could not be read.");
        }

        return await _inner.import_bibtex(content, folder, update_existing, dry_run);
    }

    [McpServerTool, Description(
        "Exports citation keys found in note frontmatter as a full-fidelity BibTeX document or Markdown table. " +
        "Accepted formats are exactly 'bibtex' and 'markdown'.")]
    public string export_citations(
        [Description("Export format: 'bibtex' or 'markdown'.")] string format = "markdown",
        [Description("Folder to scan (vault-relative). Leave empty to scan the entire vault.")] string folder = "") =>
        _inner.export_citations(format, folder);

    [McpServerTool, Description(
        "Audits citations in one combined report: citation graph and orphan sources, inline citation gaps, " +
        "and required metadata on research/literature notes.")]
    public string audit_citations(
        [Description("Folder to scope source notes, inline-gap notes, and metadata validation (vault-relative). Leave empty for the entire vault.")] string folder = "") =>
        _inner.audit_citations(folder);

    private static bool LooksLikeInlineBibtex(string source) =>
        source.TrimStart().StartsWith('@');

    private static bool LooksLikeFileSource(string source) =>
        Path.IsPathRooted(source) ||
        source.EndsWith(".bib", StringComparison.OrdinalIgnoreCase) ||
        source.Contains('/') ||
        source.Contains('\\');
}
