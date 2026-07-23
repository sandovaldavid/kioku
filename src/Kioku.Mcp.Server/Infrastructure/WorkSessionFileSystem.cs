using System.Text;
using Kioku.Mcp.Server.Services;

namespace Kioku.Mcp.Server.Infrastructure;

internal sealed class WorkSessionFileSystem : IWorkSessionFileSystem
{
    public bool DirectoryExists(string directory) => Directory.Exists(directory);

    public async Task<string?> ReadIfExistsAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public Task<string> ReadAllTextAsync(
        string filePath,
        CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);

    public async Task<string> WriteNewSessionFileAsync(
        string directory,
        string preferredName,
        string fallbackName,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { preferredName, fallbackName })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, name);
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous);
                await using var writer = new StreamWriter(stream, NoteHelpers.Utf8NoBom);
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                // A concurrent start claimed this filename; retry with the UUID-derived fallback.
            }
        }

        throw new IOException("Could not allocate a unique work-session filename.");
    }

    public async Task WriteAtomicallyAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                content,
                NoteHelpers.Utf8NoBom,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
