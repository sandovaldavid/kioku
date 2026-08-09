using Kioku.Mcp.Server.Protocol;
using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class KiokuStartupReadinessGateTests
{
    [Theory]
    [InlineData("get_server_capabilities")]
    [InlineData("get_server_status")]
    [InlineData("list_projects")]
    [InlineData("get_project_context")]
    public void Warmup_safe_tools_do_not_wait_for_the_index(string toolName)
    {
        Assert.False(KiokuTypedResultFilters.RequiresReadyIndex(toolName));
    }

    [Theory]
    [InlineData("read_note")]
    [InlineData("search_notes")]
    [InlineData("create_note")]
    [InlineData("edit_note")]
    [InlineData("start_work_session")]
    [InlineData("list_work_sessions")]
    public void Index_dependent_tools_wait_for_cold_start(string toolName)
    {
        Assert.True(KiokuTypedResultFilters.RequiresReadyIndex(toolName));
    }

    [Fact]
    public async Task Gate_releases_waiters_only_after_cold_index_is_ready()
    {
        var gate = new VaultIndexReadinessGate();
        var wait = gate.WaitAsync();

        Assert.False(gate.IsReady);
        Assert.False(wait.IsCompleted);

        gate.MarkReady();
        await wait;

        Assert.True(gate.IsReady);
    }

    [Fact]
    public async Task Gate_propagates_cold_index_failure()
    {
        var gate = new VaultIndexReadinessGate();
        var expected = new InvalidOperationException("cold index failed");

        gate.MarkFailed(expected);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => gate.WaitAsync());
        Assert.Same(expected, actual);
        Assert.False(gate.IsReady);
    }

    [Fact]
    public async Task Caller_cancellation_does_not_change_gate_state()
    {
        var gate = new VaultIndexReadinessGate();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gate.WaitAsync(cancellation.Token));

        Assert.False(gate.IsReady);

        gate.MarkReady();
        await gate.WaitAsync();
        Assert.True(gate.IsReady);
    }
}
