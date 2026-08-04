using Kioku.Mcp.Server.Domain.Coordination;
using Kioku.Mcp.Server.Http;
using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Options;

namespace Kioku.Mcp.Server.Hosting;

internal static class KiokuHostingExtensions
{
    internal static IServiceCollection AddKiokuRuntime(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<KiokuOptions>()
            .Bind(configuration.GetSection(KiokuOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<KiokuOptions>, KiokuOptionsValidator>();
        services.AddSingleton<KiokuConfiguration>(provider =>
            provider.GetRequiredService<IOptions<KiokuOptions>>().Value.ToConfiguration());

        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<HttpReadinessState>();
        services.AddSingleton<VaultPathPolicy>();
        services.AddCoordinationInfrastructure();
        services.AddSingleton<CoordinationContractValidator>();
        services.AddSingleton<ICoordinationEventStore, CoordinationEventStore>();
        services.AddSingleton<ICoordinationClaimStore, CoordinationClaimStore>();
        services.AddSingleton<ICoordinationConflictStore, CoordinationConflictStore>();
        services.AddSingleton<ICoordinationService, CoordinationService>();
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<GenerationService>();
        services.AddSingleton<VaultIndexService>();
        services.AddSingleton<IVaultIndexOperations, VaultIndexOperations>();
        services.AddSingleton<IVaultMutationService, VaultMutationService>();
        services.AddSingleton<VaultIndexingMetrics>();
        services.AddSingleton<VaultIndexingPipeline>();
        services.AddSingleton<ObsidianBridgeService>();
        services.AddSingleton<HybridSearchService>();
        services.AddSingleton<TaskService>();
        services.AddSingleton<MetricsService>();
        services.AddSingleton<ProjectWorkspaceService>();
        services.AddSingleton<VaultConfigService>();
        services.AddSingleton<IWorkSessionFileSystem, WorkSessionFileSystem>();
        services.AddSingleton<IWorkSessionService, WorkSessionService>();
        services.AddSingleton<IProjectDocumentFileSystem, ProjectDocumentFileSystem>();
        services.AddSingleton<IProjectDocumentService, ProjectDocumentService>();
        services.AddSingleton<INoteQueryService, NoteQueryService>();
        services.AddTransient<ZettelkastenTools>();

        services.AddSingleton<IKiokuRuntime, KiokuRuntime>();
        services.AddSingleton<KiokuLifecycleService>();

        // Hosted services start in registration order and stop in reverse order. The lifecycle
        // performs the cold scan first; the pipeline is therefore stopped and drained before the
        // lifecycle persists the embedding cache during shutdown.
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<KiokuLifecycleService>());
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<VaultIndexingPipeline>());

        services.AddHttpClient("ollama")
            .ConfigureHttpClient((provider, client) =>
            {
                var config = provider.GetRequiredService<KiokuConfiguration>();
                client.BaseAddress = new Uri(config.OllamaUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 4,
            });

        services.AddHttpClient("web", client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            });

        return services;
    }
}

internal interface IKiokuRuntime
{
    Task<KiokuRuntimeStatus> InitializeAsync(CancellationToken cancellationToken);
    Task ShutdownAsync(CancellationToken cancellationToken);
}

internal sealed record KiokuRuntimeStatus(
    bool EmbeddingsAvailable,
    bool GenerationConfigured,
    bool GenerationAvailable);

internal sealed class KiokuRuntime(
    VaultIndexingPipeline indexing,
    VaultIndexingMetrics indexingMetrics,
    EmbeddingService embedding,
    GenerationService generation,
    TimeProvider timeProvider) : IKiokuRuntime
{
    public async Task<KiokuRuntimeStatus> InitializeAsync(CancellationToken cancellationToken)
    {
        await indexing.InitializeAsync(cancellationToken);

        var embeddingStartedAt = timeProvider.GetTimestamp();
        await embedding.InitializeAsync(indexing.GetNotesSnapshot(), cancellationToken);
        indexingMetrics.EmbeddingInitializationCompleted(
            timeProvider.GetElapsedTime(embeddingStartedAt));

        await generation.InitializeAsync(cancellationToken);

        return new KiokuRuntimeStatus(
            embedding.IsAvailable,
            !string.IsNullOrWhiteSpace(generation.GenerationModel),
            generation.IsAvailable);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await embedding.SaveAsync(cancellationToken);
    }
}

internal sealed class KiokuLifecycleService(
    IKiokuRuntime runtime,
    HttpReadinessState readiness,
    TimeProvider timeProvider,
    ILogger<KiokuLifecycleService> logger,
    ICoordinationFaultInjector? faultInjector = null) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();
        try
        {
            var status = await runtime.InitializeAsync(cancellationToken);
            readiness.MarkIndexReady();
            readiness.SetOptionalDependencies(
                status.EmbeddingsAvailable,
                status.GenerationConfigured,
                status.GenerationAvailable);
            logger.Info(
                "Kioku runtime initialized in {ElapsedMs:F0} ms.",
                timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            await InjectAsync(CoordinationFaultPoint.ProcessCancellation, CancellationToken.None)
                .ConfigureAwait(false);
            readiness.MarkIndexFailed();
            throw;
        }
        catch
        {
            readiness.MarkIndexFailed();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await InjectAsync(CoordinationFaultPoint.ProcessShutdown, cancellationToken)
            .ConfigureAwait(false);
        logger.Info("Shutting down: flushing embedding cache asynchronously...");
        await runtime.ShutdownAsync(cancellationToken);
        logger.Info("Embedding cache flushed.");
    }

    private Task InjectAsync(
        CoordinationFaultPoint point,
        CancellationToken cancellationToken) =>
        (faultInjector ?? NoOpCoordinationFaultInjector.Instance).InjectAsync(point, cancellationToken);
}
