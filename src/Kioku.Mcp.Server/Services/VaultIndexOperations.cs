using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Narrow application boundary used by the indexing pipeline. Keeping the pipeline behind this
/// interface makes concurrency, cancellation, debounce, and reconciliation behavior testable
/// without an MCP host or direct access to the index implementation.
/// </summary>
internal interface IVaultIndexOperations
{
    IReadOnlyCollection<Note> GetNotesSnapshot();

    Task ReindexAsync(string filePath, CancellationToken cancellationToken);

    Task MoveAsync(string oldPath, string newPath, CancellationToken cancellationToken);

    void Delete(string filePath);
}

internal sealed class VaultIndexOperations(VaultIndexService vault) : IVaultIndexOperations
{
    public IReadOnlyCollection<Note> GetNotesSnapshot() => vault.GetAllNotes().ToArray();

    public async Task ReindexAsync(string filePath, CancellationToken cancellationToken)
    {
        await vault.SynchronizeFileReindexAsync(filePath).WaitAsync(cancellationToken);

        if (File.Exists(filePath) && vault.GetNote(filePath) is null)
        {
            throw new IOException($"The note could not be indexed after reading '{filePath}'.");
        }
    }

    public async Task MoveAsync(
        string oldPath,
        string newPath,
        CancellationToken cancellationToken)
    {
        await vault.SynchronizeFileMoveAsync(oldPath, newPath).WaitAsync(cancellationToken);

        if (File.Exists(newPath) && vault.GetNote(newPath) is null)
        {
            throw new IOException($"The renamed note could not be indexed at '{newPath}'.");
        }
    }

    public void Delete(string filePath) => vault.SynchronizeFileDelete(filePath);
}