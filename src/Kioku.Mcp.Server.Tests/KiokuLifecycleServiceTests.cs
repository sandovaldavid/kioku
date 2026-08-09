using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class KiokuLifecycleServiceTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

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

        var lifecycle = GetLifecycle(host);
        await lifecycle.ExecuteTask!.WaitAsync(TestTimeout);

        var readiness = host.Services.GetRequiredService<HttpReadinessState>();
        Assert.True(runtime.InitializeCalled);
        Assert.True(readiness.IsReady);
        Assert.Equal(now, readiness.LastUpdatedUtc);

        await host.StopAsync();
        Assert.True(runtime.ShutdownCalled);
    }

    [Fact]
    public async Task Generic_host_start_does_not_wait_for_runtime_initialization()
    {
        var runtime = new BlockingRuntime();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IKiokuRuntime>(runtime);
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<HttpReadinessState>();
        builder.Services.AddHostedService<KiokuLifecycleService>();

        using var host = builder.Build();
        await host.StartAsync().WaitAsync(TestTimeout);
        await runtime.Started.Task.WaitAsync(TestTimeout);

        var lifecycle = GetLifecycle(host);
        var readiness = host.Services.GetRequiredService<HttpReadinessState>();
        Assert.False(lifecycle.ExecuteTask!.IsCompleted);
        Assert.False(readiness.IsReady);

        runtime.Release.TrySetResult(true);
        await lifecycle.ExecuteTask.WaitAsync(TestTimeout);

        Assert.True(readiness.IsReady);
        await host.StopAsync();
        Assert.True(runtime.ShutdownCalled);
    }

    [Fact]
    public async Task Background_initialization_failure_marks_readiness_failed()
    {
        var runtime = new FailingRuntime();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IKiokuRuntime>(runtime);
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<HttpReadinessState>();
        builder.Services.AddHostedService<KiokuLifecycleService>();

        using var host = builder.Build();
        await host.StartAsync().WaitAsync(TestTimeout);

        var lifecycle = GetLifecycle(host);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await lifecycle.ExecuteTask!.WaitAsync(TestTimeout));

        var readiness = host.Services.GetRequiredService<HttpReadinessState>();
        Assert.False(readiness.IsReady);
        Assert.Equal("failed", readiness.GetSnapshot().Index);
    }

    [Fact]
    public async Task Shutdown_cancels_background_initialization_without_marking_index_failed()
    {
        var runtime = new CancellationAwareRuntime();
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IKiokuRuntime>(runtime);
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<HttpReadinessState>();
        builder.Services.AddHostedService<KiokuLifecycleService>();

        using var host = builder.Build();
        await host.StartAsync().WaitAsync(TestTimeout);
        await runtime.Started.Task.WaitAsync(TestTimeout);

        await host.StopAsync().WaitAsync(TestTimeout);

        var readiness = host.Services.GetRequiredService<HttpReadinessState>();
        Assert.True(runtime.InitializationCanceled);
        Assert.True(runtime.ShutdownCalled);
        Assert.False(readiness.IsReady);
        Assert.NotEqual("failed", readiness.GetSnapshot().Index);
    }

    [Fact]
    public void Readiness_uses_the_injected_time_provider()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var readiness = new HttpReadinessState(new FixedTimeProvider(now));

        readiness.MarkIndexReady();

        Assert.Equal(now, readiness.LastUpdatedUtc);
    }

    private static KiokuLifecycleService GetLifecycle(IHost host) =>
        host.Services.GetServices<IHostedService>()
            .OfType<KiokuLifecycleService>()
            .Single();

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

    private sealed class BlockingRuntime : IKiokuRuntime
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ShutdownCalled { get; private set; }

        public async Task<KiokuRuntimeStatus> InitializeAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return new KiokuRuntimeStatus(
                EmbeddingsAvailable: false,
                GenerationConfigured: false,
                GenerationAvailable: false);
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShutdownCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingRuntime : IKiokuRuntime
    {
        public Task<KiokuRuntimeStatus> InitializeAsync(CancellationToken cancellationToken) =>
            Task.FromException<KiokuRuntimeStatus>(new InvalidOperationException("initialization failed"));

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CancellationAwareRuntime : IKiokuRuntime
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool InitializationCanceled { get; private set; }
        public bool ShutdownCalled { get; private set; }

        public async Task<KiokuRuntimeStatus> InitializeAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                InitializationCanceled = true;
                throw;
            }

            throw new InvalidOperationException("Initialization should have been canceled.");
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShutdownCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}