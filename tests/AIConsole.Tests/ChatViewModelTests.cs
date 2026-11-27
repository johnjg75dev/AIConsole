using System.Runtime.CompilerServices;
using System.Text;
using AIConsole.Data;
using AIConsole.Models;
using AIConsole.Services;
using AIConsole.ViewModels;

namespace AIConsole.Tests;

public sealed class ChatViewModelTests : IDisposable
{
    private const string EndpointId = "ep";
    private const string ModelKey = EndpointId + "::model";

    private readonly Db _db;
    private readonly ChatRepo _chat;
    private readonly EndpointRepo _endpoints;
    private readonly SettingsRepo _settings;
    private readonly OpenAiClient _http;
    private readonly ModelRefreshService _refresh;
    private readonly FakeChatClient _client;
    private readonly ChatViewModel _vm;

    public ChatViewModelTests()
    {
        _db = new Db(":memory:");
        _chat = new ChatRepo(_db);
        _endpoints = new EndpointRepo(_db);
        _settings = new SettingsRepo(_db);
        _http = new OpenAiClient();
        _refresh = new ModelRefreshService(_endpoints, _http, _settings);
        _client = new FakeChatClient();

        _endpoints.Upsert(new Endpoint { Id = EndpointId, Name = "Test provider", BaseUrl = "http://localhost/v1" });
        _endpoints.SyncModels(EndpointId, new[] { "model" }, autoEnable: true, prune: false);

        _vm = new ChatViewModel(_chat, _endpoints, _settings, _client, _refresh);
    }

    public void Dispose()
    {
        _refresh.Dispose();
        _http.Dispose();
        _db.Dispose();
    }

    // ── concurrent streams ──

    [Fact]
    public void StreamsInDifferentConversations_RunConcurrently_AndStopOnlyActive()
    {
        TestThread.RunStaAsync(async () =>
        {
            _client.HoldFirst = true;
            var first = CreateConversation("first");
            var second = CreateConversation("second");

            _vm.SelectConversation(first);
            _vm.InputText = "hello first";
            var firstSend = _vm.SendAsync();
            await _client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(_vm.IsStreaming);
            Assert.Single(_vm.Messages, m => m.IsStreaming);

            _vm.SelectConversation(second);
            Assert.False(_vm.IsStreaming);
            Assert.DoesNotContain(_vm.Messages, m => m.IsStreaming);

            _vm.InputText = "hello second";
            await _vm.SendAsync();

            Assert.False(_vm.IsStreaming);
            Assert.Contains(_vm.Messages, m => m.RawMarkdown == "reply");
            Assert.False(firstSend.IsCompleted);

            _vm.SelectConversation(first);
            Assert.True(_vm.IsStreaming);
            var live = Assert.Single(_vm.Messages, m => m.IsStreaming);
            Assert.Equal("partial", live.RawMarkdown);

            _vm.Stop();
            await firstSend.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(_vm.IsStreaming);
            Assert.False(live.IsStreaming);
            var firstMessages = _chat.Messages(first.Id);
            Assert.Equal(2, firstMessages.Count);
            Assert.Equal("partial", firstMessages[^1].Content);
            Assert.Equal(2, _chat.Messages(second.Id).Count);
        });
    }

    [Fact]
    public void Stop_DoesNotCancelAnotherConversationsStream()
    {
        TestThread.RunStaAsync(async () =>
        {
            _client.HoldFirst = true;
            var first = CreateConversation("first");
            var second = CreateConversation("second");

            _vm.SelectConversation(first);
            _vm.InputText = "one";
            var firstSend = _vm.SendAsync();
            await _client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            _vm.SelectConversation(second);
            _vm.Stop(); // no stream in the selected conversation

            Assert.False(firstSend.IsCompleted);
            _vm.SelectConversation(first);
            Assert.True(_vm.IsStreaming);

            _vm.Stop();
            await firstSend.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(_vm.IsStreaming);
        });
    }

    // ── edit as new ──

    [Fact]
    public void EditAsNew_OnLastMessage_ReplacesItWhenSending()
    {
        TestThread.RunStaAsync(async () =>
        {
            var conversation = CreateConversation("edit");
            AddMessage(conversation, ChatRole.User, "original");
            _vm.SelectConversation(conversation);
            var original = Assert.Single(_vm.Messages);

            original.EditAsNewCommand.Execute(null);
            Assert.Equal("original", _vm.InputText);

            _vm.InputText = "edited";
            await _vm.SendAsync();

            var saved = _chat.Messages(conversation.Id);
            Assert.Equal(new[] { "edited", "reply" }, saved.Select(m => m.Content));
            Assert.DoesNotContain(saved, m => m.Content == "original");
        });
    }

