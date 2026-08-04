/// <summary>
/// Minimal IHttpClientFactory for standalone use of EmbeddingService outside DI.
/// </summary>
internal sealed class SimpleHttpClientFactory : IHttpClientFactory
{
    private static readonly SocketsHttpHandler Handler = new();

    public HttpClient CreateClient(string name) =>
        new(Handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
}
