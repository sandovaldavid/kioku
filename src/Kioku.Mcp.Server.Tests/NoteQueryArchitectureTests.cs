using System.Reflection;
using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using McpNoteQueryTools = Kioku.Mcp.Server.Tools.NoteQueryTools;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Mirrors WorkSessionArchitectureTests/ProjectDocumentArchitectureTests for the note-query
/// slice of #250 (slice 5): unlike those two slices, there is no filesystem infrastructure
/// port here — NoteQueryTools is read-only and its one direct file read stays behind
/// INoteQueryService. The split that matters for this slice is query logic vs. text/JSON
/// presentation, so these tests also guard that the adapter carries no formatting code and
/// that presentation stays out of the query service's own source.
/// </summary>
public sealed class NoteQueryArchitectureTests
{
    [Fact]
    public void QueryAdapter_DependsOnlyOnApplicationPort()
    {
        var constructors = typeof(McpNoteQueryTools).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var constructor = Assert.Single(constructors);
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.True(constructor.IsPublic);
        Assert.Equal(typeof(INoteQueryService), parameter.ParameterType);
        Assert.True(typeof(INoteQueryService).IsAssignableFrom(typeof(NoteQueryService)));
    }

    [Fact]
    public void Runtime_RegistersNoteQueryApplicationPort()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddKiokuRuntime(configuration);

        var application = Assert.Single(
            services,
            service => service.ServiceType == typeof(INoteQueryService));

        Assert.Equal(ServiceLifetime.Singleton, application.Lifetime);
        Assert.Equal(typeof(NoteQueryService), application.ImplementationType);
    }

    [Fact]
    public void QueryAdapter_DoesNotConstructServiceOrDependOnCollaboratorServices()
    {
        var source = ReadRepositoryFile("src/Kioku.Mcp.Server/Tools/NoteQueryTools.cs");

        Assert.DoesNotContain("new NoteQueryService", source);
        Assert.DoesNotContain("VaultIndexService", source);
        Assert.DoesNotContain("KiokuConfiguration", source);
        Assert.DoesNotContain("EmbeddingService", source);
        Assert.DoesNotContain("HybridSearchService", source);
        Assert.DoesNotContain("MetricsService", source);
    }

    [Fact]
    public void QueryAdapter_ContainsNoPresentationOrFileAccessLogic()
    {
        var source = ReadRepositoryFile("src/Kioku.Mcp.Server/Tools/NoteQueryTools.cs");

        Assert.DoesNotContain("JsonSerializer", source);
        Assert.DoesNotContain("ToJson(", source);
        Assert.DoesNotContain("IsJsonFormat(", source);
        Assert.DoesNotContain("RenderMetadata", source);
        Assert.DoesNotContain("RenderSearchResults", source);
        Assert.DoesNotContain("KiokuError.", source);
        Assert.DoesNotContain("File.", source);
    }

    [Fact]
    public void QueryService_DoesNotBuildJsonOrTextResponsesItself()
    {
        var source = ReadRepositoryFile("src/Kioku.Mcp.Server/Services/NoteQueryService.cs");

        // Formatting is delegated to NoteResultPresenter; the service only decides *what*
        // happened (found/not-found, valid/invalid, empty/populated), not how it is rendered.
        Assert.DoesNotContain("JsonSerializer", source);
        Assert.DoesNotContain("JsonSerializerOptions", source);
    }

    [Fact]
    public void QueryTools_AcceptInjectedCancellationTokenOnAsyncOperations()
    {
        var methodNames = new[] { "read_note", "search_notes" };

        foreach (var methodName in methodNames)
        {
            var method = typeof(McpNoteQueryTools).GetMethod(methodName);
            Assert.NotNull(method);
            var parameters = method!.GetParameters();
            var cancellation = Assert.Single(
                parameters,
                parameter => parameter.ParameterType == typeof(CancellationToken));
            Assert.Same(parameters[^1], cancellation);
        }
    }

    [Fact]
    public void QueryTools_ExposesExactlyTheFiveMcpQueryMethods()
    {
        var methodNames = typeof(McpNoteQueryTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToHashSet();

        Assert.Equal(
            new HashSet<string> { "read_note", "list_notes", "search_notes", "get_links", "find_similar_notes" },
            methodNames);
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
