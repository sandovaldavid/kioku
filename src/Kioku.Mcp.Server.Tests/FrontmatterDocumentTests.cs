using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class FrontmatterDocumentTests
{
    [Fact]
    public void Create_RoundTrip_PreservesSpecialValuesAndStructuredFields()
    {
        const string body = "# Título 記憶\n\nBody with `code` and # markdown.\n";
        var source = new NoteFrontmatter
        {
            Tags = ["project", "ai/research"],
            Aliases = ["A: B", "Alias #1"],
            CssClasses = ["wide-page"],
            NoteType = "note",
            Status = "draft",
            Date = new DateOnly(2026, 7, 18),
            ExtraFields = new Dictionary<string, object?>
            {
                ["colon"] = "value: with colon",
                ["hash"] = "value # with hash",
                ["boolean-like"] = "true",
                ["date-like"] = "2026-07-18",
                ["quoted"] = "She said \"hello\"",
                ["multiline"] = "first line\nsecond line",
                ["nested"] = new Dictionary<string, object?>
                {
                    ["owner"] = "human",
                    ["flags"] = new List<object?> { "one", "two" },
                },
            },
        };

        var serialized = FrontmatterDocument.Create(source, body).Serialize();
        var roundTripped = FrontmatterDocument.Parse(serialized);
        var metadata = roundTripped.ToFrontmatter();

        Assert.Equal(body, roundTripped.Body);
        Assert.Equal(source.Tags, metadata.Tags);
        Assert.Equal(source.Aliases, metadata.Aliases);
        Assert.Equal(source.CssClasses, metadata.CssClasses);
        Assert.Equal("value: with colon", metadata.ExtraFields["colon"]);
        Assert.Equal("value # with hash", metadata.ExtraFields["hash"]);
        Assert.Equal("true", metadata.ExtraFields["boolean-like"]);
        Assert.Equal("2026-07-18", metadata.ExtraFields["date-like"]);
        Assert.Equal("first line\nsecond line", metadata.ExtraFields["multiline"]);

        var nested = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(metadata.ExtraFields["nested"]);
        Assert.Equal("human", nested["owner"]);
        Assert.Equal(["one", "two"], Assert.IsAssignableFrom<IEnumerable<object?>>(nested["flags"]));

        Assert.Contains("boolean-like: \"true\"", serialized);
        Assert.Contains("date-like: \"2026-07-18\"", serialized);
        Assert.DoesNotContain('\uFEFF', serialized);
    }

    [Fact]
    public void MutateStatus_PreservesUnknownNestedFieldsAndBody()
    {
        const string content = """
            ---
            tags:
              - session
            cssclasses:
              - kioku-session
            status: active
            custom:
              owner: human
              flags:
                - keep
                - me
            ---
            # Session

            status: active must remain body text.
            """;

        var document = FrontmatterDocument.Parse(content);
        var originalBody = document.Body;

        document.SetString("status", "done");
        var serialized = document.Serialize();
        var parsed = FrontmatterDocument.Parse(serialized);
        var metadata = parsed.ToFrontmatter();

        Assert.Equal(originalBody, parsed.Body);
        Assert.Contains("status: active must remain body text.", parsed.Body);
        Assert.Equal("done", metadata.Status);
        Assert.Equal(["kioku-session"], metadata.CssClasses);

        var custom = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(metadata.ExtraFields["custom"]);
        Assert.Equal("human", custom["owner"]);
        Assert.Equal(["keep", "me"], Assert.IsAssignableFrom<IEnumerable<object?>>(custom["flags"]));
    }

    [Fact]
    public void MetadataCompatibilityRebuild_PreservesStructuredFields()
    {
        const string content = """
            ---
            tags: [project, active]
            aliases: [Project Alpha]
            cssclasses: [dashboard]
            type: project
            status: active
            custom:
              nested:
                - alpha
                - beta
            ---
            # Body
            """;

        var metadata = FrontmatterParser.Parse(content);
        var body = content[FrontmatterParser.GetBodyStart(content)..];
        var frontmatter = NoteHelpers.BuildFrontmatter(
            metadata.Tags,
            metadata.NoteType,
            status: "done",
            date: metadata.Date,
            domain: metadata.Domain,
            extraFields: metadata.ExtraFields,
            aliases: metadata.Aliases,
            updated: metadata.Updated);

        var rebuilt = FrontmatterDocument.Parse(frontmatter + body);
        var typed = rebuilt.ToFrontmatter();

        Assert.Equal(body, rebuilt.Body);
        Assert.Equal("done", typed.Status);
        Assert.Equal(["dashboard"], typed.CssClasses);
        var custom = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(typed.ExtraFields["custom"]);
        Assert.NotNull(custom["nested"]);
    }

    [Fact]
    public void Serialize_PreservesCrLfConventionAndBodyBytes()
    {
        const string content = "---\r\nstatus: active\r\ncustom: value\r\n---\r\n# Body\r\n\r\nLine\r\n";
        var document = FrontmatterDocument.Parse(content);
        var originalBody = document.Body;

        document.SetString("status", "done");
        var serialized = document.Serialize();

        Assert.Equal(originalBody, FrontmatterDocument.Parse(serialized).Body);
        Assert.DoesNotContain("\n", serialized.Replace("\r\n", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain('\uFEFF', serialized);
    }

    [Fact]
    public void TouchUpdated_NoFrontmatter_AddsMinimalYamlWithoutChangingBody()
    {
        const string body = "# Body\n\nText: # still markdown\n";

        var result = NoteHelpers.TouchUpdated(body, new DateOnly(2026, 7, 18), enabled: true);
        var document = FrontmatterDocument.Parse(result);

        Assert.Equal(body, document.Body);
        Assert.Equal(new DateOnly(2026, 7, 18), document.ToFrontmatter().Updated);
    }

    [Fact]
    public void TouchUpdated_InvalidYaml_DoesNotRewriteContent()
    {
        const string malformed = "---\nfield: [unterminated\n---\n# Body\n";

        var result = NoteHelpers.TouchUpdated(malformed, new DateOnly(2026, 7, 18), enabled: true);

        Assert.Equal(malformed, result);
    }

    [Fact]
    public void SetDate_PreservesModifiedAliasInsteadOfAddingUpdated()
    {
        const string content = "---\nmodified: 2020-01-01\n---\nBody";
        var document = FrontmatterDocument.Parse(content);

        document.SetDate("updated", new DateOnly(2026, 7, 18), "modified");
        var serialized = document.Serialize();

        Assert.Contains("modified: 2026-07-18", serialized);
        Assert.DoesNotContain("updated:", serialized);
        Assert.Equal("Body", FrontmatterDocument.Parse(serialized).Body);
    }
}
