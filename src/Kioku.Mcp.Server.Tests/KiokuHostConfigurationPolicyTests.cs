using Kioku.Mcp.Server.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class KiokuHostConfigurationPolicyTests
{
    [Fact]
    public void Apply_preserves_application_arguments_and_disables_configuration_reload()
    {
        string[] args = ["--Kioku:MaxSearchResults=42"];

        var hostArgs = KiokuHostConfigurationPolicy.Apply(args);
        var builder = Host.CreateApplicationBuilder(hostArgs);

        Assert.Equal("42", builder.Configuration["Kioku:MaxSearchResults"]);
        Assert.False(builder.Configuration.GetValue<bool>("hostBuilder:reloadConfigOnChange"));
        Assert.Equal(args, hostArgs[..args.Length]);
    }

    [Fact]
    public void Apply_overrides_an_attempt_to_enable_configuration_reload()
    {
        string[] args = ["--hostBuilder:reloadConfigOnChange=true"];

        var hostArgs = KiokuHostConfigurationPolicy.Apply(args);
        var builder = Host.CreateApplicationBuilder(hostArgs);

        Assert.False(builder.Configuration.GetValue<bool>("hostBuilder:reloadConfigOnChange"));
        Assert.Equal("--hostBuilder:reloadConfigOnChange=false", hostArgs[^1]);
    }
}
