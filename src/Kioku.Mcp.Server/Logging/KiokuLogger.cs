using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Logging;

// This compatibility facade intentionally forwards caller-owned templates and arguments.
#pragma warning disable CA1848
#pragma warning disable CA2254
internal static class KiokuLogger
{
    public static void Info(this ILogger logger, string message, params object?[] args) =>
        logger.LogInformation(message, args);

    public static void Warn(this ILogger logger, string message, params object?[] args) =>
        logger.LogWarning(message, args);

    public static void Warn(this ILogger logger, Exception ex, string message, params object?[] args) =>
        logger.LogWarning(ex, message, args);

    public static void Error(this ILogger logger, string message, params object?[] args) =>
        logger.LogError(message, args);

    public static void Error(this ILogger logger, Exception ex, string message, params object?[] args) =>
        logger.LogError(ex, message, args);

    public static void Debug(this ILogger logger, string message, params object?[] args) =>
        logger.LogDebug(message, args);

    public static void Debug(this ILogger logger, Exception ex, string message, params object?[] args) =>
        logger.LogDebug(ex, message, args);
}
#pragma warning restore CA2254
#pragma warning restore CA1848
