using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

public static class FolderRanker
{
    public static List<(string Folder, double Score)> RankFolders(
        Note source,
        int topN,
        VaultIndexService vault,
        HybridSearchService hybrid,
        EmbeddingService embedding)
    {
        var allFolders = vault.GetAllNotes()
            .Select(n => Path.GetDirectoryName(n.VaultRelativePath) ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => !string.IsNullOrEmpty(f))
            .ToList();

        if (allFolders.Count == 0)
        {
            return [];
        }

        var sourceFolder = Path.GetDirectoryName(source.VaultRelativePath) ?? "";
        allFolders = allFolders.Where(f => !f.Equals(sourceFolder, StringComparison.OrdinalIgnoreCase)).ToList();

        if (allFolders.Count == 0)
        {
            return [];
        }

        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        if (embedding.IsAvailable)
        {
            var similar = hybrid.FindSimilar(source, topN * 3, 0.3f);
            foreach (var result in similar)
            {
                var folder = Path.GetDirectoryName(result.Note.VaultRelativePath) ?? "";
                if (string.IsNullOrEmpty(folder) || folder.Equals(sourceFolder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!scores.TryGetValue(folder, out var existing))
                {
                    scores[folder] = 0;
                }
                scores[folder] = existing + result.Score;
            }
        }

        var sourceTokens = Tokenize(source.PlainText + " " + source.Name);

        foreach (var folder in allFolders)
        {
            var notesInFolder = vault.GetNotesInFolder(folder).ToList();
            if (notesInFolder.Count == 0)
            {
                continue;
            }

            var folderText = string.Join(" ", notesInFolder.Select(n => n.PlainText + " " + n.Name));
            var folderTokens = Tokenize(folderText);
            var overlap = sourceTokens.Count(t => folderTokens.Contains(t));
            var keywordScore = notesInFolder.Count > 0 ? (double)overlap / notesInFolder.Count : 0;

            if (!scores.TryGetValue(folder, out var existing))
            {
                scores[folder] = 0;
            }
            scores[folder] = existing + keywordScore;
        }

        return [.. scores
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .Take(topN)
            .Select(kv => (kv.Key, kv.Value))];
    }

    private static HashSet<string> Tokenize(string text) =>
        [.. text.ToLowerInvariant()
            .Split([' ', '\n', '\r', '\t', '.', ',', ':', ';', '!', '?', '(', ')', '[', ']', '#'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)];
}
