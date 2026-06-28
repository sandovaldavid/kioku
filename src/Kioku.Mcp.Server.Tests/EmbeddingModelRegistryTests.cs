using Kioku.Mcp.Server.Domain;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class EmbeddingModelRegistryTests
{
    [Theory]
    [InlineData("nomic-embed-text", 768)]
    [InlineData("mxbai-embed-large", 1024)]
    [InlineData("all-minilm", 384)]
    [InlineData("bge-m3", 1024)]
    [InlineData("gte-small", 384)]
    [InlineData("jina-embeddings-v2-base-en", 768)]
    public void GetExpectedDimension_ReturnsKnownDimensions(string model, int expected)
    {
        Assert.Equal(expected, EmbeddingModelRegistry.GetExpectedDimension(model));
    }

    [Fact]
    public void GetExpectedDimension_IsCaseInsensitive()
    {
        Assert.Equal(768, EmbeddingModelRegistry.GetExpectedDimension("NOMIC-EMBED-TEXT"));
    }

    [Fact]
    public void GetExpectedDimension_ReturnsDefaultForUnknownModel()
    {
        Assert.Equal(EmbeddingModelRegistry.DefaultDimension, EmbeddingModelRegistry.GetExpectedDimension("unknown-model"));
    }

    [Fact]
    public void GetExpectedDimension_ReturnsDefaultForEmptyOrNull()
    {
        Assert.Equal(EmbeddingModelRegistry.DefaultDimension, EmbeddingModelRegistry.GetExpectedDimension(""));
        Assert.Equal(EmbeddingModelRegistry.DefaultDimension, EmbeddingModelRegistry.GetExpectedDimension("   "));
    }

    [Fact]
    public void IsKnownModel_ReturnsTrueForKnownModels()
    {
        Assert.True(EmbeddingModelRegistry.IsKnownModel("nomic-embed-text"));
    }

    [Fact]
    public void IsKnownModel_ReturnsFalseForUnknownModels()
    {
        Assert.False(EmbeddingModelRegistry.IsKnownModel("totally-fake-model"));
    }
}
