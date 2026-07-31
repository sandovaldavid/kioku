namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Infrastructure port for coordination control-plane file operations.
/// </summary>
internal interface ICoordinationFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    void DeleteFile(string path);

    void MoveFile(string sourcePath, string destinationPath, bool overwrite);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    IReadOnlyList<string> EnumerateJsonFiles(string directory);

    Task WriteNewAtomicallyAsync(string path, string content, CancellationToken cancellationToken);

    Task WriteAtomicallyAsync(string path, string content, CancellationToken cancellationToken);

    Task<FileStream> AcquireExclusiveLockAsync(string path, CancellationToken cancellationToken);
}
