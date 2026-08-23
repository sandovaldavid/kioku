using System.Text;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// BM25 keyword-scoring properties: IDF (rare terms weigh more than common ones),
/// document-length normalization, and incremental index consistency after edits/deletes.
/// </summary>
public class VaultIndexServiceBm25Tests : IAsyncLifetime
{
    private string _vaultPath = null!;

    public Task InitializeAsync()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), $"kioku-bm25-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_vaultPath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            Directory.Delete(_vaultPath, recursive: true);
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    private async Task WriteNoteAsync(string name, string body)
    {
        var path = Path.Combine(_vaultPath, name + ".md");
        await File.WriteAllTextAsync(path, body, Encoding.UTF8);
    }

    private VaultIndexService CreateIndex() => new(
        NullLogger<VaultIndexService>.Instance,
        new KiokuConfiguration { VaultPath = _vaultPath });

    [Fact]
    public async Task Search_RareTermOutranksCommonTerm()
    {
        // "meteorito" appears in exactly one note; "planta" appears in all of them.
        await WriteNoteAsync("Objetivo", "El meteorito cayo cerca de la planta procesadora.");
        for (int i = 0; i < 4; i++)
        {
            await WriteNoteAsync($"Relleno{i}", "La planta crece y la planta florece junto a otra planta.");
        }

        using var index = CreateIndex();
        await index.RebuildIndexAsync();

        var results = index.Search("meteorito planta", 10).ToList();

        Assert.True(results.Count >= 5);
        Assert.Equal("Objetivo", results[0].Note.Name);
    }

    [Fact]
    public async Task Search_ShortFocusedNoteOutranksLongRamblingNote()
    {
        // Same term frequency for "sismo" (twice each), very different document lengths.
        await WriteNoteAsync("Corta", "El sismo fue fuerte. El sismo duro un minuto.");
        var filler = string.Join(" ", Enumerable.Repeat(
            "parrafo de relleno con muchas palabras irrelevantes que alargan el documento", 60));
        await WriteNoteAsync("Larga", $"El sismo fue fuerte. {filler} El sismo duro un minuto.");

        using var index = CreateIndex();
        await index.RebuildIndexAsync();

        var results = index.Search("sismo", 10).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("Corta", results[0].Note.Name);
        Assert.Equal("Larga", results[1].Note.Name);
    }

    [Fact]
    public async Task Search_EditedNote_ScoresMatchFreshRebuild()
    {
        await WriteNoteAsync("Editada", "tema original con contenido sobre volcanes y lava");
        await WriteNoteAsync("Estable", "otro tema con contenido sobre glaciares");

        using var index = CreateIndex();
        await index.RebuildIndexAsync();

        // The edit removes "volcanes" entirely and introduces "auroras".
        await WriteNoteAsync("Editada", "tema nuevo con contenido sobre auroras boreales");
        await index.SynchronizeFileReindexAsync(Path.Combine(_vaultPath, "Editada.md"));

        // The stale word must no longer match the edited note.
        Assert.DoesNotContain(index.Search("volcanes", 10), r => r.Note.Name == "Editada");

        using var fresh = CreateIndex();
        await fresh.RebuildIndexAsync();

        foreach (var query in new[] { "auroras", "contenido tema", "glaciares" })
        {
            var incremental = index.Search(query, 10).Select(r => (r.Note.Name, r.Score)).ToList();
            var rebuilt = fresh.Search(query, 10).Select(r => (r.Note.Name, r.Score)).ToList();

            Assert.Equal(rebuilt.Select(r => r.Name), incremental.Select(r => r.Name));
            for (int i = 0; i < rebuilt.Count; i++)
            {
                Assert.Equal(rebuilt[i].Score, incremental[i].Score, precision: 5);
            }
        }
    }

    [Fact]
    public async Task Search_DeletedNote_ScoresMatchFreshRebuild()
    {
        await WriteNoteAsync("Borrada", "termino compartido entre notas");
        await WriteNoteAsync("Viva", "termino compartido y algo mas");

        using var index = CreateIndex();
        await index.RebuildIndexAsync();

        var deletedPath = Path.Combine(_vaultPath, "Borrada.md");
        File.Delete(deletedPath);
        index.SynchronizeFileDelete(deletedPath);

        using var fresh = CreateIndex();
        await fresh.RebuildIndexAsync();

        var incremental = index.Search("termino compartido", 10).Select(r => (r.Note.Name, r.Score)).ToList();
        var rebuilt = fresh.Search("termino compartido", 10).Select(r => (r.Note.Name, r.Score)).ToList();

        Assert.Equal(rebuilt.Select(r => r.Name), incremental.Select(r => r.Name));
        Assert.Single(incremental);
        Assert.Equal("Viva", incremental[0].Name);
    }

    [Fact]
    public async Task Search_ManySequentialEditsAndDeletes_IncrementalAverageMatchesFreshRebuild()
    {
        // GitHub #444: avgDocLength moved from a full _docLengths.Values.Average() scan on every
        // query to a running total maintained incrementally at the same add/remove sites as
        // _docLengths itself. Repeated add/edit/delete cycles are the case most likely to reveal
        // drift between the running total and a true recount.
        for (int i = 0; i < 6; i++)
        {
            await WriteNoteAsync($"Nota{i}", $"contenido inicial numero {i} con palabras variadas");
        }

        using var index = CreateIndex();
        await index.RebuildIndexAsync();

        for (int i = 0; i < 6; i += 2)
        {
            await WriteNoteAsync($"Nota{i}", $"contenido editado numero {i} con mas palabras nuevas y distintas");
            await index.SynchronizeFileReindexAsync(Path.Combine(_vaultPath, $"Nota{i}.md"));
        }

        File.Delete(Path.Combine(_vaultPath, "Nota1.md"));
        index.SynchronizeFileDelete(Path.Combine(_vaultPath, "Nota1.md"));

        await WriteNoteAsync("NotaExtra", "nota agregada despues de las ediciones y el borrado");
        await index.SynchronizeFileReindexAsync(Path.Combine(_vaultPath, "NotaExtra.md"));

        using var fresh = CreateIndex();
        await fresh.RebuildIndexAsync();

        foreach (var query in new[] { "contenido", "palabras", "agregada" })
        {
            // Compared by name rather than by result order: several notes here legitimately tie
            // on score (same term frequency, near-identical document length), and tie order isn't
            // guaranteed to match between two independently built indexes — that's an unrelated,
            // pre-existing property of ordering ties, not what this test is checking. What matters
            // for #444 is that the incremental average produces the same score per note as a
            // from-scratch recount.
            var incremental = index.Search(query, 20).ToDictionary(r => r.Note.Name, r => r.Score);
            var rebuilt = fresh.Search(query, 20).ToDictionary(r => r.Note.Name, r => r.Score);

            Assert.Equal(rebuilt.Keys.OrderBy(k => k), incremental.Keys.OrderBy(k => k));
            foreach (var name in rebuilt.Keys)
            {
                Assert.Equal(rebuilt[name], incremental[name], precision: 5);
            }
        }
    }

    [Fact]
    public async Task Search_TitleTagAndContentMatches_AllSurfaceWithTitleFirst()
    {
        await WriteNoteAsync("Contenido", "apunte que menciona jardineria en el cuerpo del texto");
        await WriteNoteAsync("Jardineria", "nota sobre plantas y herramientas");
        await File.WriteAllTextAsync(Path.Combine(_vaultPath, "Etiquetada.md"),
            "---\ntags: [jardineria]\n---\nnota etiquetada sin la palabra en el cuerpo", Encoding.UTF8);

        using var index = CreateIndex();
        await index.RebuildIndexAsync();

        var results = index.Search("jardineria", 10).ToList();

        // Boost policy: title match ranks first (BM25 + title boost), an exact content match
        // beats a tag-only match, and the tag-only note still surfaces.
        Assert.Equal(3, results.Count);
        Assert.Equal("Jardineria", results[0].Note.Name);
        Assert.Equal("Contenido", results[1].Note.Name);
        Assert.Equal("Etiquetada", results[2].Note.Name);
    }
}
