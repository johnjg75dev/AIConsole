using AIConsole.Models;

namespace AIConsole.Data;

public sealed class ChatRepo
{
    private readonly Db _db;
    public ChatRepo(Db db) => _db = db;

    private static Conversation MapConv(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        Title = r.GetString(1),
        Model = r.IsDBNull(2) ? null : r.GetString(2),
        CreatedAt = DateTimeOffset.Parse(r.GetString(3)),
        UpdatedAt = DateTimeOffset.Parse(r.GetString(4)),
    };

    private static ChatMessage MapMsg(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        ConversationId = r.GetString(1),
        Role = Enum.Parse<ChatRole>(r.GetString(2)),
        Content = r.GetString(3),
        ModelId = r.IsDBNull(4) ? null : r.GetString(4),
        Participant = r.IsDBNull(5) ? null : r.GetString(5),
        Timestamp = DateTimeOffset.Parse(r.GetString(6)),
    };

    public List<Conversation> Conversations() =>
        _db.Query("SELECT id,title,model,created_at,updated_at FROM conversations ORDER BY updated_at DESC", MapConv);

    public Conversation Create(Conversation c)
    {
        _db.Run("INSERT INTO conversations(id,title,model,created_at,updated_at) VALUES(@id,@t,@m,@c,@u)",
            ("@id", c.Id), ("@t", c.Title), ("@m", c.Model), ("@c", c.CreatedAt.ToString("o")), ("@u", c.UpdatedAt.ToString("o")));
        return c;
    }

    public void Rename(string id, string title)
    {
        _db.Run("UPDATE conversations SET title=@t, updated_at=@u WHERE id=@id",
            ("@id", id), ("@t", title), ("@u", DateTimeOffset.Now.ToString("o")));
    }

    public void SetModel(string id, string? model) =>
        _db.Run("UPDATE conversations SET model=@m WHERE id=@id", ("@id", id), ("@m", model));

    public void Touch(string id) =>
        _db.Run("UPDATE conversations SET updated_at=@u WHERE id=@id", ("@id", id), ("@u", DateTimeOffset.Now.ToString("o")));

    public void DeleteConversation(string id) => _db.Run("DELETE FROM conversations WHERE id=@id", ("@id", id));

    public List<ChatMessage> Messages(string conversationId) =>
        _db.Query("SELECT id,conversation_id,role,content,model_id,participant,created_at FROM messages WHERE conversation_id=@c ORDER BY id",
            MapMsg, ("@c", conversationId));

    public ChatMessage Add(ChatMessage m)
    {
        _db.Run("""
            INSERT INTO messages(conversation_id,role,content,model_id,participant,created_at)
            VALUES(@c,@r,@content,@m,@p,@t)
            """,
            ("@c", m.ConversationId), ("@r", m.Role.ToString()), ("@content", m.Content),
            ("@m", m.ModelId), ("@p", m.Participant), ("@t", m.Timestamp.ToString("o")));
        m.Id = Convert.ToInt64(_db.Scalar("SELECT last_insert_rowid();") ?? 0L);
        Touch(m.ConversationId);
        return m;
    }

    public void AddAttachment(long messageId, ChatAttachment attachment) =>
        _db.Run("INSERT INTO message_attachments(message_id,name,media_type,data) VALUES(@id,@name,@type,@data)",
            ("@id", messageId), ("@name", attachment.Name), ("@type", attachment.MediaType), ("@data", attachment.Data));

    public List<ChatAttachment> Attachments(long messageId) =>
        _db.Query("SELECT name,media_type,data FROM message_attachments WHERE message_id=@id ORDER BY id",
            r => new ChatAttachment(r.GetString(0), r.GetString(1), (byte[])r.GetValue(2)), ("@id", messageId));

    /// <summary>Atomically insert a message and its attachments.</summary>
    public ChatMessage AddWithAttachments(ChatMessage m, IEnumerable<ChatAttachment> attachments)
    {
        _db.InTransaction(() =>
        {
            Add(m);
            foreach (var a in attachments) AddAttachment(m.Id, a);
        });
        return m;
    }

    /// <summary>Fetch attachments for many messages in one query (no N+1).</summary>
    public Dictionary<long, List<ChatAttachment>> AttachmentsForMany(IEnumerable<long> messageIds)
    {
        var ids = messageIds.Distinct().ToList();
        var result = new Dictionary<long, List<ChatAttachment>>();
        foreach (var id in ids) result[id] = new List<ChatAttachment>();
        if (ids.Count == 0) return result;
        var placeholders = string.Join(",", ids.Select((_, i) => "@id" + i));
        var args = ids.Select((id, i) => ("@id" + i, (object?)id)).ToArray();
        var rows = _db.Query(
            $"SELECT message_id,name,media_type,data FROM message_attachments WHERE message_id IN ({placeholders}) ORDER BY id",
            r => (MessageId: r.GetInt64(0), Attachment: new ChatAttachment(r.GetString(1), r.GetString(2), (byte[])r.GetValue(3))), args);
        foreach (var row in rows)
            if (result.TryGetValue(row.MessageId, out var list)) list.Add(row.Attachment);
        return result;
    }

    public void UpdateContent(long id, string content)
    {
        _db.InTransaction(() =>
        {
            _db.Run("UPDATE messages SET content=@content WHERE id=@id", ("@id", id), ("@content", content));
            _db.Run("UPDATE conversations SET updated_at=@u WHERE id=(SELECT conversation_id FROM messages WHERE id=@id)",
                ("@id", id), ("@u", DateTimeOffset.Now.ToString("o")));
        });
    }

    public void DeleteMessage(long id) => _db.Run("DELETE FROM messages WHERE id=@id", ("@id", id));
}
