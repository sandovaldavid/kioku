using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class KiokuLifecycleServiceTests
{
    [Fact]
    public async Task Generic_host_runs_shared_initialization_and_shutdown_pipeline()
    {
        var runtime = new RecordingRuntime();
        var now = new DateTimeOffset(2026, 7, 19, 14, 0, 0, TimeSpan.Zero);
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IKiokuRuntime>(runtime);
        builder.Services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        builder.Services.AddSingleton<HttpReadinessState>();
        builder.Services.AddHostedService<KiokuLifecycleService>();

        using var host = builder.Build();
        await host.StartAsync();

        var readiness = host.Services.GetRequiredService<HttpReadinessState>();
        Assert.True(runtime.InitializeCalled);
        Assert.True(readiness.IsReady);
        Assert.Equal(now, readiness.LastUpdatedUtc);

        await host.StopAsync();
        Assert.True(runtime.ShutdownCalled);
    }

    [Fact]
    public async Task Startup_cancellation_is_propagated_and_marks_readiness_failed()
    {
        var runtime = new CancellingRuntime();
        var readiness = new HttpReadinessState();
        var service = new KiokuLifecycleService(
            runtime,
            readiness,
            TimeProvider.System,
            NullLogger<KiokuLifecycleService>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.StartAsync(cancellation.Token));

        Assert.False(readiness.IsReady);
        Assert.Equal("failed", readiness.GetSnapshot().Index);
    }

    [Fact]
    public void Readiness_uses_the_injected_time_provider()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var readiness = new HttpReadinessState(new FixedTimeProvider(now));

        readiness.MarkIndexReady();

        Assert.Equal(now, readiness.LastUpdatedUtc);
    }

    private sealed class RecordingRuntime : IKiokuRuntime
    {
        public bool InitializeCalled { get; private set; }
        public bool ShutdownCalled { get; private set; }

        public Task<KiokuRuntimeStatus> InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InitializeCalled = true;
            return Task.FromResult(new KiokuRuntimeStatus(
                EmbeddingsAvailable: true,
                GenerationConfigured: false,
                GenerationAvailable: false));
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShutdownCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class CancellingRuntime : IKiokuRuntime
    {
        public Task<KiokuRuntimeStatus> InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation was expected.");
        }

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
