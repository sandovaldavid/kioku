using System.ComponentModel;
using System.Reflection;
using System.Text;

// Find the Kioku.Mcp.Server assembly
var cwd = Directory.GetCurrentDirectory();
var searchPaths = new[]
{
    Path.Combine(cwd, "src", "Kioku.Mcp.Server", "bin", "Debug", "net10.0", "linux-x64"),
    Path.Combine(cwd, "src", "Kioku.Mcp.Server", "bin", "Debug", "net10.0"),
    Path.Combine(cwd, "src", "Kioku.Mcp.Server", "bin", "Release", "net10.0", "linux-x64"),
    Path.Combine(cwd, "src", "Kioku.Mcp.Server", "bin", "Release", "net10.0"),
};

string? assemblyPath = null;
foreach (var path in searchPaths)
{
    var candidate = Path.Combine(path, "Kioku.Mcp.Server.dll");
    if (File.Exists(candidate))
    {
        assemblyPath = candidate;
        break;
    }
}

if (assemblyPath == null)
{
    Console.Error.WriteLine("Error: Could not find Kioku.Mcp.Server.dll. Build the server first with:");
    Console.Error.WriteLine("  dotnet build src/Kioku.Mcp.Server/");
    Environment.Exit(1);
}

var assembly = Assembly.LoadFrom(assemblyPath);

var sb = new StringBuilder();
sb.AppendLine("# MCP Tools Reference");
sb.AppendLine();
sb.AppendLine("> Auto-generated documentation of all MCP tools. Do not edit manually.");
sb.AppendLine("> Regenerate with: `dotnet run --project scripts/GenerateCommandsRef`");
sb.AppendLine();
sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
sb.AppendLine();

// Find all tool/prompt/resource classes
var toolClasses = assembly.GetTypes()
    .Where(t => t.GetCustomAttributes().Any(a => a.GetType().Name == "McpServerToolTypeAttribute"))
    .OrderBy(t => t.Name)
    .ToList();

var promptClasses = assembly.GetTypes()
    .Where(t => t.GetCustomAttributes().Any(a => a.GetType().Name == "McpServerPromptTypeAttribute"))
    .OrderBy(t => t.Name)
    .ToList();

var resourceClasses = assembly.GetTypes()
    .Where(t => t.GetCustomAttributes().Any(a => a.GetType().Name == "McpServerResourceTypeAttribute"))
    .OrderBy(t => t.Name)
    .ToList();

sb.AppendLine("## Summary");
sb.AppendLine();
sb.AppendLine($"Total tool classes: **{toolClasses.Count}**");
if (promptClasses.Count > 0)
{
    sb.AppendLine();
    sb.AppendLine($"Total prompt classes: **{promptClasses.Count}**");
}
if (resourceClasses.Count > 0)
{
    sb.AppendLine();
    sb.AppendLine($"Total resource classes: **{resourceClasses.Count}**");
}
sb.AppendLine();

var totalTools = 0;

foreach (var toolClass in toolClasses)
{
    sb.AppendLine($"## {toolClass.Name}");
    sb.AppendLine();

    var methods = toolClass.GetMethods()
        .Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "McpServerToolAttribute"))
        .OrderBy(m => m.Name)
        .ToList();

    foreach (var method in methods)
    {
        totalTools++;
        var descriptionAttr = method.GetCustomAttribute<DescriptionAttribute>();
        var description = descriptionAttr?.Description ?? "No description available.";

        sb.AppendLine($"### `{method.Name}`");
        sb.AppendLine();
        sb.AppendLine(description);
        sb.AppendLine();

        var parameters = method.GetParameters();
        if (parameters.Length > 0)
        {
            sb.AppendLine("**Parameters:**");
            sb.AppendLine();
            sb.AppendLine("| Name | Type | Required | Description |");
            sb.AppendLine("|------|------|----------|-------------|");

            foreach (var param in parameters)
            {
                var paramDescAttr = param.GetCustomAttribute<DescriptionAttribute>();
                var paramDesc = paramDescAttr?.Description ?? "";
                var isRequired = !param.HasDefaultValue;
                var typeName = param.ParameterType.Name;

                sb.AppendLine($"| `{param.Name}` | {typeName} | {(isRequired ? "Yes" : "No")} | {paramDesc} |");
            }
            sb.AppendLine();
        }
    }
}

