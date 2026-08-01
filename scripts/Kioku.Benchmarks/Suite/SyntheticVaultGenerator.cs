using System.Globalization;
using System.Text;

namespace Kioku.Benchmarks.Suite;

/// <summary>
/// Generates a synthetic Obsidian vault of N markdown notes with realistic-ish frontmatter and
/// body content: varied length, 2-4 tags per note drawn from a fixed topic pool, and a handful
/// of wikilinks between notes. Not degenerately uniform — paragraph count, sentence length and
/// tag selection all vary per note via a seeded Random for reproducibility.
///
/// Also returns the topic/word pool used, so callers can synthesize realistic search queries
/// that are guaranteed to match a meaningful subset of the generated vault.
/// </summary>
public static class SyntheticVaultGenerator
{
    private static readonly string[] Topics =
    [
        "kubernetes deployment", "quarterly budget", "hiking trail notes", "sourdough recipe",
        "database migration", "team retrospective", "book summary", "garden planning",
        "investment strategy", "travel itinerary", "workout routine", "customer interview",
        "product roadmap", "language learning", "home renovation", "photography technique",
        "meditation practice", "car maintenance", "wine tasting", "chess opening",
    ];

    private static readonly string[] WordBank =
    [
        "system", "process", "review", "design", "outcome", "metric", "schedule", "resource",
        "strategy", "insight", "pattern", "signal", "baseline", "workflow", "context", "decision",
        "tradeoff", "constraint", "milestone", "checklist", "summary", "draft", "revision",
        "experiment", "hypothesis", "observation", "follow-up", "action", "owner", "deadline",
    ];

    private static readonly string[] Statuses = ["draft", "active", "done", "archived"];

    public sealed record VaultInfo(
        string VaultPath,
        int NoteCount,
        IReadOnlyList<string> Topics,
        IReadOnlyList<string> Tags);

    /// <summary>Writes <paramref name="count"/> synthetic notes under <paramref name="vaultPath"/>.</summary>
    public static VaultInfo Generate(string vaultPath, int count, int seed = 42)
    {
        Directory.CreateDirectory(vaultPath);
        var random = new Random(seed);
        var tagPool = WordBank.Select(w => w.Replace(' ', '-')).ToList();
        var noteTopics = new List<string>(count);
        var noteNames = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var topic = Topics[random.Next(Topics.Length)];
            noteTopics.Add(topic);
            noteNames.Add($"{topic.Replace(' ', '-')}-{i:D5}");
        }

        for (var i = 0; i < count; i++)
        {
            var topic = noteTopics[i];
            var tagCount = random.Next(2, 5);
            var tags = Enumerable.Range(0, tagCount)
                .Select(_ => tagPool[random.Next(tagPool.Count)])
                .Distinct()
                .ToList();

            var paragraphCount = random.Next(2, 7);
            var body = BuildBody(random, topic, paragraphCount, noteNames, i);
            var status = Statuses[random.Next(Statuses.Length)];
            var day = random.Next(1, 28);

            var frontmatter = new StringBuilder()
                .Append("---\n")
                .Append(CultureInfo.InvariantCulture, $"title: \"{topic} {i}\"\n")
                .Append("tags: [").Append(string.Join(", ", tags)).Append("]\n")
                .Append(CultureInfo.InvariantCulture, $"status: {status}\n")
                .Append(CultureInfo.InvariantCulture, $"date: 2026-01-{day:D2}\n")
                .Append("---\n\n");

            var content = frontmatter
                .Append(CultureInfo.InvariantCulture, $"# {topic} {i}\n\n")
                .Append(body)
                .ToString();

            var path = Path.Combine(vaultPath, $"{noteNames[i]}.md");
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        return new VaultInfo(vaultPath, count, Topics, tagPool);
    }

    private static string BuildBody(Random random, string topic, int paragraphCount, List<string> noteNames, int index)
    {
        var sb = new StringBuilder();
        for (var p = 0; p < paragraphCount; p++)
        {
            var sentenceCount = random.Next(3, 8);
            sb.Append("## Section ").Append(p + 1).Append('\n');
            sb.Append('\n');
            for (var s = 0; s < sentenceCount; s++)
            {
                var wordCount = random.Next(6, 16);
                var words = Enumerable.Range(0, wordCount).Select(_ => WordBank[random.Next(WordBank.Length)]);
                sb.Append("This note about ").Append(topic).Append(" covers ")
                    .Append(string.Join(' ', words)).Append(".\n");
            }

            sb.Append('\n');
        }

        // A handful of notes link to a neighbor, so the corpus is not a set of islands.
        if (noteNames.Count > 1 && random.NextDouble() < 0.3)
        {
            var other = noteNames[random.Next(noteNames.Count)];
            if (!other.Equals(noteNames[index], StringComparison.Ordinal))
            {
                sb.Append("See also [[").Append(other).Append("]].\n");
            }
        }

        return sb.ToString();
    }
}
