using System.Text.Json;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Validates the wire-format fixtures under Fixtures/BridgeProtocol/ against the current
/// BridgeMessage/BridgeResponse contracts and the declared BridgeProtocol version range.
///
/// These fixtures are the compatibility contract between this server and the Obsidian plugin,
/// which now lives in its own repository (sandovaldavid/kioku-obsidian) and keeps its own copy
/// of the same files, validated by its own test. If a future change to ObsidianBridgeService's
/// wire format breaks one of these assertions, that is the intended failure: the fixtures (and
/// this test) exist so a breaking change trips locally on whichever side made it, instead of
/// only surfacing at runtime against the other repo.
/// </summary>
public class BridgeProtocolFixtureTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "BridgeProtocol");

    private static JsonElement LoadMessage(string fileName)
    {
        var path = Path.Combine(FixtureDir, fileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("message").Clone();
    }

    [Fact]
    public void AuthRequest_DeserializesAsBridgeMessage_WithHandshakePayload()
    {
        var message = LoadMessage("auth-request.json");
        var bridgeMessage = JsonSerializer.Deserialize(message.GetRawText(), BridgeJsonContext.Default.BridgeMessage);

        Assert.NotNull(bridgeMessage);
        Assert.Equal("auth", bridgeMessage!.Command);
        Assert.Equal("auth-1", bridgeMessage.RequestId);
        Assert.NotNull(bridgeMessage.ProtocolVersion);
        Assert.InRange(bridgeMessage.ProtocolVersion!.Value, BridgeProtocol.MinVersion, BridgeProtocol.MaxVersion);

        var payload = Assert.IsType<JsonObject>(bridgeMessage.Payload);
        Assert.Equal(3, payload["minProtocolVersion"]!.GetValue<int>());
        Assert.Equal(3, payload["maxProtocolVersion"]!.GetValue<int>());
        Assert.Equal("kioku-mcp-server", payload["clientName"]!.GetValue<string>());
        var requestedCapabilities = Assert.IsType<JsonArray>(payload["requestedCapabilities"]);
        Assert.Contains("read", requestedCapabilities.Select(node => node!.GetValue<string>()));
        Assert.Contains("unsafe-command", requestedCapabilities.Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public void CommandRequest_DeserializesAsBridgeMessage_WithRuntimeCommandPayload()
    {
        var message = LoadMessage("command-request.json");
        var bridgeMessage = JsonSerializer.Deserialize(message.GetRawText(), BridgeJsonContext.Default.BridgeMessage);

        Assert.NotNull(bridgeMessage);
        Assert.Equal("open-file", bridgeMessage!.Command);
        Assert.Equal("req-1", bridgeMessage.RequestId);
        Assert.NotNull(bridgeMessage.ProtocolVersion);
        Assert.InRange(bridgeMessage.ProtocolVersion!.Value, BridgeProtocol.MinVersion, BridgeProtocol.MaxVersion);

        var payload = Assert.IsType<JsonObject>(bridgeMessage.Payload);
        Assert.Equal("Notes/Example.md", payload["path"]!.GetValue<string>());
    }

    [Fact]
    public void AuthResponse_DeserializesAsBridgeResponse_WithNegotiatedVersionAndCapabilities()
    {
        var message = LoadMessage("auth-response.json");
        var response = JsonSerializer.Deserialize(message.GetRawText(), BridgeJsonContext.Default.BridgeResponse);

        Assert.NotNull(response);
        Assert.Equal("auth-1", response!.RequestId);
        Assert.True(response.Success);
        Assert.False(response.IsUnauthorized());
        Assert.NotNull(response.ProtocolVersion);
        Assert.InRange(response.ProtocolVersion!.Value, BridgeProtocol.MinVersion, BridgeProtocol.MaxVersion);

        var data = Assert.IsType<JsonObject>(response.Data);
        Assert.Equal(3, data["negotiatedProtocolVersion"]!.GetValue<int>());
        var capabilities = Assert.IsType<JsonArray>(data["capabilities"]);
        Assert.Equal(
            new[] { "read", "ui-navigation", "vault-wide" },
            capabilities.Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public void AuthResponseUnauthorized_DeserializesAsBridgeResponse_AndIsFlaggedUnauthorized()
    {
        var message = LoadMessage("auth-response-unauthorized.json");
        var response = JsonSerializer.Deserialize(message.GetRawText(), BridgeJsonContext.Default.BridgeResponse);

        Assert.NotNull(response);
        Assert.Equal("auth-1", response!.RequestId);
        Assert.False(response.Success);
        Assert.Equal("UNAUTHORIZED", response.ErrorCode);
        Assert.Contains("[UNAUTHORIZED]", response.Error);
        Assert.True(response.IsUnauthorized());
        Assert.NotNull(response.ProtocolVersion);
        Assert.InRange(response.ProtocolVersion!.Value, BridgeProtocol.MinVersion, BridgeProtocol.MaxVersion);
    }

    [Fact]
    public void CommandResponseSuccess_DeserializesAsBridgeResponse_WithApplicationData()
    {
        var message = LoadMessage("command-response-success.json");
        var response = JsonSerializer.Deserialize(message.GetRawText(), BridgeJsonContext.Default.BridgeResponse);

        Assert.NotNull(response);
        Assert.Equal("req-1", response!.RequestId);
        Assert.True(response.Success);
        Assert.False(response.IsUnauthorized());
        var data = Assert.IsType<JsonObject>(response.Data);
        Assert.True(data["opened"]!.GetValue<bool>());
        Assert.NotNull(response.ProtocolVersion);
        Assert.InRange(response.ProtocolVersion!.Value, BridgeProtocol.MinVersion, BridgeProtocol.MaxVersion);
    }

    [Fact]
    public void CommandResponseError_DeserializesAsBridgeResponse_AsApplicationFailureNotUnauthorized()
    {
        var message = LoadMessage("command-response-error.json");
        var response = JsonSerializer.Deserialize(message.GetRawText(), BridgeJsonContext.Default.BridgeResponse);

        Assert.NotNull(response);
        Assert.Equal("req-1", response!.RequestId);
        Assert.False(response.Success);
        Assert.Equal("File not found: Notes/Example.md", response.Error);
        Assert.Null(response.ErrorCode);
        Assert.False(response.IsUnauthorized());
        Assert.NotNull(response.ProtocolVersion);
        Assert.InRange(response.ProtocolVersion!.Value, BridgeProtocol.MinVersion, BridgeProtocol.MaxVersion);
    }

    [Fact]
    public void DeclaredProtocolRange_IsValid_AndEveryFixtureFallsWithinIt()
    {
        Assert.True(
            BridgeProtocol.MinVersion <= BridgeProtocol.MaxVersion,
            $"BridgeProtocol.MinVersion ({BridgeProtocol.MinVersion}) must not exceed MaxVersion ({BridgeProtocol.MaxVersion}).");

        var fixtureFiles = new[]
        {
            "auth-request.json",
            "auth-response.json",
            "auth-response-unauthorized.json",
            "command-request.json",
            "command-response-success.json",
            "command-response-error.json",
        };

        foreach (var fileName in fixtureFiles)
        {
            var message = LoadMessage(fileName);
            var protocolVersion = message.GetProperty("protocolVersion").GetInt32();
            Assert.InRange(protocolVersion, BridgeProtocol.MinVersion, BridgeProtocol.MaxVersion);
        }
    }
}
