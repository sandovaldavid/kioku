using Kioku.Mcp.Server.Services;

namespace Kioku.Mcp.Server.Infrastructure;

internal static class CoordinationInfrastructureExtensions
{
    internal static IServiceCollection AddCoordinationInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ICoordinationFileSystem, CoordinationFileSystem>();
        return services;
    }
}
