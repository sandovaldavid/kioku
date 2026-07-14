using Kioku.Mcp.Server.Domain;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class GoldenSetTests
{
    [Fact]
    public void Load_ValidJson_ParsesQueriesAndJudgments()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kioku-golden-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "queries": [
                { "id": "q01", "query": "notas sobre burnout laboral",
                  "relevant": [ { "path": "Salud/Burnout.md", "grade": 3 },
                                { "path": "Salud/Estres.md" } ] },
                { "id": "q02", "query": "nothing matches this", "relevant": [] }
              ]
            }
            """);

        try
        {
            var set = GoldenSet.Load(path);

            Assert.Equal(2, set.Queries.Count);

            var q1 = set.Queries[0];
            Assert.Equal("q01", q1.Id);
            Assert.True(q1.HasRelevantNotes);
            var judgments = q1.RelevanceByPath();
            Assert.Equal(3, judgments["Salud/Burnout.md"]);
            Assert.Equal(1, judgments["Salud/Estres.md"]); // grade defaults to 1

            var q2 = set.Queries[1];
            Assert.False(q2.HasRelevantNotes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RelevanceByPath_NormalizesSeparatorsAndIsCaseInsensitive()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kioku-golden-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            { "queries": [ { "id": "q", "query": "x",
                "relevant": [ { "path": "Folder\\Note.md", "grade": 2 } ] } ] }
            """);

        try
        {
            var judgments = GoldenSet.Load(path).Queries[0].RelevanceByPath();
            Assert.Equal(2, judgments["folder/note.md"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FixtureGoldenSet_LoadsAndReferencesExistingEvalVaultNotes()
    {
        var fixtureDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var set = GoldenSet.Load(Path.Combine(fixtureDir, "golden-set.json"));

        Assert.True(set.Queries.Count >= 20, $"Expected at least 20 golden queries, found {set.Queries.Count}");
        Assert.Contains(set.Queries, q => !q.HasRelevantNotes); // precision probes present

        var vaultDir = Path.Combine(fixtureDir, "EvalVault");
        foreach (var query in set.Queries)
        {
            foreach (var relevant in query.Relevant)
            {
                var notePath = Path.Combine(vaultDir, relevant.Path.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(notePath), $"Golden set query '{query.Id}' references missing note: {relevant.Path}");
                Assert.InRange(relevant.Grade, 1, 3);
            }
        }
    }
}
