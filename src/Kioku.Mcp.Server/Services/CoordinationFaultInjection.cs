namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Persistence boundaries that may be exercised by deterministic reliability tests.
/// </summary>
internal enum CoordinationFaultPoint
{
    BeforeEventCreation,
    AfterEventDurabilityBeforeProjection,
    DuringProjectionReplacement,
    AfterClaimAcquisition,
    BeforeClaimRenewal,
    AfterClaimRenewal,
    BeforeClaimTakeover,
    AfterClaimTakeover,
    AfterCasValidationBeforeWrite,
    AfterTargetWriteBeforeReindex,
    ProcessShutdown,
    ProcessCancellation,
}

/// <summary>
/// Test-only seam for stopping or pausing an operation at a reviewed persistence boundary.
/// </summary>
internal interface ICoordinationFaultInjector
{
    Task InjectAsync(
        CoordinationFaultPoint point,
        CancellationToken cancellationToken = default);
}

internal sealed class NoOpCoordinationFaultInjector : ICoordinationFaultInjector
{
    internal static readonly NoOpCoordinationFaultInjector Instance = new();

    private NoOpCoordinationFaultInjector()
    {
    }

    public Task InjectAsync(
        CoordinationFaultPoint point,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>
/// Opt-in process fault injector used only by reliability tests. No test-prefixed environment
/// variables means every call is a no-op.
/// </summary>
internal sealed class EnvironmentCoordinationFaultInjector : ICoordinationFaultInjector
{
    private const string PointVariable = "KIOKU_TEST_COORDINATION_FAULT_POINT";
    private const string ActionVariable = "KIOKU_TEST_COORDINATION_FAULT_ACTION";
    private const string SignalVariable = "KIOKU_TEST_COORDINATION_FAULT_SIGNAL_PATH";
    private const string ReleaseVariable = "KIOKU_TEST_COORDINATION_FAULT_RELEASE_PATH";
    private const string TimeoutVariable = "KIOKU_TEST_COORDINATION_FAULT_TIMEOUT_SECONDS";

    private readonly CoordinationFaultPoint point;
    private readonly CoordinationFaultAction action;
    private readonly string? signalPath;
    private readonly string? releasePath;
    private readonly TimeSpan timeout;
    private int triggered;

    private EnvironmentCoordinationFaultInjector(
        CoordinationFaultPoint point,
        CoordinationFaultAction action,
        string? signalPath,
        string? releasePath,
        TimeSpan timeout)
    {
        this.point = point;
        this.action = action;
        this.signalPath = signalPath;
        this.releasePath = releasePath;
        this.timeout = timeout;
    }

    internal static ICoordinationFaultInjector CreateFromEnvironment()
    {
        var configuredPoint = Environment.GetEnvironmentVariable(PointVariable);
        if (string.IsNullOrWhiteSpace(configuredPoint))
        {
            return NoOpCoordinationFaultInjector.Instance;
        }

        if (!Enum.TryParse<CoordinationFaultPoint>(configuredPoint, ignoreCase: true, out var point))
        {
            throw new InvalidOperationException(
                $"{PointVariable} must name a supported coordination fault point.");
        }

        var configuredAction = Environment.GetEnvironmentVariable(ActionVariable);
        if (!Enum.TryParse<CoordinationFaultAction>(configuredAction, ignoreCase: true, out var action))
        {
            throw new InvalidOperationException(
                $"{ActionVariable} must be one of crash, throw, cancel, pause, or signal.");
        }

        var timeoutSeconds = 60;
        var configuredTimeout = Environment.GetEnvironmentVariable(TimeoutVariable);
        if (!string.IsNullOrWhiteSpace(configuredTimeout) &&
            (!int.TryParse(configuredTimeout, out timeoutSeconds) || timeoutSeconds is < 1 or > 600))
        {
            throw new InvalidOperationException(
                $"{TimeoutVariable} must be an integer between 1 and 600.");
        }

        var signalPath = Environment.GetEnvironmentVariable(SignalVariable);
        var releasePath = Environment.GetEnvironmentVariable(ReleaseVariable);
        if (action == CoordinationFaultAction.Pause && string.IsNullOrWhiteSpace(releasePath))
        {
            throw new InvalidOperationException(
                $"{ReleaseVariable} is required when {ActionVariable}=pause.");
        }

        return new EnvironmentCoordinationFaultInjector(
            point,
            action,
            signalPath,
            releasePath,
            TimeSpan.FromSeconds(timeoutSeconds));
    }

    public async Task InjectAsync(
        CoordinationFaultPoint point,
        CancellationToken cancellationToken = default)
    {
        if (this.point != point || Interlocked.Exchange(ref triggered, 1) != 0)
        {
            return;
        }

        Signal();
        switch (action)
        {
            case CoordinationFaultAction.Signal:
                return;

            case CoordinationFaultAction.Pause:
                await WaitForReleaseAsync(cancellationToken).ConfigureAwait(false);
                return;

            case CoordinationFaultAction.Cancel:
                throw new OperationCanceledException(
                    $"Injected cancellation at coordination fault point '{point}'.",
                    cancellationToken);

            case CoordinationFaultAction.Throw:
                throw new CoordinationFaultInjectedException(point);

            case CoordinationFaultAction.Crash:
                Environment.FailFast($"Injected coordination crash at fault point '{point}'.");
                return;

            default:
                throw new InvalidOperationException("The coordination fault action is unsupported.");
        }
    }

    private void Signal()
    {
        if (string.IsNullOrWhiteSpace(signalPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(signalPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(signalPath, point.ToString());
    }

    private async Task WaitForReleaseAsync(CancellationToken cancellationToken)
    {
        var path = releasePath!;
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("A fault release path must include a directory.");
        Directory.CreateDirectory(directory);
        if (File.Exists(path))
        {
            return;
        }

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler onChanged = (_, _) => completed.TrySetResult();
        RenamedEventHandler onRenamed = (_, _) => completed.TrySetResult();
        watcher.Created += onChanged;
        watcher.Changed += onChanged;
        watcher.Renamed += onRenamed;

        if (File.Exists(path))
        {
            return;
        }

        await completed.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    private enum CoordinationFaultAction
    {
        Crash,
        Throw,
        Cancel,
        Pause,
        Signal,
    }
}

internal sealed class CoordinationFaultInjectedException(CoordinationFaultPoint point)
    : InvalidOperationException($"Injected coordination fault at '{point}'.");
