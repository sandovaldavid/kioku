using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kioku.Mcp.Server.Domain.Coordination;

/// <summary>
/// Computes the idempotency identity of a transition independently of server-assigned fields.
/// </summary>
public static class CoordinationEventFingerprint
{
    public static string Compute(CoordinationEvent coordinationEvent)
    {
        var node = JsonNode.Parse(CoordinationContractSerializer.Serialize(coordinationEvent))?.AsObject()
            ?? throw new JsonException("The coordination event did not serialize as an object.");

        foreach (var property in new[]
        {
            "event_id",
            "sequence_number",
            "occurred_at",
            "recorded_at",
            "previous_hash",
            "content_hash",
        })
        {
            node.Remove(property);
        }

        using var document = JsonDocument.Parse(node.ToJsonString());
        var canonical = CanonicalJson.Serialize(document.RootElement);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
