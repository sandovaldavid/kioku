using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using NJsonSchema;

namespace Kioku.Mcp.Server.Domain.Coordination;

/// <summary>
/// Maps public contract kinds to their reviewed embedded schemas.
/// </summary>
public static class CoordinationSchemaCatalog
{
    public static string GetFileName(CoordinationContractKind kind) => kind switch
    {
        CoordinationContractKind.HandoffPacket => "handoff-packet.schema.json",
        CoordinationContractKind.CoordinationEvent => "coordination-event.schema.json",
        CoordinationContractKind.CoordinationClaim => "coordination-claim.schema.json",
        CoordinationContractKind.CoordinationConflict => "coordination-conflict.schema.json",
        CoordinationContractKind.WorkItemProjection => "work-item-projection.schema.json",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown coordination contract kind."),
    };
}

/// <summary>
/// Stable error codes returned by contract validation.
/// </summary>
public static class CoordinationContractErrorCodes
{
    public const string InvalidJson = "invalid-json";
    public const string InvalidContract = "invalid-contract";
    public const string UnsupportedSchemaVersion = "unsupported-schema-version";
    public const string ContentHashMismatch = "content-hash-mismatch";
}

/// <summary>
/// A stable, content-safe validation error.
/// </summary>
public sealed record CoordinationValidationError(string Path, string Code);

/// <summary>
/// Result returned by coordination schema validation.
/// </summary>
public sealed record CoordinationValidationResult(IReadOnlyList<CoordinationValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static CoordinationValidationResult Valid { get; } = new([]);
}

/// <summary>
/// Validates versioned coordination JSON against schemas embedded in the server assembly.
/// </summary>
public sealed class CoordinationContractValidator
{
    private static readonly ConcurrentDictionary<CoordinationContractKind, Task<JsonSchema>> SchemaCache = new();
    private static readonly Assembly ContractAssembly = typeof(CoordinationContractValidator).Assembly;

    public async Task<CoordinationValidationResult> ValidateAsync(
        CoordinationContractKind kind,
        string json,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            return await ValidateAsync(kind, document.RootElement, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return new([new CoordinationValidationError("$", CoordinationContractErrorCodes.InvalidJson)]);
        }
    }

    public async Task<CoordinationValidationResult> ValidateAsync(
        CoordinationContractKind kind,
        JsonElement document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (document.ValueKind == JsonValueKind.Object &&
            document.TryGetProperty("schema_version", out var schemaVersion) &&
            schemaVersion.ValueKind == JsonValueKind.Number &&
            schemaVersion.TryGetInt32(out var version) &&
            version != CoordinationContract.CurrentSchemaVersion)
        {
            return new(
            [
                new CoordinationValidationError(
                    "$.schema_version",
                    CoordinationContractErrorCodes.UnsupportedSchemaVersion),
            ]);
        }

        var schema = await GetSchemaAsync(kind).WaitAsync(cancellationToken).ConfigureAwait(false);
        var errors = schema.Validate(document.GetRawText())
            .Select(error => new CoordinationValidationError(
                string.IsNullOrEmpty(error.Path) ? "$" : error.Path,
                CoordinationContractErrorCodes.InvalidContract))
            .Distinct()
            .OrderBy(error => error.Path, StringComparer.Ordinal)
            .ThenBy(error => error.Code, StringComparer.Ordinal)
            .ToArray();

        if (errors.Length > 0)
        {
            return new(errors);
        }

        try
        {
            return CanonicalJson.ContentHashMatches(document, CoordinationContract.ContentHashPropertyName)
                ? CoordinationValidationResult.Valid
                : new(
                [
                    new CoordinationValidationError(
                        "$.content_hash",
                        CoordinationContractErrorCodes.ContentHashMismatch),
                ]);
        }
        catch (JsonException)
        {
            return new([new CoordinationValidationError("$", CoordinationContractErrorCodes.InvalidContract)]);
        }
    }

    public Task<CoordinationValidationResult> ValidateAsync<T>(
        CoordinationContractKind kind,
        T contract,
        CancellationToken cancellationToken = default)
    {
        var json = CoordinationContractSerializer.Serialize(contract);
        return ValidateAsync(kind, json, cancellationToken);
    }

    private static Task<JsonSchema> GetSchemaAsync(CoordinationContractKind kind) =>
        SchemaCache.GetOrAdd(kind, static contractKind => LoadSchemaAsync(contractKind));

    private static async Task<JsonSchema> LoadSchemaAsync(CoordinationContractKind kind)
    {
        var fileName = CoordinationSchemaCatalog.GetFileName(kind);
        var resourceName = ContractAssembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith($".{fileName}", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException($"Embedded coordination schema was not found: {fileName}");
        }

        await using var stream = ContractAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded coordination schema could not be opened: {fileName}");
        using var reader = new StreamReader(stream);
        var schemaJson = await reader.ReadToEndAsync().ConfigureAwait(false);
        return await JsonSchema.FromJsonAsync(schemaJson).ConfigureAwait(false);
    }
}
