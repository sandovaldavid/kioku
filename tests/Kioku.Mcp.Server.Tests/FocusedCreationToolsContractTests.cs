using System.Reflection;
using Kioku.Mcp.Server.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class FocusedCreationToolsContractTests
{
    private static readonly string[] ExpectedTools =
    [
        "record_adr",
        "record_bug",
        "create_implementation_plan",
        "save_project_knowledge",
        "add_backlog_item",
        "create_regular_note",
        "create_zettel",
        "create_literature_note",
        "create_moc",
        "create_folder_readme",
    ];

    [Fact]
    public void Focused_tools_are_explicitly_exposed()
    {
        var methods = typeof(FocusedCreationTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(ExpectedTools.OrderBy(name => name), methods);
    }

    [Fact]
    public void Focused_tools_keep_small_intent_specific_schemas()
    {
        var methods = typeof(FocusedCreationTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null);

        foreach (var method in methods)
        {
            Assert.InRange(method.GetParameters().Length, 1, 8);
        }
    }

    [Theory]
    [InlineData("record_adr", "symptom")]
    [InlineData("record_bug", "decision")]
    [InlineData("create_implementation_plan", "author")]
    [InlineData("create_literature_note", "objective")]
    [InlineData("create_regular_note", "year")]
    public void Focused_tools_do_not_leak_unrelated_parameters(string toolName, string forbiddenParameter)
    {
        var method = typeof(FocusedCreationTools).GetMethod(toolName)!;

        Assert.DoesNotContain(
            method.GetParameters(),
            parameter => parameter.Name == forbiddenParameter);
    }
}
