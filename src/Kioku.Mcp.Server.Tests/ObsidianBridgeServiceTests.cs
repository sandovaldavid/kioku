using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Exercises ObsidianBridgeService's WebSocket client against a real (Kestrel-hosted) fake
/// Obsidian server on an ephemeral port, mirroring the plugin side's protocol.contract.test.ts
/// approach of using a real socket instead of a mocked one.
/// </summary>
public class ObsidianBridgeServiceTests
{
    [Fact]
    public async Task SendRequestAsync_RoundTrip_ReturnsResponseAndStampsProtocolVersion()
    {
        await using var server = await FakeObsidianServer.StartAsync();
        var config = new KiokuConfiguration { VaultPath = "/tmp", ObsidianBridgePort = server.Port };
        using var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);

        var serverSide = Task.Run(async () =>
        {
            var socket = await server.AcceptConnectionAsync();
            var raw = await server.ReceiveAsync(socket);
            var message = JsonDocument.Parse(raw).RootElement;

            Assert.Equal("ping", message.GetProperty("command").GetString());
            Assert.Equal(1, message.GetProperty("protocolVersion").GetInt32());
            var requestId = message.GetProperty("requestId").GetString();

            var response = JsonSerializer.Serialize(new
            {
                requestId,
                success = true,
                data = new { pong = true },
                error = (string?)null,
                protocolVersion = 1
            });
            await server.SendAsync(socket, response);
        });

        var result = await bridge.SendRequestAsync("ping");
        await serverSide;

        Assert.True(result.Success);
        Assert.True(result.Data!["pong"]!.GetValue<bool>());
    }

    [Fact]
    public async Task SendRequestAsync_UnknownCommand_ReturnsServerReportedError()
    {
        await using var server = await FakeObsidianServer.StartAsync();
        var config = new KiokuConfiguration { VaultPath = "/tmp", ObsidianBridgePort = server.Port };
        using var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);

        var serverSide = Task.Run(async () =>
        {
            var socket = await server.AcceptConnectionAsync();
            var raw = await server.ReceiveAsync(socket);
            var requestId = JsonDocument.Parse(raw).RootElement.GetProperty("requestId").GetString();

            var response = JsonSerializer.Serialize(new
            {
                requestId,
                success = false,
                data = (object?)null,
                error = "Unknown command: does-not-exist",
                protocolVersion = 1
            });
            await server.SendAsync(socket, response);
        });

        var result = await bridge.SendRequestAsync("does-not-exist");
        await serverSide;

        Assert.False(result.Success);
        Assert.Equal("Unknown command: does-not-exist", result.Error);
    }

    [Fact]
    public async Task SendRequestAsync_ConnectionDroppedWhilePending_ReturnsConnectionClosedError()
    {
        await using var server = await FakeObsidianServer.StartAsync();
        var config = new KiokuConfiguration { VaultPath = "/tmp", ObsidianBridgePort = server.Port };
        using var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);

        var serverSide = Task.Run(async () =>
        {
            var socket = await server.AcceptConnectionAsync();
            await server.ReceiveAsync(socket);
            server.DropConnection(socket);
        });

        var result = await bridge.SendRequestAsync("ping");
        await serverSide;

        Assert.False(result.Success);
        Assert.Equal("Connection with Obsidian closed unexpectedly.", result.Error);
    }

    [Fact]
    public async Task SendRequestAsync_ReconnectsAfterServerClosesConnection()
    {
        await using var server = await FakeObsidianServer.StartAsync();
        var config = new KiokuConfiguration { VaultPath = "/tmp", ObsidianBridgePort = server.Port };
        using var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);

        var firstConnection = Task.Run(async () =>
        {
            var socket = await server.AcceptConnectionAsync();
            await server.ReceiveAsync(socket);
            await server.CloseConnectionAsync(socket);
        });

        var firstResult = await bridge.SendRequestAsync("ping");
        await firstConnection;

        Assert.False(firstResult.Success);

        // Give the client's receive loop a moment to finish tearing down the closed socket
        // before the next request tries to reconnect.
        await Task.Delay(200);

        var secondConnection = Task.Run(async () =>
        {
            var socket = await server.AcceptConnectionAsync();
            var raw = await server.ReceiveAsync(socket);
            var requestId = JsonDocument.Parse(raw).RootElement.GetProperty("requestId").GetString();

            var response = JsonSerializer.Serialize(new
            {
                requestId,
                success = true,
                data = new { pong = true },
                error = (string?)null,
                protocolVersion = 1
            });
            await server.SendAsync(socket, response);
        });

        var secondResult = await bridge.SendRequestAsync("ping");
        await secondConnection;

        Assert.True(secondResult.Success);
        Assert.True(secondResult.Data!["pong"]!.GetValue<bool>());
    }

    [Fact]
    public async Task SendRequestAsync_NoResponseWithinTenSeconds_ReturnsTimeoutError()
    {
        await using var server = await FakeObsidianServer.StartAsync();
        var config = new KiokuConfiguration { VaultPath = "/tmp", ObsidianBridgePort = server.Port };
        using var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);

        var serverSide = Task.Run(async () =>
        {
            var socket = await server.AcceptConnectionAsync();
            await server.ReceiveAsync(socket);
            // Deliberately never respond, holding the connection open past the client's timeout.
        });

        var result = await bridge.SendRequestAsync("ping");

        Assert.False(result.Success);
        Assert.Equal("Timeout: The request to Obsidian timed out.", result.Error);

        await server.CloseAllAsync();
        await serverSide;
    }
}

/// <summary>
/// Minimal Kestrel-hosted WebSocket server standing in for the Obsidian plugin's bridge,
/// bound to an ephemeral port. Tests drive it directly via Accept/Send/Receive/Close instead
/// of scripting behavior through a mocking framework.
/// </summary>
internal sealed class FakeObsidianServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly System.Threading.Channels.Channel<WebSocket> _acceptedSockets =
        System.Threading.Channels.Channel.CreateUnbounded<WebSocket>();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<WebSocket, TaskCompletionSource> _connectionHolds = new();

    private FakeObsidianServer(WebApplication app)
    {
        _app = app;
    }

    public int Port { get; private set; }

    public static async Task<FakeObsidianServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var server = new FakeObsidianServer(app);

        app.UseWebSockets();
        app.Map("/", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync();
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            server._connectionHolds[socket] = hold;
            await server._acceptedSockets.Writer.WriteAsync(socket);

            // Keep the HTTP request (and thus the WebSocket) alive until the test ends it.
            await hold.Task;
        });

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses;
        server.Port = new Uri(addresses.First()).Port;
        return server;
    }

    public async Task<WebSocket> AcceptConnectionAsync(CancellationToken cancellationToken = default) =>
        await _acceptedSockets.Reader.ReadAsync(cancellationToken);

    public async Task SendAsync(WebSocket socket, string json, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    public async Task<string> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[4096];
        var builder = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);
        return builder.ToString();
    }

    public async Task CloseConnectionAsync(WebSocket socket)
    {
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test close", CancellationToken.None);
        ReleaseHold(socket);
    }

    public void DropConnection(WebSocket socket)
    {
        socket.Abort();
        ReleaseHold(socket);
    }

    public async Task CloseAllAsync()
    {
        foreach (var socket in _connectionHolds.Keys)
        {
            DropConnection(socket);
        }
        await Task.CompletedTask;
    }

    private void ReleaseHold(WebSocket socket)
    {
        if (_connectionHolds.TryRemove(socket, out var hold))
        {
            hold.TrySetResult();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAllAsync();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
