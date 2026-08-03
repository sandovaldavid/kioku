using Kioku.Mcp.Server.Domain;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class EmbeddingModelRegistryTests
{
    [Theory]
    [InlineData("nomic-embed-text")]
    [InlineData("nomic-embed-text-v1.5")]
    public void GetModelInfo_NomicModels_HaveAsymmetricSearchPrefixes(string model)
    {
        var info = EmbeddingModelRegistry.GetModelInfo(model);

        Assert.Equal("search_document: ", info.DocumentPrefix);
        Assert.Equal("search_query: ", info.QueryPrefix);
    }

    [Fact]
    public void GetModelInfo_MxbaiEmbedLarge_HasQueryPrefixOnly()
    {
        var info = EmbeddingModelRegistry.GetModelInfo("mxbai-embed-large");

        Assert.Null(info.DocumentPrefix);
        Assert.Equal("Represent this sentence for searching relevant passages: ", info.QueryPrefix);
    }

    [Theory]
    [InlineData("bge-m3")]
    [InlineData("all-minilm")]
    [InlineData("unknown-model")]
    public void GetModelInfo_SymmetricOrUnknownModels_HaveNoPrefixes(string model)
    {
        var info = EmbeddingModelRegistry.GetModelInfo(model);

        Assert.Null(info.DocumentPrefix);
        Assert.Null(info.QueryPrefix);
    }
}
