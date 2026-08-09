using System.Text.Json;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Protocol;

internal static class KiokuTypedResultFilters
{
    private static readonly HashSet<string> WarmupSafeTools = new(StringComparer.Ordinal)
    {
        "get_server_capabilities",
        "get_server_status",
        "list_projects",
        "get_project_context",
    };

    private static readonly JsonElement OutputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            success = new { type = "boolean" },
            data = new { },
            error = new
            {
                type = new[] { "object", "null" },
                properties = new
                {
                    code = new { type = "string" },
                    message = new { type = "string" },
                },
                required = new[] { "code", "message" },
                additionalProperties = false,
            },
            pagination = new
            {
                type = new[] { "object", "null" },
                properties = new
                {
                    total = new { type = "integer", minimum = 0 },
                    offset = new { type = "integer", minimum = 0 },
                    limit = new { type = "integer", minimum = 1 },
                    has_more = new { type = "boolean" },
                },
                required = new[] { "total", "offset", "limit", "has_more" },
                additionalProperties = false,
            },
            warnings = new
            {
                type = "array",
                items = new { type = "string" },
            },
        },
        required = new[] { "success", "data", "error", "pagination", "warnings" },
        additionalProperties = false,
    });

    internal static IMcpServerBuilder WithKiokuTypedResults(this IMcpServerBuilder builder) =>
        builder.WithRequestFilters(filters =>
        {
            filters.AddListToolsFilter(next => async (context, cancellationToken) =>
            {
                var result = await next(context, cancellationToken);
                foreach (var tool in result.Tools)
                {
                    tool.Annotations = KiokuToolAnnotations.Create(tool.Name);
                    tool.OutputSchema = OutputSchema;
                }

                return result;
            });

            filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                if (RequiresReadyIndex(context.Params.Name))
                {
                    var services = context.Services
                        ?? throw new InvalidOperationException(
                            "MCP request services are unavailable while enforcing vault index readiness.");
                    await services
                        .GetRequiredService<VaultIndexReadinessGate>()
                        .WaitAsync(cancellationToken);
                }

                var result = await next(context, cancellationToken);
                if (result.StructuredContent is JsonElement { ValueKind: JsonValueKind.Object })
                {
                    return result;
                }

                var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
                var classified = context.Params.Name == "get_server_capabilities" && IsJsonObject(text)
                    ? new Classification(false, string.Empty, string.Empty, null)
                    : Classify(text, result.IsError == true);
                var data = ParseData(text, classified.IsError);
                var pagination = ExtractPagination(data);
                var envelope = new
                {
                    success = !classified.IsError,
                    data,
                    error = classified.IsError ? new { code = classified.Code, message = classified.Message } : null,
                    pagination,
                    warnings = classified.Warning is null ? Array.Empty<string>() : new[] { classified.Warning },
                };

                result.StructuredContent = JsonSerializer.SerializeToElement(envelope);
                result.IsError = classified.IsError;
                return result;
            });
        });

    internal static bool RequiresReadyIndex(string toolName) =>
        !WarmupSafeTools.Contains(toolName);

    private static JsonElement ParseData(string text, bool isError)
    {
        if (isError || string.IsNullOrWhiteSpace(text))
        {
            return JsonSerializer.SerializeToElement<object?>(null);
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { text });
        }
    }

    private static object? ExtractPagination(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("total", out var total) ||
            !data.TryGetProperty("offset", out var offset) ||
            !data.TryGetProperty("limit", out var limit) ||
            !total.TryGetInt32(out var totalValue) ||
            !offset.TryGetInt32(out var offsetValue) ||
            !limit.TryGetInt32(out var limitValue))
        {
            return null;
        }

        return new
        {
            total = totalValue,
            offset = offsetValue,
            limit = limitValue,
            has_more = offsetValue + limitValue < totalValue,
        };
    }

    private static Classification Classify(string text, bool explicitError)
    {
        var normalized = text.Trim();
        if (explicitError)
        {
            return new(true, "INTERNAL_ERROR", normalized, null);
        }

        if (normalized.StartsWith("[info]", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, string.Empty, string.Empty, StripPrefix(normalized));
        }

        if (normalized.StartsWith("[ok]", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, string.Empty, string.Empty, null);
        }

        if (normalized.StartsWith("[loading]", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "INDEX_NOT_READY", StripPrefix(normalized), null);
        }

        if (normalized.StartsWith("[error:NOT_FOUND]", StringComparison.OrdinalIgnoreCase) ||
            (normalized.StartsWith("[error]", StringComparison.OrdinalIgnoreCase) && normalized.Contains("not found", StringComparison.OrdinalIgnoreCase)) ||
            normalized.StartsWith("Note not found", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Template not found", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Session note not found", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("The requested BibTeX source file was not found", StringComparison.OrdinalIgnoreCase) ||
            (normalized.StartsWith("Project '", StringComparison.OrdinalIgnoreCase) && normalized.Contains("' not found", StringComparison.OrdinalIgnoreCase)))
        {
            return new(true, "NOT_FOUND", StripPrefix(normalized), null);
        }

        if (normalized.StartsWith("[error:INVALID_ARGUMENT]", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("[error]", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "INVALID_ARGUMENT", StripPrefix(normalized), null);
        }

        if (normalized.StartsWith("[error:AMBIGUOUS", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "AMBIGUOUS_REFERENCE", StripPrefix(normalized), null);
        }

        if (normalized.StartsWith("[error:DEPENDENCY_UNAVAILABLE]", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "DEPENDENCY_UNAVAILABLE", StripPrefix(normalized), null);
        }

        if (normalized.StartsWith("[error:CONFLICT]", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "CONFLICT", StripPrefix(normalized), null);
        }

        if (normalized.StartsWith("[error:WRITE_CONFLICT]", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "WRITE_CONFLICT", StripPrefix(normalized), null);
        }

        if (normalized.StartsWith("[error:ACCESS_DENIED]", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "ACCESS_DENIED", StripPrefix(normalized), null);
        }

        if (normalized.StartsWith("[error:", StringComparison.OrdinalIgnoreCase))
        {
            var closingBracket = normalized.IndexOf(']');
            if (closingBracket > "[error:".Length)
            {
                var code = normalized["[error:".Length..closingBracket].ToUpperInvariant();
                return new(true, code, StripPrefix(normalized), null);
            }
        }

        return new(false, string.Empty, string.Empty, null);
    }

    private static string StripPrefix(string value)
    {
        var closingBracket = value.IndexOf(']');
        return closingBracket >= 0 ? value[(closingBracket + 1)..].Trim() : value;
    }

    private static bool IsJsonObject(string value)
    {
        if (!value.StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record Classification(bool IsError, string Code, string Message, string? Warning);
}