using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Logging;

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
