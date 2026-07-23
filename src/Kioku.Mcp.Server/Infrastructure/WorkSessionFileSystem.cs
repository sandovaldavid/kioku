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
            FileStream stream;
            try
            {
                stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous);
            }
            catch (IOException) when (File.Exists(path))
            {
                // A concurrent start claimed this filename; retry with the UUID-derived fallback.
                continue;
            }

            try
            {
                await using (stream)
                await using (var writer = new StreamWriter(stream, NoteHelpers.Utf8NoBom))
                {
                    await writer.WriteAsync(content.AsMemory(), cancellationToken);
                }

                return path;
            }
            catch
            {
                TryDelete(path);
                throw;
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
            TryDelete(temporary);
        }
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
            // Preserve the original write/cancellation failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve the original write/cancellation failure.
        }
    }
}
