using Kioku.Mcp.Server.Protocol;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class KiokuToolAnnotationsTests
{
    [Theory]
    [InlineData("read_note")]
    [InlineData("search_notes")]
    [InlineData("get_project_context")]
    [InlineData("get_vault_snapshot")]
    public void Read_tools_are_closed_world_and_non_destructive(string toolName)
    {
        var annotations = KiokuToolAnnotations.Create(toolName);

        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    [Fact]
    public void Delete_note_is_destructive_and_not_retry_safe()
    {
        var annotations = KiokuToolAnnotations.Create("delete_note");

        Assert.False(annotations.ReadOnlyHint);
        Assert.True(annotations.DestructiveHint);
        Assert.False(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    [Fact]
    public void Create_note_is_additive_but_not_read_only()
    {
        var annotations = KiokuToolAnnotations.Create("create_note");

        Assert.False(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.False(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    [Theory]
    [InlineData("summarize_note")]
    [InlineData("query_dataview")]
    [InlineData("trigger_obsidian_command")]
    public void Dependency_or_bridge_tools_are_open_world(string toolName)
    {
        Assert.True(KiokuToolAnnotations.Create(toolName).OpenWorldHint);
    }

    [Fact]
    public void Every_tool_gets_an_explicit_complete_annotation_object()
    {
        var annotations = KiokuToolAnnotations.Create("future_tool");

        Assert.Equal("Future Tool", annotations.Title);
        Assert.False(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.False(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }
}
