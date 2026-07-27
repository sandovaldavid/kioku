using System.Runtime.InteropServices;

namespace Kioku.Benchmarks.Suite;

/// <summary>
/// Best-effort collection of hardware/OS/runtime metadata for the report's metadata block.
/// A friendly CPU model name is only available on Linux (via /proc/cpuinfo); elsewhere we
/// fall back to processor count and architecture only, and say so explicitly.
/// </summary>
public sealed record HardwareSnapshot(
    string? CpuModel,
    int ProcessorCount,
    string OsArchitecture,
    string OsDescription,
    string RuntimeDescription,
    string Notes)
{
    public static HardwareSnapshot Capture()
    {
        var cpuModel = TryReadLinuxCpuModel();
        var notes = cpuModel is not null
            ? "CPU model read from /proc/cpuinfo (Linux)."
            : "No friendly CPU model name available in this sandbox; falling back to processor " +
              "count and architecture only.";

        return new HardwareSnapshot(
            cpuModel,
            Environment.ProcessorCount,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            notes);
    }

    private static string? TryReadLinuxCpuModel()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                if (!line.StartsWith("model name", StringComparison.Ordinal))
                {
                    continue;
                }

                var separator = line.IndexOf(':');
                if (separator < 0)
                {
                    continue;
                }

                return line[(separator + 1)..].Trim();
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }
}
