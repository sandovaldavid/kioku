using System.Collections;
using System.Globalization;
using System.Text;
using Kioku.Mcp.Server.Domain;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Loss-aware representation of an Obsidian Markdown document with YAML frontmatter.
/// The YAML node graph is retained so fields owned by users and third-party plugins survive
/// mutations of Kioku fields.
/// </summary>
public sealed class FrontmatterDocument
{
    private const string Delimiter = "---";

    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "tags", "tag", "aliases", "alias", "cssclasses", "cssclass",
        "type", "status", "domain", "date", "created", "updated", "modified", "zettel_id",
    };

    private static readonly HashSet<string> AmbiguousPlainScalars = new(StringComparer.OrdinalIgnoreCase)
    {
        "null", "~", "true", "false", "yes", "no", "on", "off",
        ".nan", ".inf", "+.inf", "-.inf",
    };

    private readonly YamlMappingNode _root;

    private FrontmatterDocument(YamlMappingNode root, string body, string newLine, bool hasFrontmatter)
    {
        _root = root;
        Body = body;
        NewLine = newLine;
        HasFrontmatter = hasFrontmatter;
    }

    public string Body { get; private set; }

    public string NewLine { get; }

    public bool HasFrontmatter { get; private set; }

    public static FrontmatterDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var newLine = DetectNewLine(content);
        if (!TryFindFrontmatterBounds(content, out var yamlStart, out var yamlLength, out var bodyStart))
        {
            return new FrontmatterDocument(new YamlMappingNode(), content, newLine, hasFrontmatter: false);
        }

        return new FrontmatterDocument(
            ParseMapping(content.Substring(yamlStart, yamlLength)),
            content[bodyStart..],
            newLine,
            hasFrontmatter: true);
    }

    public static FrontmatterDocument Create(
        NoteFrontmatter frontmatter,
        string body = "",
        string newLine = "\n")
    {
        ArgumentNullException.ThrowIfNull(frontmatter);

        var document = CreateFromFields(frontmatter.ExtraFields, body, newLine);
        document.SetStringList("tags", frontmatter.Tags);
        document.SetStringList("aliases", frontmatter.Aliases);
        document.SetStringList("cssclasses", frontmatter.CssClasses);
        document.SetString("type", frontmatter.NoteType);
        document.SetString("status", frontmatter.Status);
        document.SetString("domain", frontmatter.Domain);
        document.SetDate("date", frontmatter.Date);
        document.SetDate("updated", frontmatter.Updated);
        document.SetString("zettel_id", frontmatter.ZettelId);
        return document;
    }

    internal static FrontmatterDocument CreateFromFields(
        IReadOnlyDictionary<string, object?> fields,
        string body = "",
        string newLine = "\n")
    {
        ArgumentNullException.ThrowIfNull(fields);
        ValidateNewLine(newLine);

        var document = new FrontmatterDocument(new YamlMappingNode(), body, newLine, hasFrontmatter: true);
        document.Merge(fields);
        return document;
    }

    public NoteFrontmatter ToFrontmatter()
    {
        var extras = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in ToFieldDictionary())
        {
            if (!KnownKeys.Contains(key))
            {
                extras[key] = value;
            }
        }

        return new NoteFrontmatter
        {
            Tags = ReadTags(),
            Aliases = ReadStringList("aliases", "alias"),
            CssClasses = ReadStringList("cssclasses", "cssclass"),
            NoteType = ReadString("type"),
            Status = ReadString("status"),
            Domain = ReadString("domain"),
            Date = ReadDate("date", "created"),
            Updated = ReadDate("updated", "modified"),
            ZettelId = ReadString("zettel_id"),
            ExtraFields = extras,
        };
    }

    public NoteMetadata ToNoteMetadata()
    {
        var typed = ToFrontmatter();
        var legacyExtras = typed.ExtraFields.ToDictionary(
            pair => pair.Key,
            pair => ToLegacyString(pair.Value),
            StringComparer.OrdinalIgnoreCase);

        return new NoteMetadata
        {
            Aliases = typed.Aliases,
            Tags = typed.Tags,
            Date = typed.Date,
            Updated = typed.Updated,
            Status = typed.Status,
            NoteType = typed.NoteType,
            Domain = typed.Domain,
            ExtraFields = new PreservedFrontmatterFields(legacyExtras, ToFieldDictionary()),
        };
    }

    public void ReplaceBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Body = body;
    }

    public void SetString(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Remove(key);
            return;
        }

        SetNode(key, CreateStringNode(value));
    }

    public void SetStringList(string key, IEnumerable<string>? values)
    {
        var normalized = values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList() ?? [];

        if (normalized.Count == 0)
        {
            Remove(key);
            return;
        }

        var sequence = new YamlSequenceNode();
        foreach (var value in normalized)
        {
            sequence.Add(CreateStringNode(value));
        }

        SetNode(key, sequence);
    }

    /// <summary>
    /// Sets a date using an existing alias when present, preserving vaults that use
    /// `created`/`modified` instead of `date`/`updated`.
    /// </summary>
    public void SetDate(string preferredKey, DateOnly? value, params string[] aliases)
    {
        if (!value.HasValue)
        {
            Remove(preferredKey);
            foreach (var alias in aliases)
            {
                Remove(alias);
            }
            return;
        }

        var existingKey = FindKeyNode([preferredKey, .. aliases]);
        SetNode(existingKey?.Value ?? preferredKey,
            new YamlScalarNode(value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            {
                Style = ScalarStyle.Plain,
            });
    }

    public void SetValue(string key, object? value)
    {
        if (value is null)
        {
            Remove(key);
            return;
        }

        SetNode(key, ToYamlNode(value));
    }

    public void Merge(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var (key, value) in values)
        {
            SetValue(key, value);
        }
    }

    public bool Remove(string key)
    {
        var keyNode = FindKeyNode([key]);
        return keyNode is not null && _root.Children.Remove(keyNode);
    }

    public string Serialize()
    {
        if (!HasFrontmatter && _root.Children.Count == 0)
        {
            return Body;
        }

        return SerializeFrontmatter() + Body;
    }

    public string SerializeFrontmatter()
    {
        HasFrontmatter = true;
        var yaml = SerializeMapping(_root, NewLine);
        var builder = new StringBuilder();
        builder.Append(Delimiter).Append(NewLine);
        if (!string.IsNullOrEmpty(yaml))
        {
            builder.Append(yaml).Append(NewLine);
        }
        builder.Append(Delimiter).Append(NewLine);
        return builder.ToString();
    }

    internal static int GetBodyStart(string content) =>
        TryFindFrontmatterBounds(content, out _, out _, out var bodyStart) ? bodyStart : 0;

    private IReadOnlyDictionary<string, object?> ToFieldDictionary()
    {
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (keyNode, valueNode) in _root.Children)
        {
            if (keyNode is YamlScalarNode { Value: not null } key)
            {
                fields[key.Value] = ToObject(valueNode);
            }
        }
        return fields;
    }

    private static YamlMappingNode ParseMapping(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return new YamlMappingNode();
        }

        var stream = new YamlStream();
        using var reader = new StringReader(yaml);
        stream.Load(reader);

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is null)
        {
            return new YamlMappingNode();
        }

        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
        {
            throw new InvalidDataException("Obsidian frontmatter must contain a single YAML mapping document.");
        }

        return mapping;
    }

    private static bool TryFindFrontmatterBounds(
        string content,
        out int yamlStart,
        out int yamlLength,
        out int bodyStart)
    {
        yamlStart = 0;
        yamlLength = 0;
        bodyStart = 0;

        var firstLf = content.IndexOf('\n');
        if (firstLf < 0)
        {
            return false;
        }

        var firstLineEnd = firstLf > 0 && content[firstLf - 1] == '\r' ? firstLf - 1 : firstLf;
        if (!content.AsSpan(0, firstLineEnd).TrimEnd().SequenceEqual(Delimiter.AsSpan()))
        {
            return false;
        }

        yamlStart = firstLf + 1;
        var lineStart = yamlStart;
        while (lineStart <= content.Length)
        {
            var lf = content.IndexOf('\n', lineStart);
            var lineEnd = lf >= 0 ? lf : content.Length;
            var lineContentEnd = lineEnd > lineStart && content[lineEnd - 1] == '\r'
                ? lineEnd - 1
                : lineEnd;

            if (content.AsSpan(lineStart, lineContentEnd - lineStart)
                .TrimEnd()
                .SequenceEqual(Delimiter.AsSpan()))
            {
                yamlLength = lineStart - yamlStart;
                bodyStart = lf >= 0 ? lf + 1 : content.Length;
                return true;
            }

            if (lf < 0)
            {
                break;
            }
            lineStart = lf + 1;
        }

        yamlStart = 0;
        return false;
    }

    private IReadOnlyList<string> ReadTags()
    {
        var node = FindValueNode("tags", "tag");
        if (node is YamlSequenceNode sequence)
        {
            return sequence.Children
                .OfType<YamlScalarNode>()
                .Select(item => item.Value?.Trim().TrimStart('#'))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToList();
        }

        var scalar = ReadScalar(node);
        return string.IsNullOrWhiteSpace(scalar)
            ? []
            : scalar.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(tag => tag.TrimStart('#'))
                .Where(tag => tag.Length > 0)
                .ToList();
    }

    private IReadOnlyList<string> ReadStringList(params string[] keys)
    {
        var node = FindValueNode(keys);
        return node switch
        {
            null => [],
            YamlSequenceNode sequence => sequence.Children
                .OfType<YamlScalarNode>()
                .Select(item => item.Value)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToList(),
            _ => string.IsNullOrWhiteSpace(ReadScalar(node)) ? [] : [ReadScalar(node)!],
        };
    }

    private string? ReadString(params string[] keys) => ReadScalar(FindValueNode(keys));

    private DateOnly? ReadDate(params string[] keys)
    {
        var value = ReadString(keys);
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private YamlNode? FindValueNode(params string[] keys)
    {
        var keyNode = FindKeyNode(keys);
        return keyNode is null ? null : _root.Children[keyNode];
    }

    private YamlScalarNode? FindKeyNode(IEnumerable<string> keys)
    {
        var candidates = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _root.Children.Keys
            .OfType<YamlScalarNode>()
            .FirstOrDefault(node => node.Value is not null && candidates.Contains(node.Value));
    }

    private void SetNode(string key, YamlNode value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        HasFrontmatter = true;

        var existing = FindKeyNode([key]);
        if (existing is not null)
        {
            _root.Children[existing] = value;
            return;
        }

        _root.Add(new YamlScalarNode(key), value);
    }

    private static string? ReadScalar(YamlNode? node) =>
        node is YamlScalarNode scalar ? scalar.Value : null;

    private static YamlScalarNode CreateStringNode(string value)
    {
        var style = value.Contains('\n') || value.Contains('\r')
            ? ScalarStyle.Literal
            : CanUsePlainScalar(value) ? ScalarStyle.Plain : ScalarStyle.DoubleQuoted;
        return new YamlScalarNode(value) { Style = style };
    }

    private static bool CanUsePlainScalar(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.Equals(value.Trim(), StringComparison.Ordinal) ||
            AmbiguousPlainScalars.Contains(value))
        {
            return false;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _) ||
            DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return false;
        }

        if (value[0] is '-' or '?' or ':' or '!' or '&' or '*' or '#' or '{' or '}' or '[' or ']' or ',' or '|' or '>' or '@' or '`')
        {
            return false;
        }

        return !value.Contains(':') &&
               !value.Contains('#') &&
               !value.Contains('"') &&
               !value.Contains('\'') &&
               !value.Contains('{') &&
               !value.Contains('}') &&
               !value.Contains('[') &&
               !value.Contains(']');
    }

    private static YamlNode ToYamlNode(object value)
    {
        switch (value)
        {
            case YamlNode node:
                return node;
            case string text:
                return CreateStringNode(text);
            case DateOnly date:
                return new YamlScalarNode(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                {
                    Style = ScalarStyle.Plain,
                };
            case DateTime dateTime:
                return CreateStringNode(dateTime.ToString("O", CultureInfo.InvariantCulture));
            case DateTimeOffset dateTimeOffset:
                return CreateStringNode(dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
            case bool boolean:
                return new YamlScalarNode(boolean ? "true" : "false") { Style = ScalarStyle.Plain };
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                return new YamlScalarNode(Convert.ToString(value, CultureInfo.InvariantCulture))
                {
                    Style = ScalarStyle.Plain,
                };
            case IReadOnlyDictionary<string, object?> readOnlyMapping:
                {
                    var mapping = new YamlMappingNode();
                    foreach (var (key, item) in readOnlyMapping)
                    {
                        mapping.Add(new YamlScalarNode(key), item is null
                            ? new YamlScalarNode("null") { Style = ScalarStyle.Plain }
                            : ToYamlNode(item));
                    }
                    return mapping;
                }
            case IDictionary dictionary:
                {
                    var mapping = new YamlMappingNode();
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (entry.Key is null)
                        {
                            continue;
                        }

                        mapping.Add(
                            new YamlScalarNode(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty),
                            entry.Value is null
                                ? new YamlScalarNode("null") { Style = ScalarStyle.Plain }
                                : ToYamlNode(entry.Value));
                    }
                    return mapping;
                }
            case IEnumerable sequence:
                {
                    var yamlSequence = new YamlSequenceNode();
                    foreach (var item in sequence)
                    {
                        yamlSequence.Add(item is null
                            ? new YamlScalarNode("null") { Style = ScalarStyle.Plain }
                            : ToYamlNode(item));
                    }
                    return yamlSequence;
                }
            default:
                return CreateStringNode(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    private static object? ToObject(YamlNode node) => node switch
    {
        YamlScalarNode scalar => scalar.Value,
        YamlSequenceNode sequence => sequence.Children.Select(ToObject).ToList(),
        YamlMappingNode mapping => mapping.Children
            .Where(pair => pair.Key is YamlScalarNode { Value: not null })
            .ToDictionary(
                pair => ((YamlScalarNode)pair.Key).Value!,
                pair => ToObject(pair.Value),
                StringComparer.OrdinalIgnoreCase),
        _ => node.ToString(),
    };

    private static string ToLegacyString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            IEnumerable<object?> sequence => string.Join(", ", sequence.Select(ToLegacyString)),
            IReadOnlyDictionary<string, object?> mapping => SerializeMapping(
                (YamlMappingNode)ToYamlNode(mapping), "\n"),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static string SerializeMapping(YamlMappingNode mapping, string newLine)
    {
        ValidateNewLine(newLine);
        if (mapping.Children.Count == 0)
        {
            return string.Empty;
        }

        var stream = new YamlStream(new YamlDocument(mapping));
        using var writer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        stream.Save(writer, assignAnchors: false);

        var lines = writer.ToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

        if (lines.Count > 0 && lines[0].Trim().Equals(Delimiter, StringComparison.Ordinal))
        {
            lines.RemoveAt(0);
        }
        while (lines.Count > 0 && string.IsNullOrEmpty(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }
        if (lines.Count > 0 && lines[^1].Trim().Equals("...", StringComparison.Ordinal))
        {
            lines.RemoveAt(lines.Count - 1);
        }
        while (lines.Count > 0 && string.IsNullOrEmpty(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join(newLine, lines);
    }

    private static string DetectNewLine(string content) =>
        content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static void ValidateNewLine(string newLine)
    {
        if (newLine is not "\n" and not "\r\n")
        {
            throw new ArgumentException("Line ending must be LF or CRLF.", nameof(newLine));
        }
    }
}

/// <summary>
/// Compatibility dictionary that exposes scalar strings to existing index consumers while also
/// carrying the complete structured YAML field map for loss-aware rebuilds.
/// </summary>
internal sealed class PreservedFrontmatterFields(
    IReadOnlyDictionary<string, string> legacyValues,
    IReadOnlyDictionary<string, object?> allFields)
    : IReadOnlyDictionary<string, string>
{
    public IReadOnlyDictionary<string, object?> AllFields { get; } = allFields;

    public string this[string key] => legacyValues[key];

    public IEnumerable<string> Keys => legacyValues.Keys;

    public IEnumerable<string> Values => legacyValues.Values;

    public int Count => legacyValues.Count;

    public bool ContainsKey(string key) => legacyValues.ContainsKey(key);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => legacyValues.GetEnumerator();

    public bool TryGetValue(string key, out string value)
    {
        if (legacyValues.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
