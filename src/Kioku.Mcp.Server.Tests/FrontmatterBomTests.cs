using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class FrontmatterBomTests
{
    [Fact]
    public void ParseAndMutate_BomPrefixedNote_WritesSingleFrontmatterWithoutBom()
    {
        const string content = "\uFEFF---\nstatus: active\ncustom: keep\n---\n# Body\n";

        var document = FrontmatterDocument.Parse(content);
        document.SetString("status", "done");
        var serialized = document.Serialize();

        Assert.DoesNotContain('\uFEFF', serialized);
        Assert.StartsWith("---\n", serialized);
        Assert.Equal(2, CountOccurrences(serialized, "---\n"));
        Assert.Contains("status: done", serialized);
        Assert.Contains("custom: keep", serialized);
        Assert.EndsWith("# Body\n", serialized);
    }

    [Fact]
    public void GetBodyStart_BomPrefixedNote_ReturnsIndexIntoOriginalContent()
    {
        const string content = "\uFEFF---\nstatus: active\n---\n# Body";

        var bodyStart = FrontmatterParser.GetBodyStart(content);

        Assert.Equal("# Body", content[bodyStart..]);
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }
}
