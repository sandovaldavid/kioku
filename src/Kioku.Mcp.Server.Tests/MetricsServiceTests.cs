using System.Diagnostics;
using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class MetricsServiceTests
{
    [Fact]
    public void CoordinationMetrics_AreDisabledAndToolTotalsRemainSeparateByDefault()
    {
        var metrics = new MetricsService(new KiokuConfiguration { VaultPath = "/tmp/kioku" });

        metrics.RecordToolCall("read_note");
        metrics.RecordCoordinationTransition("work-item.created");
        metrics.RecordCoordinationClaim("claim-conflict");

        Assert.Equal(0, metrics.TotalCalls);
        Assert.Empty(metrics.GetCoordinationSnapshot().Counters);
    }

    [Fact]
    public void CoordinationMetrics_UseBoundedCategoriesAndTrackRecovery()
    {
        var metrics = new MetricsService(new KiokuConfiguration
        {
            VaultPath = "/tmp/kioku",
            EnableMetrics = true,
        });

        metrics.RecordToolCall("read_note");
        metrics.RecordCoordinationTransition("caller-controlled-value");
        metrics.RecordCoordinationReplay("corrupt-history");
        metrics.RecordCoordinationClaim("claim-conflict");
        metrics.RecordCoordinationMutation("STALE_FENCE");
        metrics.RecordCoordinationRecovery(TimeSpan.FromMilliseconds(12), succeeded: true);

        var snapshot = metrics.GetCoordinationSnapshot();
        Assert.Equal(1, metrics.TotalCalls);
        Assert.Equal(1, snapshot.Counters["coordination.transitions.other"]);
        Assert.Equal(1, snapshot.Counters["coordination.replay.corrupt-history"]);
        Assert.Equal(1, snapshot.Counters["coordination.claims.contention"]);
        Assert.Equal(1, snapshot.Counters["coordination.mutations.stale-fence"]);
        Assert.Equal(1, snapshot.RecoveryCount);
        Assert.Equal(12, snapshot.RecoveryDurationMilliseconds);
        Assert.All(snapshot.Counters.Keys, key => Assert.StartsWith("coordination.", key, StringComparison.Ordinal));
    }

    [Fact]
    public void Tracing_UsesW3CActivityAndOnlyDomainCorrelationTags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MetricsService.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        var metrics = new MetricsService(new KiokuConfiguration
        {
            VaultPath = "/tmp/kioku",
            EnableTracing = true,
        });

        using var activity = metrics.StartCoordinationActivity(
            "coordination.history.replay",
            runId: "run-1",
            workItemId: "work-1",
            attemptId: "attempt-1",
            sessionId: "session-1");

        Assert.NotNull(activity);
        Assert.Equal(ActivityIdFormat.W3C, activity!.IdFormat);
        Assert.Equal("kioku.durable-coordination", activity.GetTagItem("kioku.profile.id"));
        Assert.Equal("run-1", activity.GetTagItem("kioku.run_id"));
        Assert.Null(activity.GetTagItem("kioku.resource_key"));
        Assert.Null(activity.GetTagItem("kioku.note_body"));
    }
}
