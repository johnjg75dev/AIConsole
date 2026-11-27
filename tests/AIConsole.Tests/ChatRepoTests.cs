using System.IO;
using AIConsole.Data;
using AIConsole.Models;

namespace AIConsole.Tests;

public sealed class ChatRepoTests : IDisposable
{
    private readonly string _path;
    private readonly Db _db;
    private readonly ChatRepo _repo;

    public ChatRepoTests()
    {
        _path = Path.Combine(Path.GetTempPath(), "aiconsole-tests", Guid.NewGuid().ToString("N") + ".db");
        _db = new Db(_path);
        _repo = new ChatRepo(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_path + suffix); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void AddWithAttachments_PersistsMessageAndAttachments()
    {
        var conversation = NewConversation();

        var message = _repo.AddWithAttachments(
            NewMessage(conversation, "hi"),
            new[] { Attachment("a.txt", "text/plain", 1, 2, 3) });

        Assert.True(message.Id > 0);
        var saved = Assert.Single(_repo.Messages(conversation.Id));
        Assert.Equal("hi", saved.Content);

        var attachment = Assert.Single(_repo.Attachments(saved.Id));
        Assert.Equal("a.txt", attachment.Name);
        Assert.Equal("text/plain", attachment.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, attachment.Data);
    }

    [Fact]
    public void AddWithAttachments_RollsBackMessage_WhenAttachmentInsertFails()
    {
        var conversation = NewConversation();
        var invalid = new ChatAttachment("bad.bin", "application/octet-stream", null!);

        Assert.ThrowsAny<Exception>(() => _repo.AddWithAttachments(
            NewMessage(conversation, "x"),
            new[] { Attachment("ok.txt", "text/plain", 1), invalid }));

        Assert.Empty(_repo.Messages(conversation.Id));
        Assert.Equal(0L, Convert.ToInt64(_db.Scalar("SELECT COUNT(*) FROM message_attachments")));
    }

    [Fact]
    public void AttachmentsForMany_GroupsByMessage_AndReturnsEmptyListsForMissing()
    {
        var conversation = NewConversation();
        var first = _repo.AddWithAttachments(NewMessage(conversation, "one"),
            new[] { Attachment("a.txt", "text/plain", 1), Attachment("b.txt", "text/plain", 2) });
        var second = _repo.AddWithAttachments(NewMessage(conversation, "two"),
            new[] { Attachment("c.txt", "text/plain", 3) });
        var third = _repo.Add(NewMessage(conversation, "three"));

        var map = _repo.AttachmentsForMany(new[] { first.Id, second.Id, third.Id, third.Id });

        Assert.Equal(3, map.Count);
        Assert.Equal(new[] { "a.txt", "b.txt" }, map[first.Id].Select(a => a.Name));
        Assert.Equal("c.txt", Assert.Single(map[second.Id]).Name);
        Assert.Empty(map[third.Id]);
    }

    [Fact]
    public void AttachmentsForMany_EmptyInput_ReturnsEmptyDictionary()
    {
        Assert.Empty(_repo.AttachmentsForMany(Array.Empty<long>()));
    }

    [Fact]
    public void UpdateContent_UpdatesMessageAndBumpsConversationTimestamp()
    {
        var oldTimestamp = DateTimeOffset.Now.AddHours(-2);
        var conversation = NewConversation(oldTimestamp);
        var message = _repo.Add(NewMessage(conversation, "before"));
        _db.Run("UPDATE conversations SET updated_at=@u WHERE id=@id",
            ("@u", oldTimestamp.ToString("o")), ("@id", conversation.Id));

        _repo.UpdateContent(message.Id, "after");

        Assert.Equal("after", Assert.Single(_repo.Messages(conversation.Id)).Content);
        Assert.True(Assert.Single(_repo.Conversations()).UpdatedAt > oldTimestamp);
    }

    [Fact]
    public void DeleteConversation_CascadesMessagesAndAttachments()
    {
        var conversation = NewConversation();
        var message = _repo.AddWithAttachments(NewMessage(conversation, "x"),
            new[] { Attachment("a.txt", "text/plain", 9) });

        _repo.DeleteConversation(conversation.Id);

        Assert.Empty(_repo.Messages(conversation.Id));
        Assert.Empty(_repo.Attachments(message.Id));
        Assert.Equal(0L, Convert.ToInt64(_db.Scalar("SELECT COUNT(*) FROM messages")));
        Assert.Equal(0L, Convert.ToInt64(_db.Scalar("SELECT COUNT(*) FROM message_attachments")));
    }

    private Conversation NewConversation(DateTimeOffset? updatedAt = null) =>
        _repo.Create(new Conversation
        {
            Title = "test",
            Model = "endpoint::model",
            UpdatedAt = updatedAt ?? DateTimeOffset.Now,
        });

    private static ChatMessage NewMessage(Conversation conversation, string content) => new()
    {
        ConversationId = conversation.Id,
        Role = ChatRole.User,
        Content = content,
        Participant = "You",
    };

    private static ChatAttachment Attachment(string name, string mediaType, params byte[] data) =>
        new(name, mediaType, data);
}
