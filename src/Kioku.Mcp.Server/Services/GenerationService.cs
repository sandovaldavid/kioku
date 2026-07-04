using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Kioku.Mcp.Server.Logging;
using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Generates text locally via Ollama (summaries, explanations, Q/A).
/// Mirrors EmbeddingService's pattern: degrades gracefully when Ollama or the configured
/// model is unavailable. Never sends vault content anywhere but KIOKU_OLLAMA_URL.
/// </summary>
public sealed class GenerationService
{
    private const int MaxPromptChars = 4000;
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(120);

    private readonly KiokuConfiguration _config;
    private readonly ILogger<GenerationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeSpan _requestTimeout;

    public GenerationService(
        KiokuConfiguration config,
        ILogger<GenerationService> logger,
        IHttpClientFactory httpClientFactory,
        TimeSpan? requestTimeout = null)
    {
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
    }

    public bool IsAvailable { get; private set; }

    /// <summary>Configured generation model name (may be null/empty when disabled).</summary>
    public string? GenerationModel => _config.GenerationModel;

    // Initialization

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_config.GenerationModel))
        {
            _logger.Info("Local generation disabled (KIOKU_GEN_MODEL not configured).");
            return;
        }

        IsAvailable = await PingOllamaAsync(cancellationToken);
        if (!IsAvailable)
        {
            _logger.Warn("Ollama not reachable at {Url} — local generation disabled.", _config.OllamaUrl);
            return;
        }

        _logger.Info("Local generation ready. Model: {Model}", _config.GenerationModel);
    }

    // Public API

    /// <summary>
    /// Generates text from a prompt via Ollama. Returns null when generation is unavailable,
    /// the request times out, or Ollama reports an error (e.g. the model isn't pulled).
    /// The prompt is truncated to ~4000 characters — long inputs are impractically slow on CPU.
    /// </summary>
    public async Task<string?> GenerateAsync(string prompt, string? system = null, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return null;
        }

        var truncated = prompt.Length > MaxPromptChars ? prompt[..MaxPromptChars] : prompt;

        using var timeoutCts = new CancellationTokenSource(_requestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using var http = _httpClientFactory.CreateClient("ollama");
            var response = await http.PostAsJsonAsync(
                $"{_config.OllamaUrl}/api/generate",
                new OllamaGenerateRequest
                {
                    Model = _config.GenerationModel!,
                    Prompt = truncated,
                    System = system,
                    Stream = false,
                },
                linkedCts.Token);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync(
                GenerationJsonContext.Default.OllamaGenerateResponse, linkedCts.Token);
            return result?.Response;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.Warn("Generation request timed out after {Timeout}.", _requestTimeout);
            return null;
        }
        catch (Exception ex)
        {
            _logger.Warn("Generation request failed: {Message}", ex.Message);
            return null;
        }
    }

    // Private helpers

    private async Task<bool> PingOllamaAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient("ollama");
            var response = await http.GetAsync($"{_config.OllamaUrl}/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

// Ollama HTTP types (AOT-safe)

internal sealed class OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("system")]
    public string? System { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }
}

internal sealed class OllamaGenerateResponse
{
    [JsonPropertyName("response")]
    public string? Response { get; init; }
}

[JsonSerializable(typeof(OllamaGenerateRequest))]
[JsonSerializable(typeof(OllamaGenerateResponse))]
internal partial class GenerationJsonContext : JsonSerializerContext
{
}
