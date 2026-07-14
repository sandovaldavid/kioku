using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Logging;
using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Services;

public sealed partial class ObsidianBridgeService : IDisposable
{
    private readonly ILogger<ObsidianBridgeService> _logger;
    private readonly int _port;
    private readonly string? _bridgeToken;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BridgeResponse>> _pendingRequests = new();
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _loopCts;
    private Task? _receiveLoopTask;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);

    public ObsidianBridgeService(ILogger<ObsidianBridgeService> logger, KiokuConfiguration config)
    {
        _logger = logger;
        _port = config.ObsidianBridgePort;
        _bridgeToken = config.BridgeToken;
    }

    public async Task<BridgeResponse> SendRequestAsync(string command, JsonNode? payload = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureConnectedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Warn("Could not establish connection to Obsidian: {Message}", ex.Message);
            var error = ex.Message.Contains("[UNAUTHORIZED]", StringComparison.Ordinal)
                ? ex.Message
                : $"Could not connect to Obsidian. Make sure Obsidian is open and the Kioku MCP plugin is activated on port {_port}. Details: {ex.Message}";
            return new BridgeResponse { Success = false, Error = error };
        }

        return await SendOverExistingConnectionAsync(command, payload, cancellationToken);
    }

    // Matches Templater's <% %> and <%* %> syntax, including multi-line blocks.
    [GeneratedRegex(@"<%[\s\S]*?%>")]
    private static partial Regex TemplaterSyntaxRegex();

    /// <summary>
    /// If <paramref name="renderedContent"/> contains Templater syntax, asks the real Templater
    /// plugin (via the Obsidian bridge) to evaluate the already-written file in place. Best-effort:
    /// never throws. When no Templater syntax is present, returns immediately without any RPC
    /// (fully headless, no behavior change for users who don't use Templater).
    /// </summary>
    public async Task<TemplaterEvaluationResult> EvaluateTemplaterInPlaceAsync(
        string renderedContent, string vaultRelativePath, CancellationToken cancellationToken = default)
    {
        if (!TemplaterSyntaxRegex().IsMatch(renderedContent))
        {
            return TemplaterEvaluationResult.NotNeeded;
        }

        var payload = new JsonObject { ["notePath"] = vaultRelativePath };
        var response = await SendRequestAsync("evaluate-templater-in-file", payload, cancellationToken);

        return response.Success
            ? new TemplaterEvaluationResult(Applied: true, Warning: null)
            : new TemplaterEvaluationResult(
                Applied: false,
                Warning: "template contains Templater syntax; left unevaluated (open Obsidian or use {{var}})");
    }

    private async Task<BridgeResponse> SendOverExistingConnectionAsync(string command, JsonNode? payload, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<BridgeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = tcs;

        var message = new BridgeMessage
        {
            Command = command,
            Payload = payload,
            RequestId = requestId,
            ProtocolVersion = BridgeProtocol.Version
        };

        try
        {
            var json = JsonSerializer.Serialize(message, BridgeJsonContext.Default.BridgeMessage);
            var bytes = Encoding.UTF8.GetBytes(json);

            await _webSocket!.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _pendingRequests.TryRemove(requestId, out _);
            _logger.Error(ex, "Error sending message over WebSocket");
            await CloseAndResetAsync();
            return new BridgeResponse { Success = false, Error = $"Communication error: {ex.Message}" };
        }

        // Wait for the response with a 10-second timeout
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            using (linkedCts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }
        catch (TaskCanceledException)
        {
            _pendingRequests.TryRemove(requestId, out _);
            if (timeoutCts.IsCancellationRequested)
            {
                return new BridgeResponse { Success = false, Error = "Timeout: The request to Obsidian timed out." };
            }
            throw;
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_webSocket is { State: WebSocketState.Open })
        {
            return;
        }

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_webSocket is { State: WebSocketState.Open })
            {
                return;
            }

            await CloseAndResetAsync();

            _webSocket = new ClientWebSocket();
            // Keep the keep-alive interval short
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);

            var uri = new Uri($"ws://127.0.0.1:{_port}/");
            _logger.Info("Connecting to Obsidian bridge at {Uri}...", uri);

            using var connectTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectTimeoutCts.Token);

            await _webSocket.ConnectAsync(uri, linkedCts.Token);
            _logger.Info("Connected to Obsidian bridge successfully.");

            _loopCts = new CancellationTokenSource();
            _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_loopCts.Token));

            await AuthenticateAsync(cancellationToken);
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    /// <summary>
    /// Sends the "auth" handshake as the first message on a freshly established connection.
    /// Required on every (re)connection — the plugin tracks authentication per WebSocket
    /// connection, not per token. A no-op on the plugin side when no token is configured there.
    /// </summary>
    private async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        var payload = new JsonObject();
        if (!string.IsNullOrEmpty(_bridgeToken))
        {
            payload["token"] = _bridgeToken;
        }

        var response = await SendOverExistingConnectionAsync("auth", payload, cancellationToken);
        if (!response.Success)
        {
            await CloseAndResetAsync();
            throw new InvalidOperationException(
                response.Error ?? "[error] [UNAUTHORIZED] Obsidian bridge authentication failed.");
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var messageBuilder = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket is { State: WebSocketState.Open })
            {
                WebSocketReceiveResult result;
                messageBuilder.Clear();

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
                        return;
                    }

                    var chunk = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    messageBuilder.Append(chunk);

                } while (!result.EndOfMessage);

                var responseJson = messageBuilder.ToString();
                try
                {
                    var response = JsonSerializer.Deserialize(responseJson, BridgeJsonContext.Default.BridgeResponse);
                    if (response?.RequestId is not null && _pendingRequests.TryRemove(response.RequestId, out var tcs))
                    {
                        tcs.TrySetResult(response);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn("Error deserializing bridge response: {Error}. Raw: {Raw}", ex.Message, responseJson);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Receive loop cancelled.");
        }
        catch (WebSocketException ex)
        {
            _logger.Debug("WebSocket error in receive loop: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Warn("Unexpected error in receive loop: {Type}: {Message}", ex.GetType().Name, ex.Message);
        }
        finally
        {
            await CloseAndResetAsync();
        }
    }

    /// <summary>
    /// Tears down the current connection. Safe to call concurrently/reentrantly — it's invoked
    /// both explicitly (e.g. after a failed auth handshake) and from ReceiveLoopAsync's own
    /// teardown, which runs as an independent background task and isn't serialized by
    /// _connectionSemaphore. Each field is atomically claimed via Interlocked.Exchange so only
    /// one concurrent caller ever disposes it — otherwise a second caller could hit an
    /// ObjectDisposedException disposing an already-disposed CancellationTokenSource/WebSocket.
    /// </summary>
    private async Task CloseAndResetAsync()
    {
        var loopCts = Interlocked.Exchange(ref _loopCts, null);
        if (loopCts is not null)
        {
            await loopCts.CancelAsync();
            loopCts.Dispose();
        }

        var webSocket = Interlocked.Exchange(ref _webSocket, null);
        if (webSocket is not null)
        {
            try
            {
                if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", closeCts.Token);
                }
            }
            catch
            {
                // Ignore closing errors
            }
            finally
            {
                webSocket.Dispose();
            }
        }

        // Cancel and clean up pending requests
        var pending = _pendingRequests.Values.ToList();
        _pendingRequests.Clear();
        foreach (var tcs in pending)
        {
            tcs.TrySetResult(new BridgeResponse
            {
                Success = false,
                Error = "Connection with Obsidian closed unexpectedly."
            });
        }
    }

    public void Dispose()
    {
        _connectionSemaphore.Dispose();
        _loopCts?.Dispose();
        _webSocket?.Dispose();
    }
}