    [Fact]
    public void EditAsNew_KeepsOriginal_WhenReplacementSettingIsOff()
    {
        TestThread.RunStaAsync(async () =>
        {
            _settings.Set(SettingKeys.Settings, new AppSettings { ReplaceLastMessageOnEditAsNew = false });
            var conversation = CreateConversation("edit");
            AddMessage(conversation, ChatRole.User, "original");
            _vm.SelectConversation(conversation);

            _vm.Messages.Single().EditAsNewCommand.Execute(null);
            _vm.InputText = "edited";
            await _vm.SendAsync();

            var saved = _chat.Messages(conversation.Id);
            Assert.Equal(new[] { "original", "edited", "reply" }, saved.Select(m => m.Content));
        });
    }

    [Fact]
    public void EditAsNew_KeepsOriginal_WhenItWasNotTheLastMessage()
    {
        TestThread.RunStaAsync(async () =>
        {
            var conversation = CreateConversation("edit");
            AddMessage(conversation, ChatRole.User, "original");
            AddMessage(conversation, ChatRole.Assistant, "old reply");
            _vm.SelectConversation(conversation);

            _vm.Messages.First().EditAsNewCommand.Execute(null);
            _vm.InputText = "edited";
            await _vm.SendAsync();

            var saved = _chat.Messages(conversation.Id);
            Assert.Equal(new[] { "original", "old reply", "edited", "reply" }, saved.Select(m => m.Content));
        });
    }

    [Fact]
    public void EditAsNew_DoesNotSendAttachments_WhenTheSettingIsOff()
    {
        TestThread.RunSta(() =>
        {
            var conversation = CreateConversation("edit");
            AddMessage(conversation, ChatRole.User, "original",
                new[] { new ChatAttachment("notes.txt", "text/plain", Encoding.UTF8.GetBytes("SECRET")) });
            _vm.SelectConversation(conversation);

            _vm.Messages.Single().EditAsNewCommand.Execute(null);

            Assert.False(_vm.CanAttach);
            Assert.Empty(_vm.PendingAttachments);
        });
    }

    [Fact]
    public void EditAsNew_RestoresAttachments_WhenTheSettingIsOn()
    {
        TestThread.RunSta(() =>
        {
            _settings.Set(SettingKeys.Settings, new AppSettings { SendAttachments = true });
            _vm.ReloadSettings();
            var conversation = CreateConversation("edit");
            AddMessage(conversation, ChatRole.User, "original",
                new[] { new ChatAttachment("notes.txt", "text/plain", Encoding.UTF8.GetBytes("SECRET")) });
            _vm.SelectConversation(conversation);

            _vm.Messages.Single().EditAsNewCommand.Execute(null);

            var pending = Assert.Single(_vm.PendingAttachments);
            Assert.Equal("notes.txt", pending.Name);
        });
    }

    // ── context cutoff ──

    [Fact]
    public void EndContext_ExcludesLaterMessages_AndAdvancesAfterSending()
    {
        TestThread.RunStaAsync(async () =>
        {
            var conversation = CreateConversation("context");
            AddMessage(conversation, ChatRole.User, "u1");
            var included = AddMessage(conversation, ChatRole.Assistant, "a1");
            AddMessage(conversation, ChatRole.User, "u2");
            AddMessage(conversation, ChatRole.Assistant, "a2");
            _vm.SelectConversation(conversation);

            _vm.Messages.Single(m => m.Id == included.Id).EndContextCommand.Execute(null);

            Assert.All(_vm.Messages.Where(m => m.Id > included.Id), m => Assert.True(m.IsOutOfContext));
            Assert.All(_vm.Messages.Where(m => m.Id <= included.Id), m => Assert.False(m.IsOutOfContext));
            Assert.Equal(included.Id, conversation.ContextEndMessageId);
            Assert.Contains("4 chars", _vm.ContextUsageText);

            _vm.InputText = "u3";
            await _vm.SendAsync();

            var firstRequest = _client.Requests.Single();
            Assert.Equal(new[] { "u1", "a1", "u3" },
                firstRequest.Messages.Select(m => Assert.IsType<string>(m.Content)));

            var savedConversation = _chat.Conversations().Single(c => c.Id == conversation.Id);
            var newAssistant = _chat.Messages(conversation.Id)[^1];
            var newUser = _vm.Messages.Single(m => m.RawMarkdown == "u3");
            Assert.Equal(included.Id, savedConversation.ContextEndMessageId);
            Assert.Equal(newUser.Id, savedConversation.ContextResumeMessageId);
            Assert.Equal(included.Id, conversation.ContextEndMessageId);
            Assert.Equal(newUser.Id, conversation.ContextResumeMessageId);
            Assert.True(_vm.Messages.Single(m => m.Id > included.Id && m.RawMarkdown == "u2").IsOutOfContext);
            Assert.False(newUser.IsOutOfContext);
            Assert.False(_vm.Messages.Single(m => m.Id == newAssistant.Id).IsOutOfContext);

            _vm.InputText = "u4";
            await _vm.SendAsync();

            var secondRequest = _client.Requests[^1];
            Assert.Equal(new[] { "u1", "a1", "u3", "reply", "u4" },
                secondRequest.Messages.Select(m => Assert.IsType<string>(m.Content)));
            Assert.True(_vm.Messages.Single(m => m.RawMarkdown == "u2").IsOutOfContext);
        });
    }

