using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kioku.Benchmarks.Suite;

/// <summary>
/// Links Recall@K/MRR/NDCG@K to the golden set by shelling out to the existing
/// scripts/Kioku.Eval runner and parsing its Markdown tables — the SAME computation
/// docs/retrieval-eval.md documents as authoritative (RetrievalMetrics.cs +
/// GoldenSet.Load), not a parallel reimplementation that could silently drift from it.
/// </summary>
public static partial class RetrievalQualityBenchmark
{
    public sealed record MetricRow(int K, double PrecisionAtK, double RecallAtK, double Mrr, double NdcgAtK);

    public sealed record ModeResult(string Mode, IReadOnlyList<MetricRow> Rows, double? NoAnswerProbeAvgResults);

    public sealed record Result(
        string Command,
        string VaultPath,
        string GoldenPath,
        string RawMarkdown,
        IReadOnlyList<ModeResult> Modes);

    public static async Task<Result> RunAsync(
        string repoRoot,
        string evalProjectPath,
        string vaultPath,
        string goldenPath,
        string label,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("[info] Building Kioku.Eval (Release, dotnet build)...");
        using (var build = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "build", evalProjectPath, "--configuration", "Release" },
            UseShellExecute = false,
            WorkingDirectory = repoRoot,
        }) ?? throw new InvalidOperationException("Unable to start 'dotnet build' for Kioku.Eval."))
        {
            await build.WaitForExitAsync(cancellationToken);
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException($"'dotnet build {evalProjectPath}' failed with exit code {build.ExitCode}.");
            }
        }

        string[] arguments =
        [
            "run", "--project", evalProjectPath, "--configuration", "Release", "--no-build", "--",
            "--vault", vaultPath, "--golden", goldenPath,
            "--modes", "keyword,semantic,hybrid", "--k", "5,10", "--label", label,
        ];
        var command = $"dotnet {string.Join(' ', arguments)}";
        Console.WriteLine($"[loading] Retrieval quality: {command}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = repoRoot,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start 'dotnet run' for Kioku.Eval.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Kioku.Eval exited with code {process.ExitCode}.\n--- stderr ---\n{stderr}\n--- stdout ---\n{stdout}");
        }

        Console.WriteLine("[ok] Retrieval quality eval completed.");
        var modes = ParseMarkdown(stdout);
        return new Result(command, vaultPath, goldenPath, stdout, modes);
    }

    private static List<ModeResult> ParseMarkdown(string markdown)
    {
        var results = new List<ModeResult>();
        string? currentMode = null;
        var rows = new List<MetricRow>();
        double? probeAvg = null;

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var headingMatch = ModeHeadingRegex().Match(line);
            if (headingMatch.Success)
            {
                FlushMode(results, ref currentMode, rows, ref probeAvg);
                currentMode = headingMatch.Groups[1].Value.Trim();
                continue;
            }

            var rowMatch = TableRowRegex().Match(line);
            if (rowMatch.Success && currentMode is not null)
            {
                rows.Add(new MetricRow(
                    int.Parse(rowMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                    double.Parse(rowMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                    double.Parse(rowMatch.Groups[3].Value, CultureInfo.InvariantCulture),
                    double.Parse(rowMatch.Groups[4].Value, CultureInfo.InvariantCulture),
                    double.Parse(rowMatch.Groups[5].Value, CultureInfo.InvariantCulture)));
                continue;
            }

            var probeMatch = ProbeRegex().Match(line);
            if (probeMatch.Success)
            {
                probeAvg = double.Parse(probeMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        FlushMode(results, ref currentMode, rows, ref probeAvg);
        return results;
    }

    private static void FlushMode(
        List<ModeResult> results, ref string? currentMode, List<MetricRow> rows, ref double? probeAvg)
    {
        if (currentMode is not null && rows.Count > 0)
        {
            results.Add(new ModeResult(currentMode, [.. rows], probeAvg));
        }

        currentMode = null;
        rows.Clear();
        probeAvg = null;
    }

    [GeneratedRegex(@"^##\s+(\S+)\s*$")]
    private static partial Regex ModeHeadingRegex();

    [GeneratedRegex(@"^\|\s*(\d+)\s*\|\s*([\d.]+)\s*\|\s*([\d.]+)\s*\|\s*([\d.]+)\s*\|\s*([\d.]+)\s*\|\s*$")]
    private static partial Regex TableRowRegex();

    [GeneratedRegex(@"No-answer probes: avg ([\d.]+) results returned")]
    private static partial Regex ProbeRegex();
}
