using Kioku.Mcp.Server.Services;

namespace Kioku.Mcp.Server.Infrastructure;

internal static class WorkSessionInfrastructureExtensions
{
    internal static IServiceCollection AddWorkSessionInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IWorkSessionFileSystem, WorkSessionFileSystem>();
        return services;
    }
}
