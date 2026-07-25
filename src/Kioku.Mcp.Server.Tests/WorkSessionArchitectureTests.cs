using System.Reflection;
using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using McpSessionContextTools = Kioku.Mcp.Server.Tools.SessionContextTools;

namespace Kioku.Mcp.Server.Tests;

public sealed class WorkSessionArchitectureTests
{
    [Fact]
    public void SessionAdapter_DependsOnlyOnApplicationPort()
    {
        var constructors = typeof(McpSessionContextTools).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var constructor = Assert.Single(constructors);
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.True(constructor.IsPublic);
        Assert.Equal(typeof(IWorkSessionService), parameter.ParameterType);
        Assert.True(typeof(IWorkSessionService).IsAssignableFrom(typeof(WorkSessionService)));
    }

    [Fact]
    public void Runtime_RegistersSessionApplicationAndInfrastructurePorts()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddKiokuRuntime(configuration);

        var application = Assert.Single(
            services,
            service => service.ServiceType == typeof(IWorkSessionService));
        var fileSystem = Assert.Single(
            services,
            service => service.ServiceType == typeof(IWorkSessionFileSystem));

        Assert.Equal(ServiceLifetime.Singleton, application.Lifetime);
        Assert.Equal(typeof(WorkSessionService), application.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, fileSystem.Lifetime);
        Assert.Equal(typeof(WorkSessionFileSystem), fileSystem.ImplementationType);
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
        Assert.DoesNotContain("WorkSessionFileSystem", source);
    }

    [Fact]
    public void SessionIntegrationHarness_DoesNotDependOnMcpAdapterOrSdk()
    {
        var source = ReadRepositoryFile("src/Kioku.Mcp.Server.Tests/WorkSessionTestHarness.cs");

        Assert.DoesNotContain("Kioku.Mcp.Server.Tools", source);
        Assert.DoesNotContain("ModelContextProtocol", source);
        Assert.DoesNotContain("McpServer", source);
    }

    [Fact]
    public void SessionAdapter_DoesNotExposeRemovedConsolidatedMethods()
    {
        Assert.Null(typeof(McpSessionContextTools).GetMethod("get_recent_activity"));
        Assert.Null(typeof(McpSessionContextTools).GetMethod("get_session_activity"));
    }

    [Fact]
    public void SessionWorkflow_DoesNotCallSystemIoDirectly()
    {
        var source = string.Concat(
            ReadRepositoryFile("src/Kioku.Mcp.Server/Services/WorkSessionService.cs"),
            ReadRepositoryFile("src/Kioku.Mcp.Server/Services/WorkSessionService.Helpers.cs"));

        Assert.DoesNotContain("File.", source);
        Assert.DoesNotContain("Directory.", source);
    }

    [Fact]
    public void SessionTools_AcceptInjectedCancellationToken()
    {
        var methodNames = new[]
        {
            "get_work_context",
            "start_work_session",
            "end_work_session",
            "list_work_sessions",
        };

        foreach (var methodName in methodNames)
        {
            var method = typeof(McpSessionContextTools).GetMethod(methodName);
            Assert.NotNull(method);
            var parameters = method.GetParameters();
            var cancellation = Assert.Single(
                parameters,
                parameter => parameter.ParameterType == typeof(CancellationToken));
            Assert.Same(parameters[^1], cancellation);
        }
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
