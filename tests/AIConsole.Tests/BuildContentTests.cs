using System.Text;
using System.Text.Json;
using AIConsole.Models;
using AIConsole.ViewModels;

namespace AIConsole.Tests;

public class BuildContentTests
{
    [Fact]
    public void NoAttachments_ReturnsOriginalContent()
    {
        var content = ChatViewModel.BuildContent(Message("hello"), Array.Empty<ChatAttachment>());

        Assert.Equal("hello", content);
    }

    [Fact]
    public void TextOnlyAttachments_AreAppendedToTheText()
    {
        var content = ChatViewModel.BuildContent(Message("hello"),
            new[] { new ChatAttachment("notes.txt", "text/plain", Encoding.UTF8.GetBytes("file body")) });

        var text = Assert.IsType<string>(content);
        Assert.Contains("hello", text);
        Assert.Contains("Attachment: notes.txt", text);
        Assert.Contains("file body", text);
    }

    [Fact]
    public void ImageAttachment_ProducesMultimodalParts()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        var content = ChatViewModel.BuildContent(Message("look"),
            new[] { new ChatAttachment("pic.png", "image/png", png) });

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(content));
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.Equal("text", json.RootElement[0].GetProperty("type").GetString());
        Assert.Equal("look", json.RootElement[0].GetProperty("text").GetString());

        var image = json.RootElement[1];
        Assert.Equal("image_url", image.GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(png),
            image.GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public void ImageOnlyWithEmptyContent_OmitsEmptyTextPart()
    {
        var content = ChatViewModel.BuildContent(Message(""),
            new[] { new ChatAttachment("pic.png", "IMAGE/PNG", new byte[] { 1 }) });

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(content));
        Assert.Equal(1, json.RootElement.GetArrayLength());
        Assert.Equal("image_url", json.RootElement[0].GetProperty("type").GetString());
    }

    [Fact]
    public void MixedAttachments_IncludeTextAndImagePartsInOrder()
    {
        var content = ChatViewModel.BuildContent(Message("x"), new[]
        {
            new ChatAttachment("a.txt", "text/plain", Encoding.UTF8.GetBytes("A")),
            new ChatAttachment("b.png", "image/png", new byte[] { 2 }),
        });

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(content));
        Assert.Equal(3, json.RootElement.GetArrayLength());
        Assert.Equal("text", json.RootElement[0].GetProperty("type").GetString());
        Assert.Equal("text", json.RootElement[1].GetProperty("type").GetString());
        Assert.Contains("Attachment: a.txt", json.RootElement[1].GetProperty("text").GetString());
        Assert.Equal("image_url", json.RootElement[2].GetProperty("type").GetString());
    }

    private static ChatMessage Message(string content) => new()
    {
        ConversationId = "conversation",
        Role = ChatRole.User,
        Content = content,
    };
}
