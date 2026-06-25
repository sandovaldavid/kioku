using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kioku.Mcp.Server.Logging;
using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Services;

public sealed class ObsidianBridgeService : IDisposable
{
    private readonly ILogger<ObsidianBridgeService> _logger;
    private readonly int _port;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<BridgeResponse>> _pendingRequests = new();
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _loopCts;
    private Task? _receiveLoopTask;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);

    public ObsidianBridgeService(ILogger<ObsidianBridgeService> logger, KiokuConfiguration config)
    {
        _logger = logger;
        _port = config.ObsidianBridgePort;
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
            return new BridgeResponse
            {
                Success = false,
                Error = $"Could not connect to Obsidian. Make sure Obsidian is open and the Kioku MCP plugin is activated on port {_port}. Details: {ex.Message}"
            };
        }

        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<BridgeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = tcs;

        var message = new BridgeMessage
        {
            Command = command,
            Payload = payload,
            RequestId = requestId
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
        }
        finally
        {
            _connectionSemaphore.Release();
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

    private async Task CloseAndResetAsync()
    {
        if (_loopCts is not null)
        {
            await _loopCts.CancelAsync();
            _loopCts.Dispose();
            _loopCts = null;
        }

        if (_webSocket is not null)
        {
            try
            {
                if (_webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", closeCts.Token);
                }
            }
            catch
            {
                // Ignore closing errors
            }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
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

public sealed class BridgeMessage
{
    [JsonPropertyName("command")]
    public required string Command { get; set; }

    [JsonPropertyName("payload")]
    public JsonNode? Payload { get; set; }

    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }
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
}

[JsonSerializable(typeof(BridgeMessage))]
[JsonSerializable(typeof(BridgeResponse))]
internal partial class BridgeJsonContext : JsonSerializerContext
{
}
