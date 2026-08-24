namespace Kioku.Mcp.Server.Hosting;

internal static class KiokuHostConfigurationPolicy
{
    private const string DisableReloadArgument = "--hostBuilder:reloadConfigOnChange=false";

    internal static string[] Apply(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return [.. args, DisableReloadArgument];
    }
}
