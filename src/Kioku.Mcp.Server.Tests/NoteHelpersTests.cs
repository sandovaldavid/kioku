using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class NoteHelpersTests
{
    [Fact]
    public void BuildFilePath_SimpleName_AddsMdExtension()
    {
        var result = NoteHelpers.BuildFilePath("My Note", "/vault");

        Assert.Equal(Path.Combine("/vault", "My Note.md"), result);
    }

    [Fact]
    public void BuildFilePath_WithExtension_DoesNotDuplicate()
    {
        var result = NoteHelpers.BuildFilePath("My Note.md", "/vault");

        Assert.Equal(Path.Combine("/vault", "My Note.md"), result);
    }

    [Fact]
    public void BuildFilePath_WithSubfolder_HandlesCorrectly()
    {
        var result = NoteHelpers.BuildFilePath("Projects/My Note", "/vault");

        Assert.Equal(Path.Combine("/vault", "Projects", "My Note.md"), result);
    }

    [Fact]
    public void BuildFilePath_ForwardSlash_NormalizesToSeparator()
    {
        var result = NoteHelpers.BuildFilePath("Projects/Sub/Note", "/vault");

        Assert.Contains("Projects", result);
        Assert.Contains("Sub", result);
        Assert.Contains("Note.md", result);
    }

    [Fact]
    public void BuildFilePath_Backslash_NormalizesToSeparator()
    {
        var result = NoteHelpers.BuildFilePath(@"Projects\Sub\Note", "/vault");

        Assert.Contains("Projects", result);
        Assert.Contains("Sub", result);
        Assert.Contains("Note.md", result);
    }

    [Fact]
    public void ParseTags_EmptyString_ReturnsEmpty()
    {
        var result = NoteHelpers.ParseTags("");

        Assert.Empty(result);
    }

    [Fact]
    public void ParseTags_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(NoteHelpers.ParseTags(null!));
        Assert.Empty(NoteHelpers.ParseTags("   "));
    }

    [Fact]
    public void ParseTags_SingleTag_ReturnsSingle()
    {
        var result = NoteHelpers.ParseTags("project");

        Assert.Single(result);
        Assert.Equal("project", result[0]);
    }

    [Fact]
    public void ParseTags_MultipleCommaSeparated_TrimsAndSplits()
    {
        var result = NoteHelpers.ParseTags("project, ai, research");

        Assert.Equal(3, result.Count);
        Assert.Equal("project", result[0]);
        Assert.Equal("ai", result[1]);
        Assert.Equal("research", result[2]);
    }

    [Fact]
    public void ParseTags_ExtraSpaces_TrimsCorrectly()
    {
        var result = NoteHelpers.ParseTags("  project ,  ai  , research  ");

        Assert.Equal(3, result.Count);
        Assert.Equal("project", result[0]);
        Assert.Equal("ai", result[1]);
        Assert.Equal("research", result[2]);
    }

    [Fact]
    public void SanitizeFileName_RemovesInvalidChars()
    {
        var result = NoteHelpers.SanitizeFileName("My<Note>File");

        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
    }

    [Fact]
    public void SanitizeFileName_ReplacesSpacesWithHyphens()
    {
        var result = NoteHelpers.SanitizeFileName("My Note File");

        Assert.Equal("My-Note-File", result);
    }

    [Fact]
    public void SanitizeFileName_TrimsHyphens()
    {
        var result = NoteHelpers.SanitizeFileName(" -My Note- ");

        Assert.Equal("My-Note", result);
    }

    [Fact]
    public void MergeTagsWithInheritance_NoDuplicates()
    {
        var userTags = new[] { "project", "ai" };
        var inheritedTags = new[] { "ai", "research" };

        var result = NoteHelpers.MergeTagsWithInheritance(userTags, inheritedTags);

        Assert.Equal(3, result.Count);
        Assert.Equal("project", result[0]);
        Assert.Equal("ai", result[1]);
        Assert.Equal("research", result[2]);
    }

    [Fact]
    public void MergeTagsWithInheritance_UserTagsFirst()
    {
        var userTags = new[] { "user-tag" };
        var inheritedTags = new[] { "inherited-tag" };

        var result = NoteHelpers.MergeTagsWithInheritance(userTags, inheritedTags);

        Assert.Equal("user-tag", result[0]);
        Assert.Equal("inherited-tag", result[1]);
    }

    [Fact]
    public void MergeTagsWithInheritance_CaseInsensitiveDedup()
    {
        var userTags = new[] { "Project" };
        var inheritedTags = new[] { "project" };

        var result = NoteHelpers.MergeTagsWithInheritance(userTags, inheritedTags);

        Assert.Single(result);
        Assert.Equal("Project", result[0]);
    }

    [Fact]
    public void MergeTagsWithInheritance_ExcludesFields()
    {
        var userTags = new[] { "project" };
        var inheritedTags = new[] { "research" };
        var excluded = new[] { "research" };

        var result = NoteHelpers.MergeTagsWithInheritance(userTags, inheritedTags, excluded);

        Assert.Single(result);
        Assert.Equal("project", result[0]);
    }

    [Fact]
    public void MergeTagsWithInheritance_SkipsEmptyTags()
    {
        var userTags = new[] { "project", "", "  " };
        var inheritedTags = new[] { "research" };

        var result = NoteHelpers.MergeTagsWithInheritance(userTags, inheritedTags);

        Assert.Equal(2, result.Count);
        Assert.Equal("project", result[0]);
        Assert.Equal("research", result[1]);
    }

    [Fact]
    public void ExpandTemplateVariables_ReplacesKnownVariables()
    {
        var template = "Hello {{name}}, today is {{date}}.";
        var variables = new Dictionary<string, string>
        {
            ["name"] = "World",
            ["date"] = "2024-01-01",
        };

        var result = NoteHelpers.ExpandTemplateVariables(template, variables);

        Assert.Equal("Hello World, today is 2024-01-01.", result);
    }

    [Fact]
    public void ExpandTemplateVariables_LeavesUnknownVariablesIntact()
    {
        var template = "Hello {{name}}, your {{role}} is active.";
        var variables = new Dictionary<string, string>
        {
            ["name"] = "World",
        };

        var result = NoteHelpers.ExpandTemplateVariables(template, variables);

        Assert.Equal("Hello World, your {{role}} is active.", result);
    }

    [Fact]
    public void ExpandTemplateVariables_CaseInsensitive()
    {
        var template = "Hello {{Name}}.";
        var variables = new Dictionary<string, string>
        {
            ["name"] = "World",
        };

        var result = NoteHelpers.ExpandTemplateVariables(template, variables);

        Assert.Equal("Hello World.", result);
    }

    [Fact]
    public void ExpandTemplateVariables_EmptyValue_ReplacesWithEmpty()
    {
        var template = "Hello {{name}}!";
        var variables = new Dictionary<string, string>
        {
            ["name"] = "",
        };

        var result = NoteHelpers.ExpandTemplateVariables(template, variables);

        Assert.Equal("Hello !", result);
    }

    [Fact]
    public void ExpandTemplateVariables_BuiltinDateTime_ReplacesCorrectly()
    {
        var now = new DateTimeOffset(2026, 6, 27, 15, 30, 45, TimeSpan.Zero);

        var result = NoteHelpers.ExpandTemplateVariables(
            "{{date}} {{time}} {{datetime}} {{year}}-{{month}}-{{day}}",
            new Dictionary<string, string>(),
            now: now);

        Assert.Equal("2026-06-27 15:30:45 2026-06-27 15:30:45 2026-06-27", result);
    }

    [Fact]
    public void ExpandTemplateVariables_BuiltinTitle_ReplacesWhenProvided()
    {
        var result = NoteHelpers.ExpandTemplateVariables(
            "{{title}}",
            new Dictionary<string, string>(),
            noteTitle: "My Note");

        Assert.Equal("My Note", result);
    }

    [Fact]
    public void ExpandTemplateVariables_BuiltinUid_GeneratesUniqueValues()
    {
        var result1 = NoteHelpers.ExpandTemplateVariables("{{uid}}", new Dictionary<string, string>());
        var result2 = NoteHelpers.ExpandTemplateVariables("{{uid}}", new Dictionary<string, string>());

        Assert.NotEqual(result1, result2);
        Assert.Equal(32, result1.Length);
    }

    [Fact]
    public void ExpandTemplateVariables_UserVariablesTakePrecedenceOverBuiltins()
    {
        var result = NoteHelpers.ExpandTemplateVariables(
            "{{date}}",
            new Dictionary<string, string> { ["date"] = "custom-date" });

        Assert.Equal("custom-date", result);
    }

    [Fact]
    public void BuildFrontmatter_WithTags_GeneratesYamlList()
    {
        var tags = new[] { "project", "ai" };

        var result = NoteHelpers.BuildFrontmatter(tags);

        Assert.Contains("---", result);
        Assert.Contains("tags:", result);
        Assert.Contains("  - project", result);
        Assert.Contains("  - ai", result);
    }

    [Fact]
    public void BuildFrontmatter_WithAllFields_GeneratesComplete()
    {
        var tags = new[] { "project" };

        var result = NoteHelpers.BuildFrontmatter(
            tags,
            type: "note",
            status: "draft",
            date: new DateOnly(2024, 1, 15),
            zettelId: "20240115120000",
            domain: "tech");

        Assert.Contains("tags:", result);
        Assert.Contains("type: note", result);
        Assert.Contains("status: draft", result);
        Assert.Contains("date: 2024-01-15", result);
        Assert.Contains("zettel_id: \"20240115120000\"", result);
        Assert.Contains("domain: tech", result);
    }

    [Fact]
    public void BuildFrontmatter_NoTags_OmitsTagsSection()
    {
        var result = NoteHelpers.BuildFrontmatter([]);

        Assert.DoesNotContain("tags:", result);
    }

    [Fact]
    public void BuildFrontmatter_WithExtraFields_IncludesThem()
    {
        var extra = new Dictionary<string, string>
        {
            ["citekey"] = "smith2024",
            ["rating"] = "5",
        };

        var result = NoteHelpers.BuildFrontmatter([], extraFields: extra);

        Assert.Contains("citekey: smith2024", result);
        Assert.Contains("rating: 5", result);
    }

    [Fact]
    public void EnsureInsideVault_ValidPath_ReturnsCanonicalPath()
    {
        var vaultRoot = Path.GetTempPath();
        var candidate = Path.Combine(vaultRoot, "subfolder", "note.md");

        var result = NoteHelpers.EnsureInsideVault(vaultRoot, candidate);

        Assert.Equal(Path.GetFullPath(candidate), result);
    }

    [Fact]
    public void EnsureInsideVault_RootPath_ReturnsCanonicalPath()
    {
        var vaultRoot = Path.GetTempPath();

        var result = NoteHelpers.EnsureInsideVault(vaultRoot, vaultRoot);

        Assert.Equal(Path.GetFullPath(vaultRoot), result);
    }

    [Fact]
    public void EnsureInsideVault_PathTraversal_Throws()
    {
        var vaultRoot = Path.GetTempPath();
        var candidate = Path.Combine(vaultRoot, "..", "evil", "note.md");

        var ex = Assert.Throws<InvalidOperationException>(
            () => NoteHelpers.EnsureInsideVault(vaultRoot, candidate));

        Assert.Contains("escapes the vault", ex.Message);
    }

    [Fact]
    public void EnsureInsideVault_AbsolutePathOutside_Throws()
    {
        var vaultRoot = Path.GetTempPath();

        var ex = Assert.Throws<InvalidOperationException>(
            () => NoteHelpers.EnsureInsideVault(vaultRoot, "/etc/passwd"));

        Assert.Contains("escapes the vault", ex.Message);
    }

    [Fact]
    public void EnsureInsideVault_DoubleDotTraversal_Throws()
    {
        var vaultRoot = Path.GetTempPath();
        var candidate = Path.Combine(vaultRoot, "sub", "..", "..", "outside.md");

        Assert.Throws<InvalidOperationException>(
            () => NoteHelpers.EnsureInsideVault(vaultRoot, candidate));
    }

    [Fact]
    public void BuildFilePath_PathTraversal_Throws()
    {
        var vaultRoot = Path.GetTempPath();

        Assert.Throws<InvalidOperationException>(
            () => NoteHelpers.BuildFilePath("../../evil/note", vaultRoot));
    }

    [Fact]
    public void BuildFilePath_AbsolutePathOutside_Throws()
    {
        var vaultRoot = Path.GetTempPath();

        Assert.Throws<InvalidOperationException>(
            () => NoteHelpers.BuildFilePath("/etc/passwd", vaultRoot));
    }
}
