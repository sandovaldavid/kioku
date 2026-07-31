using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kioku.Mcp.Server.Domain.Coordination;

/// <summary>
/// Serializes coordination documents into stable UTF-8 JSON and computes their content hashes.
/// </summary>
public static class CoordinationContractSerializer
{
    public static string Serialize<T>(T value) => CanonicalJson.Serialize(ToJsonElement(value));

    public static string ComputeContentHash<T>(T value) =>
        CanonicalJson.ComputeSha256Hex(ToJsonElement(value), CoordinationContract.ContentHashPropertyName);

    public static bool ContentHashMatches(JsonElement document) =>
        CanonicalJson.ContentHashMatches(document, CoordinationContract.ContentHashPropertyName);

    public static JsonElement ToJsonElement<T>(T value) => value switch
    {
        HandoffPacket packet => JsonSerializer.SerializeToElement(packet, CoordinationJsonContext.Default.HandoffPacket),
        CoordinationEvent coordinationEvent => JsonSerializer.SerializeToElement(
            coordinationEvent,
            CoordinationJsonContext.Default.CoordinationEvent),
        CoordinationClaim claim => JsonSerializer.SerializeToElement(claim, CoordinationJsonContext.Default.CoordinationClaim),
        CoordinationConflict conflict => JsonSerializer.SerializeToElement(
            conflict,
            CoordinationJsonContext.Default.CoordinationConflict),
        WorkItemProjection projection => JsonSerializer.SerializeToElement(
            projection,
            CoordinationJsonContext.Default.WorkItemProjection),
        _ => throw new ArgumentException(
            $"Unsupported coordination contract type: {typeof(T).FullName}",
            nameof(value)),
    };
}

/// <summary>
/// Implements the canonical representation shared by all coordination documents.
/// </summary>
public static class CanonicalJson
{
    public static string Serialize(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteElement(element, writer, isRoot: true, excludedRootProperty: null);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string ComputeSha256Hex(JsonElement element, string excludedRootProperty)
    {
        ArgumentException.ThrowIfNullOrEmpty(excludedRootProperty);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteElement(element, writer, isRoot: true, excludedRootProperty);
            writer.Flush();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    public static bool ContentHashMatches(JsonElement element, string hashPropertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(hashPropertyName, out var hashElement) ||
            hashElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var expectedHash = hashElement.GetString();
        if (string.IsNullOrEmpty(expectedHash))
        {
            return false;
        }

        var actualHash = ComputeSha256Hex(element, hashPropertyName);
        return string.Equals(expectedHash, actualHash, StringComparison.Ordinal);
    }

    private static void WriteElement(
        JsonElement element,
        Utf8JsonWriter writer,
        bool isRoot,
        string? excludedRootProperty)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var propertyNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (!propertyNames.Add(property.Name))
                    {
                        throw new JsonException($"Duplicate JSON property: {property.Name}");
                    }

                    if (isRoot && string.Equals(property.Name, excludedRootProperty, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    WriteElement(property.Value, writer, isRoot: false, excludedRootProperty: null);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(item, writer, isRoot: false, excludedRootProperty: null);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                WriteNumber(element, writer);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new JsonException($"Unsupported JSON value kind: {element.ValueKind}");
        }
    }

    private static void WriteNumber(JsonElement element, Utf8JsonWriter writer)
    {
        if (element.TryGetInt64(out var signedValue))
        {
            writer.WriteNumberValue(signedValue);
            return;
        }

        if (element.TryGetUInt64(out var unsignedValue))
        {
            writer.WriteNumberValue(unsignedValue);
            return;
        }

        if (element.TryGetDecimal(out var decimalValue))
        {
            writer.WriteRawValue(decimalValue.ToString("G29", CultureInfo.InvariantCulture));
            return;
        }

        if (element.TryGetDouble(out var doubleValue) && double.IsFinite(doubleValue))
        {
            writer.WriteRawValue(doubleValue.ToString("R", CultureInfo.InvariantCulture));
            return;
        }

        throw new JsonException($"JSON number is outside the supported canonical range: {element.GetRawText()}");
    }
}
