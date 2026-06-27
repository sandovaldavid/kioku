namespace Kioku.Mcp.Server.Logging;

/// <summary>
/// Minimal bootstrap logger for use before DI is configured.
/// Writes to stderr with a simple format. Used only during startup
/// for configuration errors that occur before the full logging
/// pipeline is available.
/// </summary>
internal static class BootstrapLogger
{
    public static void Error(string message)
    {
        Console.Error.WriteLine($"[error] {message}");
    }

    public static void Error(string format, params object[] args)
    {
        Console.Error.WriteLine($"[error] {string.Format(format, args)}");
    }

    public static void Warn(string message)
    {
        Console.Error.WriteLine($"[warn] {message}");
    }

    public static void Info(string message)
    {
        Console.Error.WriteLine($"[info] {message}");
    }
}
