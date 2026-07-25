using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class VaultIndexingMetricsTests
{
    [Fact]
    public void QueueDepthBalancesWhenConsumerReportsBeforeProducer()
    {
        var metrics = new VaultIndexingMetrics();

        metrics.ChangeDequeued();
        metrics.ChangeQueued();

        var snapshot = metrics.Snapshot;
        Assert.Equal(1, snapshot.QueuedChanges);
        Assert.Equal(0, snapshot.QueueDepth);
    }

    [Fact]
    public void QueueDepthReportsOutstandingAcceptedChanges()
    {
        var metrics = new VaultIndexingMetrics();

        metrics.ChangeQueued();
        metrics.ChangeQueued();
        metrics.ChangeDequeued();

        var snapshot = metrics.Snapshot;
        Assert.Equal(2, snapshot.QueuedChanges);
        Assert.Equal(1, snapshot.QueueDepth);
    }
}
