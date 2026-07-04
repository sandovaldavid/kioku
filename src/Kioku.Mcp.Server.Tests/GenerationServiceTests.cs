using System.Net;
using System.Net.Http.Json;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class GenerationServiceTests
{
    private static GenerationService CreateService(
        HttpMessageHandler handler,
        string? generationModel = "llama3.2",
        TimeSpan? requestTimeout = null)
    {
        var config = new KiokuConfiguration
        {
            VaultPath = "/tmp",
            GenerationModel = generationModel,
        };
        return new GenerationService(
            config,
            NullLogger<GenerationService>.Instance,
            new FakeHttpClientFactory(handler),
            requestTimeout);
    }

    private static HttpMessageHandler RespondOk(Func<HttpRequestMessage, HttpResponseMessage> forRequest) =>
        new FakeHttpMessageHandler((request, _) => Task.FromResult(forRequest(request)));

    [Fact]
    public async Task InitializeAsync_NoModelConfigured_IsAvailableFalse()
    {
        var handler = RespondOk(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(handler, generationModel: null);

        await service.InitializeAsync();

        Assert.False(service.IsAvailable);
    }

    [Fact]
    public async Task InitializeAsync_OllamaUnreachable_IsAvailableFalse()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            throw new HttpRequestException("Connection refused"));
        var service = CreateService(handler);

        await service.InitializeAsync();

        Assert.False(service.IsAvailable);
    }

    [Fact]
    public async Task InitializeAsync_OllamaReachable_IsAvailableTrue()
    {
        var handler = RespondOk(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(handler);

        await service.InitializeAsync();

        Assert.True(service.IsAvailable);
    }

    [Fact]
    public async Task GenerateAsync_WhenNotInitialized_ReturnsNull()
    {
        var handler = RespondOk(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(handler);
        // Deliberately not calling InitializeAsync — IsAvailable stays false.

        var result = await service.GenerateAsync("hello");

        Assert.Null(result);
    }

    [Fact]
    public async Task GenerateAsync_Success_ReturnsResponseText()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = "- point one\n- point two" }),
            });
        });
        var service = CreateService(handler);
        await service.InitializeAsync();

        var result = await service.GenerateAsync("Summarize this note.");

        Assert.Equal("- point one\n- point two", result);
    }

    [Fact]
    public async Task GenerateAsync_OllamaGoesDownMidRequest_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            throw new HttpRequestException("Connection refused");
        });
        var service = CreateService(handler);
        await service.InitializeAsync();

        var result = await service.GenerateAsync("hello");

        Assert.Null(result);
    }

    [Fact]
    public async Task GenerateAsync_ModelNotPulled_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new { error = "model 'llama3.2' not found, try pulling it first" }),
            });
        });
        var service = CreateService(handler);
        await service.InitializeAsync();

        var result = await service.GenerateAsync("hello");

        Assert.Null(result);
    }

    [Fact]
    public async Task GenerateAsync_TimesOut_ReturnsNullWithoutWaitingTheFullTimeout()
    {
        var handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            // Simulate a slow model — far longer than the test's configured timeout.
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = "too late" }),
            };
        });
        var service = CreateService(handler, requestTimeout: TimeSpan.FromMilliseconds(50));
        await service.InitializeAsync();

        var result = await service.GenerateAsync("hello");

        Assert.Null(result);
    }

    [Fact]
    public async Task GenerateAsync_LongPrompt_TruncatesTo4000Characters()
    {
        string? capturedPrompt = null;
        var handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            capturedPrompt = doc.RootElement.GetProperty("prompt").GetString();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = "summary" }),
            };
        });
        var service = CreateService(handler);
        await service.InitializeAsync();

        var longPrompt = new string('a', 10_000);
        await service.GenerateAsync(longPrompt);

        Assert.NotNull(capturedPrompt);
        Assert.Equal(4000, capturedPrompt!.Length);
    }
}
