namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Infrastructure port used by project-document workflows for durable file operations.
/// Application services depend on this contract instead of calling <see cref="File"/> or
/// <see cref="Directory"/> directly.
/// </summary>
internal interface IProjectDocumentFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    void DeleteFile(string path);

    DateTime GetFileLastWriteTimeUtc(string path);

    DateTime GetDirectoryLastWriteTimeUtc(string path);

    IReadOnlyList<string> EnumerateMarkdownFilesRecursive(string directory);

    Task<string> ReadAllTextAsync(
        string path,
        CancellationToken cancellationToken);

    Task WriteAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken);

    Task AppendAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken);
}
