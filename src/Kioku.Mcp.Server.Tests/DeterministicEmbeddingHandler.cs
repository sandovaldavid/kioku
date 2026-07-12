using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Deterministic stand-in for Ollama's /api/embeddings endpoint: embeds text as an L2-normalized
/// bag-of-words vector where each token is FNV-1a-hashed onto a few dimensions. Cosine similarity
/// between two such vectors correlates with lexical overlap, so ranking mechanics (RRF fusion,
/// top-k, thresholds) can be exercised meaningfully without a live Ollama.
/// Do not assert exact orderings against it — only floors and invariants — to avoid
/// overfitting tests to the fake.
/// </summary>
internal static class DeterministicEmbedding
{
    public const int Dimension = 256;

    private static readonly char[] Separators =
    [
        ' ', '\t', '\n', '\r', '-', '_', '.', ',', '!', '?', ':', ';', '(', ')', '[', ']', '{', '}',
        '"', '\'', '/', '\\', '*', '#', '>', '<', '=', '`',
    ];

    public static float[] Embed(string text)
    {
        var vector = new float[Dimension];
        foreach (var raw in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 2)
            {
                continue;
            }

            var hash = Fnv1a(raw.ToLowerInvariant());
            vector[(int)(hash % Dimension)] += 1f;
            vector[(int)((hash >> 8) % Dimension)] += 0.5f;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0f)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }

    /// <summary>
    /// Builds a responder for <see cref="FakeHttpMessageHandler"/> that answers Ollama's
    /// ping (GET) and /api/embeddings (POST) with deterministic vectors.
    /// Every embedded prompt is also reported through <paramref name="onPrompt"/>.
    /// </summary>
    public static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Responder(
        Action<string>? onPrompt = null)
    {
        return async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var prompt = doc.RootElement.GetProperty("prompt").GetString() ?? string.Empty;
            onPrompt?.Invoke(prompt);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { embedding = Embed(prompt) }),
            };
        };
    }

    private static uint Fnv1a(string token)
    {
        var hash = 2166136261u;
        foreach (var c in token)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash;
    }
}
