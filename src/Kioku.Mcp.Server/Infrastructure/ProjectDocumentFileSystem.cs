using System.Text;
using Kioku.Mcp.Server.Services;

namespace Kioku.Mcp.Server.Infrastructure;

internal sealed class ProjectDocumentFileSystem : IProjectDocumentFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);

    public DateTime GetFileLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    public DateTime GetDirectoryLastWriteTimeUtc(string path) => Directory.GetLastWriteTimeUtc(path);

    public IReadOnlyList<string> EnumerateMarkdownFilesRecursive(string directory) =>
        [.. Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories)];

    public Task<string> ReadAllTextAsync(
        string path,
        CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);

    public Task WriteAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, content, NoteHelpers.Utf8NoBom, cancellationToken);

    public Task AppendAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken) =>
        File.AppendAllTextAsync(path, content, NoteHelpers.Utf8NoBom, cancellationToken);
}
