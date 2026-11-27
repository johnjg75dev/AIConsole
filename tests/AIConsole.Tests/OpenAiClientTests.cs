using AIConsole.Services;

namespace AIConsole.Tests;

public class OpenAiClientTests
{
    [Theory]
    [InlineData("https://api.openai.com", "chat/completions", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/", "models", "https://api.openai.com/v1/models")]
    [InlineData("https://api.openai.com/v1", "chat/completions", "https://api.openai.com/v1/chat/completions")]
    [InlineData("https://api.openai.com/v1/", "models", "https://api.openai.com/v1/models")]
    [InlineData("http://localhost:1234/api", "chat/completions", "http://localhost:1234/api/chat/completions")]
    [InlineData("http://localhost:1234/api/v1", "models", "http://localhost:1234/api/v1/models")]
    [InlineData("https://host/openai/", "systemone", "https://host/openai/systemone")]
    public void CombineUrl_UsesExplicitPathOrAddsV1ForBareHost(string baseUrl, string path, string expected)
    {
        Assert.Equal(expected, OpenAiClient.CombineUrl(baseUrl, path));
    }

    [Theory]
    [InlineData("https://api.openai.com")]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("http://localhost:1234/api")]
    [InlineData("http://localhost:1234/api/v1")]
    public void ModelListCandidates_FirstCandidateMatchesCombineUrl(string baseUrl)
    {
        Assert.Equal(OpenAiClient.CombineUrl(baseUrl, "models"),
            OpenAiClient.ModelListCandidates(baseUrl).First());
    }

    [Fact]
    public void ModelListCandidates_BareHost_TriesV1ThenRoot()
    {
        Assert.Equal(new[] { "https://host/v1/models", "https://host/models" },
            OpenAiClient.ModelListCandidates("https://host").ToArray());
    }

    [Fact]
    public void ModelListCandidates_ExplicitPath_TriesDirectFirstThenV1()
    {
        Assert.Equal(new[] { "https://host/api/models", "https://host/api/v1/models" },
            OpenAiClient.ModelListCandidates("https://host/api").ToArray());
    }

    [Fact]
    public void ModelListCandidates_V1Base_DoesNotDoubleV1()
    {
        Assert.Equal("https://host/v1/models", OpenAiClient.ModelListCandidates("https://host/v1").First());
    }

    [Theory]
    [InlineData("https://api.openai.com", true)]
    [InlineData("https://api.openai.com/", true)]
    [InlineData("https://api.openai.com/v1", false)]
    [InlineData("http://localhost:1234/api", false)]
    [InlineData("http://localhost:1234/api/v1", false)]
    public void NeedsV1Prefix_OnlyForBareHosts(string baseUrl, bool expected)
    {
        Assert.Equal(expected, OpenAiClient.NeedsV1Prefix(baseUrl));
    }
}
