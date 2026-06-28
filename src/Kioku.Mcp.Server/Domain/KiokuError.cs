namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Standardized error taxonomy for Kioku MCP tool responses.
/// </summary>
public static class KiokuError
{
    /// <summary>Resource was not found.</summary>
    public const string NotFoundCode = "NOT_FOUND";

    /// <summary>Input was missing, empty, or otherwise invalid.</summary>
    public const string InvalidArgumentCode = "INVALID_ARGUMENT";

    /// <summary>Authentication or authorization failed.</summary>
    public const string UnauthorizedCode = "UNAUTHORIZED";

    /// <summary>An internal or unexpected failure occurred.</summary>
    public const string InternalCode = "INTERNAL";

    /// <summary>Dependency (Obsidian plugin, Ollama, third-party tool) is unavailable.</summary>
    public const string DependencyUnavailableCode = "DEPENDENCY_UNAVAILABLE";

    /// <summary>Creates a prefixed error string with an optional stable code.</summary>
    public static string Format(string code, string message) => $"[error] [{code}] {message}";

    /// <summary>Creates a not-found error.</summary>
    public static string NotFound(string message) => Format(NotFoundCode, message);

    /// <summary>Creates an invalid-argument error.</summary>
    public static string InvalidArgument(string message) => Format(InvalidArgumentCode, message);

    /// <summary>Creates an unauthorized error.</summary>
    public static string Unauthorized(string message) => Format(UnauthorizedCode, message);

    /// <summary>Creates an internal error.</summary>
    public static string Internal(string message) => Format(InternalCode, message);

    /// <summary>Creates a dependency-unavailable error.</summary>
    public static string DependencyUnavailable(string message) => Format(DependencyUnavailableCode, message);
}