// Bridge Protocol Types (AOT Safe)

public static class BridgeProtocol
{
    public const int Version = 2;
}

public sealed class BridgeMessage
{
    [JsonPropertyName("command")]
    public required string Command { get; set; }

    [JsonPropertyName("payload")]
    public JsonNode? Payload { get; set; }

    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("protocolVersion")]
    public int? ProtocolVersion { get; set; }
}

public sealed class BridgeResponse
{
    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public JsonNode? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("protocolVersion")]
    public int? ProtocolVersion { get; set; }

    /// <summary>
    /// True when this failed response represents an authentication failure (invalid/missing
    /// bridge token, or auth required but never sent). Lets tool code surface a distinct
    /// [UNAUTHORIZED] error instead of a generic "Obsidian plugin error".
    /// </summary>
    public bool IsUnauthorized() => Error?.Contains("[UNAUTHORIZED]", StringComparison.Ordinal) == true;
}

/// <summary>
/// Result of a best-effort attempt to have Templater evaluate an already-written note in place.
/// <see cref="NotNeeded"/> means the content had no Templater syntax, so no bridge call was made.
/// </summary>
public readonly record struct TemplaterEvaluationResult(bool Applied, string? Warning)
{
    public static readonly TemplaterEvaluationResult NotNeeded = new(false, null);
}

[JsonSerializable(typeof(BridgeMessage))]
[JsonSerializable(typeof(BridgeResponse))]
internal partial class BridgeJsonContext : JsonSerializerContext
{
}
