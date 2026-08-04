using Kioku.Mcp.Server.Services;

namespace Kioku.Mcp.Server.Infrastructure;

internal sealed class CoordinationFileSystem : ICoordinationFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteFile(string path) => File.Delete(path);

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Move(sourcePath, destinationPath, overwrite);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) =>
        NoteHelpers.ReadAllTextAsync(path, cancellationToken);

    public IReadOnlyList<string> EnumerateJsonFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        return Directory.EnumerateFiles(directory, "*.json", options).ToArray();
    }

    public Task WriteNewAtomicallyAsync(string path, string content, CancellationToken cancellationToken) =>
        WriteAtomicallyCoreAsync(path, content, overwrite: false, cancellationToken);

    public Task WriteAtomicallyAsync(string path, string content, CancellationToken cancellationToken) =>
        WriteAtomicallyCoreAsync(path, content, overwrite: true, cancellationToken);

    public async Task<FileStream> AcquireExclusiveLockAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("A lock path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }
        }
    }

    private static async Task WriteAtomicallyCoreAsync(
        string path,
        string content,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("A coordination path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await WriteFlushedAsync(temporary, content, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(temporary, path, overwrite);
                    break;
                }
                catch (Exception exception) when (
                    exception is (IOException or UnauthorizedAccessException) && attempt < 7)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
                }
            }
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static async Task WriteFlushedAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        var bytes = NoteHelpers.Utf8NoBom.GetBytes(content);
        await stream.WriteAsync(bytes.AsMemory(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
