namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Infrastructure port used by work-session workflows for durable file operations.
/// Application services depend on this contract instead of calling <see cref="File"/> or
/// <see cref="Directory"/> directly.
/// </summary>
internal interface IWorkSessionFileSystem
{
    bool DirectoryExists(string directory);

    Task<string?> ReadIfExistsAsync(
        string filePath,
        CancellationToken cancellationToken);

    Task<string> ReadAllTextAsync(
        string filePath,
        CancellationToken cancellationToken);

    Task<string> WriteNewSessionFileAsync(
        string directory,
        string preferredName,
        string fallbackName,
        string content,
        CancellationToken cancellationToken);

    Task WriteAtomicallyAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken);
}
