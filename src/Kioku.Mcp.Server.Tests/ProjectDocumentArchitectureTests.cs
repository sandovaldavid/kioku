using System.Reflection;
using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using McpEngineeringWorkflowTools = Kioku.Mcp.Server.Tools.EngineeringWorkflowTools;

namespace Kioku.Mcp.Server.Tests;

public sealed class ProjectDocumentArchitectureTests
{
    [Fact]
    public void ProjectDocumentAdapter_DependsOnlyOnApplicationPort()
    {
        var constructors = typeof(McpEngineeringWorkflowTools).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var constructor = Assert.Single(constructors);
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.True(constructor.IsPublic);
        Assert.Equal(typeof(IProjectDocumentService), parameter.ParameterType);
        Assert.True(typeof(IProjectDocumentService).IsAssignableFrom(typeof(ProjectDocumentService)));
    }

    [Fact]
    public void Runtime_RegistersProjectDocumentApplicationAndInfrastructurePorts()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddKiokuRuntime(configuration);

        var application = Assert.Single(
            services,
            service => service.ServiceType == typeof(IProjectDocumentService));
        var fileSystem = Assert.Single(
            services,
            service => service.ServiceType == typeof(IProjectDocumentFileSystem));

        Assert.Equal(ServiceLifetime.Singleton, application.Lifetime);
        Assert.Equal(typeof(ProjectDocumentService), application.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, fileSystem.Lifetime);
        Assert.Equal(typeof(ProjectDocumentFileSystem), fileSystem.ImplementationType);
    }

    [Fact]
    public void ProjectDocumentAdapter_DoesNotConstructWorkflowOrDependOnInfrastructureServices()
    {
        var source = ReadRepositoryFile("src/Kioku.Mcp.Server/Tools/EngineeringWorkflowTools.cs");

        Assert.DoesNotContain("new ProjectDocumentService", source);
        Assert.DoesNotContain("VaultIndexService", source);
        Assert.DoesNotContain("KiokuConfiguration", source);
        Assert.DoesNotContain("VaultConfigService", source);
        Assert.DoesNotContain("ProjectWorkspaceService", source);
        Assert.DoesNotContain("ObsidianBridgeService", source);
        Assert.DoesNotContain("ProjectDocumentFileSystem", source);
    }

    [Fact]
    public void ProjectDocumentWorkflow_DoesNotCallSystemIoDirectly()
    {
        var source = ReadRepositoryFile("src/Kioku.Mcp.Server/Services/ProjectDocumentService.cs");

        Assert.DoesNotContain("File.", source);
        Assert.DoesNotContain("Directory.", source);
    }

    [Fact]
    public void ProjectDocumentTools_AcceptInjectedCancellationToken()
    {
        var methodNames = new[]
        {
            "create_project_doc",
            "record_adr",
            "log_bug",
            "create_plan",
            "add_knowledge",
            "add_backlog_item",
            "get_project_context",
            "list_projects",
            "list_engineering_templates",
            "get_engineering_template",
            "set_engineering_template",
            "setup_agent_workflow",
        };

        foreach (var methodName in methodNames)
        {
            var method = typeof(McpEngineeringWorkflowTools).GetMethod(methodName);
            Assert.NotNull(method);
            var parameters = method!.GetParameters();
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
