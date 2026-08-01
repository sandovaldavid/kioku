using Kioku.Mcp.Server.Services;

namespace Kioku.Mcp.Server.Infrastructure;

internal static class ProjectDocumentInfrastructureExtensions
{
    internal static IServiceCollection AddProjectDocumentInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IProjectDocumentFileSystem, ProjectDocumentFileSystem>();
        return services;
    }
}