    [Fact]
    public void FullContext_ClearsCutoffAndFlags()
    {
        TestThread.RunSta(() =>
        {
            var conversation = CreateConversation("context");
            AddMessage(conversation, ChatRole.User, "u1");
            var included = AddMessage(conversation, ChatRole.Assistant, "a1");
            AddMessage(conversation, ChatRole.User, "u2");
            _vm.SelectConversation(conversation);

            _vm.Messages.Single(m => m.Id == included.Id).EndContextCommand.Execute(null);
            Assert.Contains(_vm.Messages, m => m.IsOutOfContext);

            _vm.Messages.First().FullContextCommand.Execute(null);

            Assert.DoesNotContain(_vm.Messages, m => m.IsOutOfContext);
            Assert.Null(conversation.ContextEndMessageId);
            Assert.Null(conversation.ContextResumeMessageId);
            var saved = _chat.Conversations().Single(c => c.Id == conversation.Id);
            Assert.Null(saved.ContextEndMessageId);
            Assert.Null(saved.ContextResumeMessageId);
        });
    }

    [Fact]
    public void ContextChanges_AreBlockedWhileTheActiveConversationStreams()
    {
        TestThread.RunStaAsync(async () =>
        {
            _client.HoldFirst = true;
            var conversation = CreateConversation("context");
            AddMessage(conversation, ChatRole.User, "first");
            _vm.SelectConversation(conversation);

            _vm.InputText = "streaming";
            var send = _vm.SendAsync();
            await _client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var user = _vm.Messages.First(m => m.RawMarkdown == "streaming");
            user.EndContextCommand.Execute(null);

            Assert.Contains("Stop the current response", _vm.StatusMessage);
            Assert.Null(conversation.ContextEndMessageId);

            _vm.Stop();
            await send.WaitAsync(TimeSpan.FromSeconds(5));
        });
    }

    // ── attachment policy ──

    [Fact]
    public void Attachments_AreExcludedByDefault_AndIncludedWhenEnabled()
    {
        TestThread.RunStaAsync(async () =>
        {
            var conversation = CreateConversation("attachments");
            AddMessage(conversation, ChatRole.User, "look",
                new[] { new ChatAttachment("notes.txt", "text/plain", Encoding.UTF8.GetBytes("SECRET")) });
            _vm.SelectConversation(conversation);

            Assert.False(_vm.CanAttach);
            _vm.InputText = "next";
            await _vm.SendAsync();

            var defaultContent = Assert.IsType<string>(_client.Requests.Single().Messages[0].Content);
            Assert.Equal("look", defaultContent);

            _settings.Set(SettingKeys.Settings, new AppSettings { SendAttachments = true });
            _vm.ReloadSettings();
            Assert.True(_vm.CanAttach);

            _vm.InputText = "again";
            await _vm.SendAsync();

            var enabledContent = Assert.IsType<string>(_client.Requests[^1].Messages[0].Content);
            Assert.Contains("Attachment: notes.txt", enabledContent);
            Assert.Contains("SECRET", enabledContent);
        });
    }

    // ── helpers ──

    private Conversation CreateConversation(string title)
    {
        var created = _chat.Create(new Conversation { Title = title, Model = ModelKey });
        _vm.ReloadConversations();
        return _vm.Conversations.Single(c => c.Id == created.Id);
    }

    private ChatMessage AddMessage(Conversation conversation, ChatRole role, string content,
        IReadOnlyList<ChatAttachment>? attachments = null)
    {
        var message = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = role,
            Content = content,
            Participant = role == ChatRole.User ? "You" : "model",
            ModelId = ModelKey,
        };
        return attachments is null
            ? _chat.Add(message)
            : _chat.AddWithAttachments(message, attachments);
    }

    private sealed record RecordedRequest(
        string BaseUrl, string? ApiKey, string Model, IReadOnlyList<ChatMessageDto> Messages);

    private sealed class FakeChatClient : IChatClient
    {
        private int _calls;

        public List<RecordedRequest> Requests { get; } = new();
        public bool HoldFirst { get; set; }
        public string Reply { get; set; } = "reply";
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<string>> ListModelsAsync(string baseUrl, string? apiKey, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public async IAsyncEnumerable<string> StreamChatAsync(
            string baseUrl, string? apiKey, string model, IReadOnlyList<ChatMessageDto> messages,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _calls);
            lock (Requests) Requests.Add(new RecordedRequest(baseUrl, apiKey, model, messages.ToList()));

            if (call == 1 && HoldFirst)
            {
                yield return "partial";
                Started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            yield return Reply;
        }

        public Task<string> DecideAsync(string baseUrl, string? apiKey, string model, string state,
            string type, string instructions, IReadOnlyList<string> criteria, CancellationToken ct = default)
            => Task.FromResult("");
    }
}
