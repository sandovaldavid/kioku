namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Stable identifiers and versions for public Kioku capability negotiation.
/// </summary>
public static class KiokuCapabilityCatalog
{
    public const string CoordinationProfileId = "kioku.durable-coordination";
    public const int CoordinationProfileVersion = 1;
    public const int CoordinationSchemaVersion = 1;

    public static IReadOnlyList<string> CoordinationFeatures { get; } =
    [
        "coordination.core",
        "coordination.history",
        "coordination.claims",
        "coordination.fencing",
        "coordination.cas",
        "coordination.conflicts",
    ];
}
