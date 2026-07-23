using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class WorkSessionArchitectureTests
{
    [Fact]
    public void SessionAdapter_DependsOnlyOnApplicationPort()
    {
        var constructor = Assert.Single(typeof(SessionContextTools).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(typeof(IWorkSessionService), parameter.ParameterType);
        Assert.True(typeof(IWorkSessionService).IsAssignableFrom(typeof(WorkSessionService)));
    }

    [Fact]
    public void Runtime_RegistersApplicationPortToConcreteService()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddKiokuRuntime(configuration);

        var descriptor = Assert.Single(
            services.Where(service => service.ServiceType == typeof(IWorkSessionService)));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(WorkSessionService), descriptor.ImplementationType);
    }

    [Fact]
    public void SessionAdapter_DoesNotConstructWorkflowOrDependOnInfrastructureServices()
    {
        var source = ReadRepositoryFile("src/Kioku.Mcp.Server/Tools/SessionContextTools.cs");

        Assert.DoesNotContain("new WorkSessionService", source);
        Assert.DoesNotContain("VaultIndexService", source);
        Assert.DoesNotContain("KiokuConfiguration", source);
        Assert.DoesNotContain("VaultConfigService", source);
        Assert.DoesNotContain("ProjectWorkspaceService", source);
        Assert.DoesNotContain("ObsidianBridgeService", source);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Kioku.slnx")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            throw new InvalidOperationException("Could not locate the Kioku repository root.");
        }

        return File.ReadAllText(Path.Combine(current.FullName, relativePath));
    }
}
