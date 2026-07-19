using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Protocol;

internal static class KiokuTypedResultFilters
{
    private static readonly HashSet<string> MigratedTools = new(StringComparer.Ordinal)
    {
        "read_note",
        "list_notes",
        "search_notes",
        "get_project_context",
        "start_work_session",
        "end_work_session",
        "create_note",
        "edit_note",
        "delete_note",
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
                    if (MigratedTools.Contains(tool.Name))
                    {
                        tool.OutputSchema = OutputSchema;
                    }
                }

                return result;
            });

            filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                var result = await next(context, cancellationToken);
                if (!MigratedTools.Contains(context.Params.Name) || result.StructuredContent is not null)
                {
                    return result;
                }

                var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
                var classified = Classify(text, result.IsError == true);
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

    private static object? ParseData(string text, bool isError)
    {
        if (isError || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new { text };
        }
    }

    private static object? ExtractPagination(object? data)
    {
        if (data is not JsonElement { ValueKind: JsonValueKind.Object } element ||
            !element.TryGetProperty("total", out var total) ||
            !element.TryGetProperty("offset", out var offset) ||
            !element.TryGetProperty("limit", out var limit) ||
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

        if (normalized.StartsWith("[error:NOT_FOUND]", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("not found", StringComparison.OrdinalIgnoreCase))
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

        if (normalized.StartsWith("[loading]", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "INDEX_NOT_READY", StripPrefix(normalized), null);
        }

        if (normalized.StartsWith("[error:DEPENDENCY_UNAVAILABLE]", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "DEPENDENCY_UNAVAILABLE", StripPrefix(normalized), null);
        }

        if (normalized.StartsWith("[error:CONFLICT]", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "CONFLICT", StripPrefix(normalized), null);
        }

        if (normalized.StartsWith("[error:ACCESS_DENIED]", StringComparison.OrdinalIgnoreCase))
        {
            return new(true, "ACCESS_DENIED", StripPrefix(normalized), null);
        }

        if (normalized.StartsWith("[info]", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, string.Empty, string.Empty, StripPrefix(normalized));
        }

        return new(false, string.Empty, string.Empty, null);
    }

    private static string StripPrefix(string value)
    {
        var closingBracket = value.IndexOf(']');
        return closingBracket >= 0 ? value[(closingBracket + 1)..].Trim() : value;
    }

    private sealed record Classification(bool IsError, string Code, string Message, string? Warning);
}
