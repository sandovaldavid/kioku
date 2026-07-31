using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Protocol;
using Kioku.Mcp.Server.Resources;
using Kioku.Mcp.Server.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class CoordinationSurfaceContractTests
{
    [Fact]
    public void CoordinationTools_ExposeFocusedOperations()
    {
        var expected = new[]
        {
            "create_coordination_work_item",
            "get_coordination_work_item",
            "list_coordination_work_items",
            "list_coordination_runs",
            "transition_coordination_work_item",
            "acquire_coordination_claim",
            "renew_coordination_claim",
            "release_coordination_claim",
            "expire_coordination_claim",
            "list_coordination_claims",
            "list_coordination_history",
            "get_coordination_handoff",
            "list_coordination_blockers",
            "list_stale_coordination_work",
            "list_failed_coordination_attempts",
            "list_coordination_conflicts",
            "resolve_coordination_conflict",
        };
        var actual = typeof(CoordinationTools)
            .GetMethods()
            .Where(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: true).Length > 0)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
        Assert.NotNull(typeof(CoordinationTools).GetCustomAttributes(typeof(McpServerToolTypeAttribute), inherit: true).SingleOrDefault());
    }

    [Fact]
    public void CoordinationResources_ExposeProjectionHistoryAndHandoffTemplates()
    {
        var templates = typeof(CoordinationResources)
            .GetMethods()
            .SelectMany(method => method.GetCustomAttributes(typeof(McpServerResourceAttribute), inherit: true))
            .Cast<McpServerResourceAttribute>()
            .Select(attribute => attribute.UriTemplate)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "kioku://coordination/handoff/{run_id}/{work_item_id}",
                "kioku://coordination/history/{run_id}/{work_item_id}",
                "kioku://coordination/work/{run_id}/{work_item_id}",
            },
            templates);
    }

    [Fact]
    public void CoordinationCapability_IsOffByDefaultAndExplicitlyOptIn()
    {
        var vault = Path.Combine(Path.GetTempPath(), $"kioku-capability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(vault);
        try
        {
            Assert.False(VaultCapabilityProfile.Load(vault).IsEnabled("coordination"));
            Directory.CreateDirectory(Path.Combine(vault, ".kioku"));
            File.WriteAllText(
                Path.Combine(vault, ".kioku", "config.yml"),
                "capabilities:\n  require_explicit: true\n  enabled:\n    - coordination\n");

            Assert.True(VaultCapabilityProfile.Load(vault).IsEnabled("coordination"));
        }
        finally
        {
            Directory.Delete(vault, recursive: true);
        }
    }

    [Theory]
    [InlineData("get_coordination_work_item")]
    [InlineData("list_coordination_history")]
    [InlineData("get_coordination_handoff")]
    [InlineData("list_coordination_conflicts")]
    public void CoordinationReadTools_AreReadOnlyAndIdempotent(string toolName)
    {
        var annotations = KiokuToolAnnotations.Create(toolName);

        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }
}