var totalPrompts = 0;

if (promptClasses.Count > 0)
{
    sb.AppendLine("## Prompts");
    sb.AppendLine();

    foreach (var promptClass in promptClasses)
    {
        var promptMethods = promptClass.GetMethods()
            .Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "McpServerPromptAttribute"))
            .OrderBy(m => m.Name)
            .ToList();

        foreach (var method in promptMethods)
        {
            totalPrompts++;
            var descriptionAttr = method.GetCustomAttribute<DescriptionAttribute>();
            var description = descriptionAttr?.Description ?? "No description available.";

            sb.AppendLine($"### `{method.Name}`");
            sb.AppendLine();
            sb.AppendLine(description);
            sb.AppendLine();

            var parameters = method.GetParameters();
            if (parameters.Length > 0)
            {
                sb.AppendLine("**Arguments:**");
                sb.AppendLine();
                sb.AppendLine("| Name | Type | Required | Description |");
                sb.AppendLine("|------|------|----------|-------------|");

                foreach (var param in parameters)
                {
                    var paramDescAttr = param.GetCustomAttribute<DescriptionAttribute>();
                    var paramDesc = paramDescAttr?.Description ?? "";
                    var isRequired = !param.HasDefaultValue;
                    var typeName = param.ParameterType.Name;

                    sb.AppendLine($"| `{param.Name}` | {typeName} | {(isRequired ? "Yes" : "No")} | {paramDesc} |");
                }
                sb.AppendLine();
            }
        }
    }
}

var totalResources = 0;

if (resourceClasses.Count > 0)
{
    sb.AppendLine("## Resources");
    sb.AppendLine();

    foreach (var resourceClass in resourceClasses)
    {
        var resourceMethods = resourceClass.GetMethods()
            .Where(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "McpServerResourceAttribute"))
            .OrderBy(m => m.Name)
            .ToList();

        foreach (var method in resourceMethods)
        {
            totalResources++;
            var resourceAttr = method.GetCustomAttributes()
                .First(a => a.GetType().Name == "McpServerResourceAttribute");
            var resourceAttrType = resourceAttr.GetType();
            var uriTemplate = resourceAttrType.GetProperty("UriTemplate")?.GetValue(resourceAttr) as string ?? "";
            var mimeType = resourceAttrType.GetProperty("MimeType")?.GetValue(resourceAttr) as string ?? "";

            var descriptionAttr = method.GetCustomAttribute<DescriptionAttribute>();
            var description = descriptionAttr?.Description ?? "No description available.";

            sb.AppendLine($"### `{uriTemplate}`");
            sb.AppendLine();
            sb.AppendLine(description);
            sb.AppendLine();
            sb.AppendLine($"MIME type: `{mimeType}`");
            sb.AppendLine();
        }
    }
}

sb.AppendLine("---");
sb.AppendLine();
sb.AppendLine($"**Total tools:** {totalTools}");
if (totalPrompts > 0)
{
    sb.AppendLine();
    sb.AppendLine($"**Total prompts:** {totalPrompts}");
}
if (totalResources > 0)
{
    sb.AppendLine();
    sb.AppendLine($"**Total resources:** {totalResources}");
}

var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "docs", "commands-reference.md");
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, sb.ToString());

Console.WriteLine($"Generated {outputPath}");
Console.WriteLine($"  {totalTools} tools from {toolClasses.Count} classes");
if (totalPrompts > 0)
{
    Console.WriteLine($"  {totalPrompts} prompts from {promptClasses.Count} classes");
}
if (totalResources > 0)
{
    Console.WriteLine($"  {totalResources} resources from {resourceClasses.Count} classes");
}
