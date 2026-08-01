namespace Kioku.Benchmarks.Suite;

/// <summary>
/// Minimal IHttpClientFactory for standalone use of EmbeddingService outside DI, mirroring
/// scripts/Kioku.Eval's EvalHttpClientFactory.
/// </summary>
public sealed class SimpleHttpClientFactory : IHttpClientFactory
{
    private static readonly SocketsHttpHandler Handler = new();

    public HttpClient CreateClient(string name) =>
        new(Handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
}
