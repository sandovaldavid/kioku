using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class EmbeddingConcurrencyTests
{
    [Fact]
    public async Task Background_backlog_respects_configured_concurrency()
    {
        var vaultPath = Path.Combine(Path.GetTempPath(), $"kioku-embedding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vaultPath);
        try
        {
            var active = 0;
            var maximum = 0;
            var handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
            {
                if (request.Method == HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }

                var current = Interlocked.Increment(ref active);
                UpdateMaximum(ref maximum, current);
                try
                {
                    await Task.Delay(40, cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f } }),
                    };
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });
            var configuration = new KiokuConfiguration
            {
                VaultPath = vaultPath,
                EmbeddingModel = "nomic-embed-text",
            };
            var service = new EmbeddingService(
                configuration,
                NullLogger<EmbeddingService>.Instance,
                new FakeHttpClientFactory(handler),
                Options.Create(new KiokuOptions
                {
                    VaultPath = vaultPath,
                    EmbeddingConcurrency = 3,
                }));
            using (service)
            {
                var notes = Enumerable.Range(0, 20)
                    .Select(index => CreateNote(vaultPath, index))
                    .ToArray();

                await service.InitializeAsync(notes);
                await WaitForBacklogAsync(service, TimeSpan.FromSeconds(10));

                Assert.Equal(3, service.MaximumConcurrency);
                Assert.InRange(maximum, 1, 3);
                Assert.Equal(20, service.CachedEmbeddingCount);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(vaultPath, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup on Windows.
            }
        }
    }

    private static Note CreateNote(string vaultPath, int index) => new()
    {
        FilePath = Path.Combine(vaultPath, $"note-{index}.md"),
        VaultRelativePath = $"note-{index}.md",
        Name = $"note-{index}",
        RawContent = $"content {index}",
        PlainText = $"content {index}",
        ContentHash = $"hash-{index}",
        LastModified = DateTimeOffset.UtcNow,
    };

    private static async Task WaitForBacklogAsync(EmbeddingService service, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (service.EmbeddingBacklog > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(0, service.EmbeddingBacklog);
    }

    private static void UpdateMaximum(ref int maximum, int current)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (current <= observed ||
                Interlocked.CompareExchange(ref maximum, current, observed) == observed)
            {
                return;
            }
        }
    }
}
