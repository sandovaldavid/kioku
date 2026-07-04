using System.Net.Http;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Minimal HttpMessageHandler stand-in for testing HttpClient-based services (GenerationService,
/// EmbeddingService-style code) without a mocking framework — matches the repo's convention of
/// favoring real/fake collaborators over mocks.
/// </summary>
internal sealed class FakeHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        responder(request, cancellationToken);
}

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
