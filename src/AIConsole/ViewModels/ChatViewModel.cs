using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using AIConsole.Data;
using AIConsole.Models;
using AIConsole.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIConsole.ViewModels;

public sealed record ModelChoice(string Key, string ModelId, string EndpointName)
{
    public string Display => string.IsNullOrEmpty(EndpointName) ? ModelId : $"{ModelId}  ·  {EndpointName}";
}

public partial class ChatViewModel : ObservableObject
{
    private sealed class ActiveStream
    {
        public required string ConversationId { get; init; }
        public required MessageViewModel Assistant { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required long UserMessageId { get; init; }
        public StringBuilder Buffer { get; } = new();
    }

    private readonly ChatRepo _chat;
    private readonly EndpointRepo _endpoints;
    private readonly SettingsRepo _settings;
    private readonly IChatClient _client;
    private readonly ModelRefreshService _refresh;
    private readonly Dictionary<string, ActiveStream> _streams = new();
    private (string ConversationId, long MessageId)? _pendingEditAsNew;
    private AppSettings _appSettings;

    public ObservableCollection<Conversation> Conversations { get; } = new();
    public ObservableCollection<ModelChoice> Models { get; } = new();
    public ObservableCollection<ChatAttachment> PendingAttachments { get; } = new();

    [ObservableProperty] private ObservableCollection<MessageViewModel> _messages = new();
    [ObservableProperty] private Conversation? _selectedConversation;
    [ObservableProperty] private ModelChoice? _selectedModel;
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _sidebarVisible = true;
    [ObservableProperty] private bool _showLineNumbers = true;
    [ObservableProperty] private double _sidebarWidth = 210;
    [ObservableProperty] private bool _canAttach;

    public bool EnterToSend => _appSettings.EnterToSend;

    public string ComposerHint => EnterToSend
        ? "Enter to send · Shift+Enter for newline"
        : "Ctrl+Enter to send · Enter for a newline";
    public string ContextUsageText { get; private set; } = "0 chars";
    public string ContextUsageDetails { get; private set; } = "No messages in context.";

    public event Action<string, string>? MessageCompleted;
    public event Action? MessageAdded;
    public event Action? MessagesChanged;
    public event Action? ComposerFocusRequested;

    public ChatViewModel(ChatRepo chat, EndpointRepo endpoints, SettingsRepo settings, IChatClient client, ModelRefreshService refresh)
    {
        _chat = chat; _endpoints = endpoints; _settings = settings; _client = client; _refresh = refresh;
        _appSettings = settings.Get(SettingKeys.Settings, new AppSettings());
        _showLineNumbers = _appSettings.ShowLineNumbers;
        _canAttach = _appSettings.SendAttachments;
        Markdown.MarkdownRenderer.ShowLineNumbers = _appSettings.ShowLineNumbers;
        _refresh.ModelsChanged += RebuildModels;
        PendingAttachments.CollectionChanged += OnPendingAttachmentsChanged;
        ReloadConversations();
        RebuildModels();
        RecalculateContextUsage();
    }

    /// <summary>Re-reads chat-related settings, or accepts the live settings being edited.</summary>
    public void ReloadSettings(AppSettings? live = null)
    {
        _appSettings = live ?? _settings.Get(SettingKeys.Settings, new AppSettings());
        CanAttach = _appSettings.SendAttachments;
        if (!CanAttach) PendingAttachments.Clear();
        OnPropertyChanged(nameof(EnterToSend));
        OnPropertyChanged(nameof(ComposerHint));
        RecalculateContextUsage();
    }

    private void OnPendingAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RecalculateContextUsage();

    // ── conversations ──

    public void RerenderMarkdown() { foreach (var m in Messages) m.RenderMarkdown(); }

    public void ReloadConversations()
    {
        Conversations.Clear();
        foreach (var c in _chat.Conversations()) Conversations.Add(c);
    }

    partial void OnMessagesChanged(ObservableCollection<MessageViewModel> value) => MessagesChanged?.Invoke();

    partial void OnSelectedConversationChanged(Conversation? value)
    {
        _pendingEditAsNew = null;
        LoadMessages(value);

        if (value?.Model is { } k && Models.FirstOrDefault(m => m.Key == k) is { } match && SelectedModel?.Key != k)
            SelectedModel = match;

        UpdateStreamingState();
        RecalculateContextUsage();
        MessagesChanged?.Invoke();
    }

    private void LoadMessages(Conversation? value)
    {
        var list = new ObservableCollection<MessageViewModel>();
        if (value is not null)
        {
            var msgs = _chat.Messages(value.Id);
            var attMap = _chat.AttachmentsForMany(msgs.Select(m => m.Id));
            foreach (var m in msgs)
            {
                var vm = new MessageViewModel(m);
                vm.SetAttachments(attMap.TryGetValue(m.Id, out var listAtt) ? listAtt : EmptyAttachments);
                Hook(vm);
                list.Add(vm);
            }

            // Add the live assistant placeholder unless its persisted row already loaded.
            if (_streams.TryGetValue(value.Id, out var stream) &&
                !list.Any(m => m.Id != 0 && m.Id == stream.Assistant.Id))
                list.Add(stream.Assistant);
        }

        Messages = list;
        UpdateContextFlags(value);
    }

    [RelayCommand]
    public void NewChat()
    {
        SelectedConversation = null;
        Messages = new ObservableCollection<MessageViewModel>();
        PendingAttachments.Clear();
        InputText = "";
        StatusMessage = "";
        _pendingEditAsNew = null;
        RecalculateContextUsage();
        MessagesChanged?.Invoke();
    }

    public void SelectConversation(Conversation c) => SelectedConversation = c;

    [RelayCommand]
    public void DeleteConversation(Conversation c)
    {
        if (_streams.TryGetValue(c.Id, out var stream))
        {
            stream.Cts.Cancel();
            _streams.Remove(c.Id);
        }

        _chat.DeleteConversation(c.Id);
        if (ReferenceEquals(c, SelectedConversation)) SelectedConversation = null;
        Conversations.Remove(c);
        UpdateStreamingState();
    }

    public void RenameConversation(Conversation c, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        _chat.Rename(c.Id, title.Trim());
        c.Title = title.Trim();
        c.UpdatedAt = DateTimeOffset.Now;
        RefreshConversationRow(c);
    }

    private void RefreshConversationRow(Conversation c)
    {
        var idx = Conversations.IndexOf(c);
        if (idx >= 0) { Conversations.RemoveAt(idx); Conversations.Insert(idx, c); }
    }

    private void TouchConversation(string conversationId, DateTimeOffset at)
    {
        var row = Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (row is null) return;
        row.LastMessageAt = at;
        var idx = Conversations.IndexOf(row);
        if (idx > 0) { Conversations.RemoveAt(idx); Conversations.Insert(0, row); }
    }

    [RelayCommand]
    public void ToggleSidebar() { SidebarVisible = !SidebarVisible; SidebarWidth = SidebarVisible ? 210 : 0; }

    partial void OnSidebarVisibleChanged(bool value) => SidebarWidth = value ? 210 : 0;

    // ── models ──

    [RelayCommand]
    public void RebuildModels()
    {
        _appSettings = _settings.Get(SettingKeys.Settings, new AppSettings());
        var key = SelectedConversation?.Model ?? SelectedModel?.Key ?? _appSettings.LastModelKey;
        Models.Clear();
        foreach (var m in _endpoints.EnabledModels())
            Models.Add(new ModelChoice(m.Key, m.ModelId, m.EndpointName));
        SelectedModel = Models.FirstOrDefault(x => x.Key == key) ?? Models.FirstOrDefault();
    }

    partial void OnSelectedModelChanged(ModelChoice? value)
    {
        if (value is null) return;
        _appSettings = _settings.Get(SettingKeys.Settings, new AppSettings());
        _appSettings.LastModelKey = value.Key;
        _settings.Set(SettingKeys.Settings, _appSettings);
        if (SelectedConversation is { } c) { _chat.SetModel(c.Id, value.Key); c.Model = value.Key; }
    }

    // ── sending ──

    [RelayCommand]
    public async Task SendAsync()
    {
        var settings = _settings.Get(SettingKeys.Settings, new AppSettings());
        _appSettings = settings;
        CanAttach = settings.SendAttachments;

        var text = InputText?.Trim() ?? "";
        var attachments = CanAttach ? PendingAttachments.ToList() : new List<ChatAttachment>();
        if (text.Length == 0 && attachments.Count == 0) return;

        if (SelectedModel is not { } model)
        {
            StatusMessage = "No model selected — add an endpoint in Settings.";
            return;
        }

        if (SelectedConversation is { } existing && _streams.ContainsKey(existing.Id)) return;

        try
        {
            var endpointKey = model.Key.Split("::", 2);
            var ep = _endpoints.Get(endpointKey[0]);
            if (ep is null) { StatusMessage = "Endpoint not found."; return; }

            if (SelectedConversation is null)
            {
                var title = text.Length == 0 ? attachments[0].Name : text.Length <= 42 ? text : text[..42] + "…";
                SelectedConversation = _chat.Create(new Conversation { Title = title, Model = model.Key });
                Conversations.Insert(0, SelectedConversation);
            }

            var conversation = SelectedConversation;
            var convId = conversation.Id;

            // Edit-as-new replacement happens before the outgoing context is captured.
            if (_pendingEditAsNew is { } pending && pending.ConversationId == convId)
            {
                _pendingEditAsNew = null;
                if (settings.ReplaceLastMessageOnEditAsNew)
                {
                    var last = Messages.LastOrDefault();
                    if (last is { IsUser: true } && last.Id == pending.MessageId && last.Id != 0)
                    {
                        _chat.DeleteMessage(last.Id);
                        Messages.Remove(last);
                        if (conversation.ContextEndMessageId == last.Id)
                            SetContextEndInternal(conversation, null, null);
                        else if (conversation.ContextResumeMessageId == last.Id)
                            SetContextEndInternal(conversation, conversation.ContextEndMessageId, null);
                    }
                }
            }

            // Build the outgoing context before adding the new user message. A cutoff
            // keeps the old prefix and, once a new exchange exists, the newer suffix;
            // the messages between them stay visible but out of context.
            var all = _chat.Messages(convId);
            var cutoff = conversation.ContextEndMessageId;
            var resume = conversation.ContextResumeMessageId;
            var contextMessages = cutoff is { } end
                ? all.Where(m => m.Id <= end || (resume is { } start && m.Id >= start)).ToList()
                : all;
            var attMap = _chat.AttachmentsForMany(contextMessages.Select(m => m.Id));
            var history = contextMessages
                .Select(m => new ChatMessageDto(
                    m.Role switch { ChatRole.User => "user", ChatRole.Assistant => "assistant", _ => "system" },
                    BuildContent(m, CanAttach && attMap.TryGetValue(m.Id, out var att) ? att : EmptyAttachments)))
                .ToList();

            InputText = "";
            StatusMessage = "";

            var userVm = new MessageViewModel(true, "You") { RawMarkdown = text, ModelKey = model.Key };
            userVm.RenderMarkdown();
            var userMsg = _chat.AddWithAttachments(new ChatMessage
            {
                ConversationId = convId, Role = ChatRole.User, Content = text,
                Participant = "You", ModelId = model.Key,
            }, attachments);
            userVm.Id = userMsg.Id;
            userVm.SetAttachments(attachments);
            PendingAttachments.Clear();
            Hook(userVm);
            Messages.Add(userVm);
            history.Add(new ChatMessageDto("user", BuildContent(userMsg, attachments)));
            TouchConversation(convId, userMsg.Timestamp);

            // The first post-cutoff exchange starts the new suffix; later turns join
            // it. The old excluded tail remains outside both ranges.
            if (cutoff is not null)
            {
                conversation.ContextResumeMessageId ??= userMsg.Id;
                _chat.SetContext(convId, cutoff, conversation.ContextResumeMessageId);
            }
            UpdateContextFlags(conversation);
            RecalculateContextUsage();

            var assistantVm = new MessageViewModel(false, model.ModelId)
            {
                IsStreaming = true,
                IsAwaitingFirstToken = true,
                ModelKey = model.Key,
            };
            var cts = new CancellationTokenSource();
            var stream = new ActiveStream
            {
                ConversationId = convId,
                Assistant = assistantVm,
                Cts = cts,
                UserMessageId = userMsg.Id,
            };
            _streams[convId] = stream;
            Messages.Add(assistantVm);
            UpdateStreamingState();
            MessageAdded?.Invoke();

            long? savedId = null;
            try
            {
                await foreach (var delta in _client.StreamChatAsync(ep.BaseUrl, ep.ApiKey, model.ModelId, history, cts.Token))
                {
                    stream.Buffer.Append(delta);
                    assistantVm.RawMarkdown = stream.Buffer.ToString();
                    if (SelectedConversation?.Id == convId)
                    {
                        MessageAdded?.Invoke();
                        RecalculateContextUsage();
                    }
                }

                assistantVm.RawMarkdown = stream.Buffer.ToString();
                assistantVm.IsStreaming = false;
                savedId = PersistAssistant(convId, stream.Buffer.ToString(), model, assistantVm);
                var preview = stream.Buffer.Length > 120 ? stream.Buffer.ToString(0, 120) + "…" : stream.Buffer.ToString();
                MessageCompleted?.Invoke(model.ModelId, preview);
            }
            catch (OperationCanceledException)
            {
                assistantVm.RawMarkdown = stream.Buffer.ToString();
                assistantVm.IsStreaming = false;
                if (stream.Buffer.Length > 0)
                    savedId = PersistAssistant(convId, stream.Buffer.ToString(), model, assistantVm);
                else
                    RemoveVmIfPresent(assistantVm);
            }
            catch (Exception ex)
            {
                assistantVm.IsStreaming = false;
                if (stream.Buffer.Length > 0)
                {
                    assistantVm.RawMarkdown = stream.Buffer.ToString();
                    savedId = PersistAssistant(convId, stream.Buffer.ToString(), model, assistantVm);
                }
                else RemoveVmIfPresent(assistantVm);
                StatusMessage = ex.Message;
            }
            finally
            {
                FinishStream(convId, stream, savedId);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            UpdateStreamingState();
        }
    }

    private void FinishStream(string convId, ActiveStream stream, long? savedAssistantId)
    {
        _streams.Remove(convId);
        stream.Cts.Dispose();

        if (SelectedConversation?.Id == convId)
        {
            if (savedAssistantId is not null && !Messages.Contains(stream.Assistant))
                Messages.Add(stream.Assistant);
            UpdateContextFlags(SelectedConversation);
            RecalculateContextUsage();
            MessagesChanged?.Invoke();
        }

        UpdateStreamingState();
        TouchConversation(convId, DateTimeOffset.Now);
    }

    private long? PersistAssistant(string convId, string content, ModelChoice model, MessageViewModel vm)
    {
        try
        {
            var saved = _chat.Add(new ChatMessage
            {
                ConversationId = convId, Role = ChatRole.Assistant,
                Content = content, ModelId = model.Key, Participant = model.ModelId,
            });
            vm.Id = saved.Id;
            return saved.Id;
        }
        catch { return null; }
    }

    [RelayCommand]
    public void Stop()
    {
        if (SelectedConversation is { } c && _streams.TryGetValue(c.Id, out var stream))
            stream.Cts.Cancel();
    }

    private void UpdateStreamingState() =>
        IsStreaming = SelectedConversation is { } c && _streams.ContainsKey(c.Id);

    // ── context ──

    private void OnContextEndRequested(MessageViewModel vm)
    {
        if (IsStreaming) { StatusMessage = "Stop the current response before changing context."; return; }
        if (vm.Id == 0 || SelectedConversation is not { } c) return;
        SetContextEndInternal(c, vm.Id, null);
        StatusMessage = "Context ends at this message.";
    }

    private void OnFullContextRequested()
    {
        if (IsStreaming) { StatusMessage = "Stop the current response before changing context."; return; }
        if (SelectedConversation is not { } c) return;
        SetContextEndInternal(c, null, null);
        StatusMessage = "The full conversation is in context.";
    }

    private void SetContextEndInternal(Conversation conversation, long? endMessageId, long? resumeMessageId)
    {
        conversation.ContextEndMessageId = endMessageId;
        conversation.ContextResumeMessageId = resumeMessageId;
        _chat.SetContext(conversation.Id, endMessageId, resumeMessageId);
        UpdateContextFlags(conversation);
        RecalculateContextUsage();
    }

    private void UpdateContextFlags(Conversation? conversation)
    {
        var cutoff = conversation?.ContextEndMessageId;
        var resume = conversation?.ContextResumeMessageId;
        foreach (var m in Messages)
            m.IsOutOfContext = cutoff is { } id && m.Id != 0 && m.Id > id &&
                               (resume is not { } start || m.Id < start);
    }

    private void RecalculateContextUsage()
    {
        long chars = 0;
        var count = 0;
        foreach (var m in Messages)
        {
            if (m.IsOutOfContext) continue;
            chars += m.RawMarkdown.Length;
            count++;
        }

        if (!string.IsNullOrEmpty(InputText))
        {
            chars += InputText.Length;
            count++;
        }

        if (CanAttach)
        {
            foreach (var a in PendingAttachments)
            {
                if (a.IsImage) continue;
                try { chars += Encoding.UTF8.GetString(a.Data).Length; }
                catch { chars += a.Data.Length; }
            }
        }

        var tokens = (int)Math.Ceiling(chars / 4.0);
        ContextUsageText = $"{chars:N0} chars · ~{tokens:N0} tok";
        ContextUsageDetails = $"Next request: {count} message(s), {chars:N0} characters, ~{tokens:N0} tokens (estimated). Images are not counted.";
        OnPropertyChanged(nameof(ContextUsageText));
        OnPropertyChanged(nameof(ContextUsageDetails));
    }

    partial void OnInputTextChanged(string value) => RecalculateContextUsage();

    // ── message actions ──

    private static readonly IReadOnlyList<ChatAttachment> EmptyAttachments = Array.Empty<ChatAttachment>();

    /// <summary>Build the OpenAI message `content` (string, or multimodal parts when attachments exist).</summary>
    internal static object BuildContent(ChatMessage message, IReadOnlyList<ChatAttachment> attachments)
    {
        if (attachments.Count == 0) return message.Content;
        if (attachments.All(a => !a.IsImage))
            return message.Content + string.Concat(attachments.Select(a => $"\n\nAttachment: {a.Name}\n\n{Encoding.UTF8.GetString(a.Data)}"));
        var parts = new List<object>();
        if (!string.IsNullOrWhiteSpace(message.Content)) parts.Add(new { type = "text", text = message.Content });
        foreach (var attachment in attachments)
        {
            if (attachment.IsImage)
                parts.Add(new { type = "image_url", image_url = new { url = $"data:{attachment.MediaType};base64,{Convert.ToBase64String(attachment.Data)}" } });
            else
                parts.Add(new { type = "text", text = $"Attachment: {attachment.Name}\n\n{Encoding.UTF8.GetString(attachment.Data)}" });
        }
        return parts;
    }

    [RelayCommand]
    private void RemoveAttachment(ChatAttachment attachment) => PendingAttachments.Remove(attachment);

    public async Task<string> DecideAsync(string state, string type, string instructions, IReadOnlyList<string> criteria, CancellationToken ct)
    {
        if (SelectedModel is not { } model) throw new InvalidOperationException("Select a model first.");
        var ep = _endpoints.Get(model.Key.Split("::", 2)[0]) ?? throw new InvalidOperationException("Endpoint not found.");
        return await _client.DecideAsync(ep.BaseUrl, ep.ApiKey, model.ModelId, state, type, instructions, criteria, ct);
    }

    private void Hook(MessageViewModel vm)
    {
        vm.RemoveRequested += OnRemoveRequested;
        vm.EditRequested += OnEditRequested;
        vm.EditAsNewRequested += OnEditAsNewRequested;
        vm.ContextEndRequested += OnContextEndRequested;
        vm.FullContextRequested += OnFullContextRequested;
    }

    private void OnEditRequested(MessageViewModel vm)
    {
        if (IsStreaming) { StatusMessage = "Stop the current response before editing history."; return; }
        if (vm.Id == 0 || string.IsNullOrWhiteSpace(vm.EditText)) { StatusMessage = "Message text cannot be empty."; return; }
        _chat.UpdateContent(vm.Id, vm.EditText);
        vm.RawMarkdown = vm.EditText;
        vm.RenderMarkdown();
        vm.IsEditing = false;
        RecalculateContextUsage();
        StatusMessage = "Message updated. Later replies were not regenerated.";
    }

    private void OnEditAsNewRequested(MessageViewModel vm)
    {
        if (IsStreaming) { StatusMessage = "Stop the current response before editing history."; return; }
        if (SelectedConversation is not { } conversation) return;

        InputText = vm.RawMarkdown;
        PendingAttachments.Clear();
        if (CanAttach)
            foreach (var attachment in vm.Attachments)
                PendingAttachments.Add(attachment.Attachment);

        _pendingEditAsNew = (conversation.Id, vm.Id);
        if (!string.IsNullOrWhiteSpace(vm.ModelKey) &&
            Models.FirstOrDefault(m => m.Key == vm.ModelKey) is { } model)
            SelectedModel = model;

        ComposerFocusRequested?.Invoke();
        StatusMessage = "Editing as a new message.";
    }

    private void OnRemoveRequested(MessageViewModel vm)
    {
        if (IsStreaming) { StatusMessage = "Stop the current response before removing messages."; return; }
        if (vm.Id != 0) _chat.DeleteMessage(vm.Id);
        Messages.Remove(vm);

        // A removed message can no longer be replaced by the pending edit.
        if (_pendingEditAsNew is { } pending && pending.ConversationId == SelectedConversation?.Id &&
            pending.MessageId == vm.Id)
            _pendingEditAsNew = null;

        if (SelectedConversation is { } conversation)
        {
            if (conversation.ContextEndMessageId == vm.Id)
            {
                // The prefix ends at the message just before the removed one, not at
                // the newest message, so an already-excluded tail stays excluded.
                var previous = _chat.Messages(conversation.Id)
                    .Where(m => m.Id < vm.Id)
                    .Select(m => (long?)m.Id)
                    .LastOrDefault();
                SetContextEndInternal(conversation, previous, previous is null ? null : conversation.ContextResumeMessageId);
            }
            else if (conversation.ContextResumeMessageId == vm.Id)
            {
                var next = _chat.Messages(conversation.Id)
                    .Where(m => m.Id > vm.Id)
                    .Select(m => (long?)m.Id)
                    .FirstOrDefault();
                SetContextEndInternal(conversation, conversation.ContextEndMessageId, next);
            }

            var row = Conversations.FirstOrDefault(c => c.Id == conversation.Id);
            if (row is not null)
            {
                row.LastMessageAt = _chat.LastMessageAt(row.Id);
                RefreshConversationRow(row);
            }
        }

        RecalculateContextUsage();
        MessagesChanged?.Invoke();
    }

    private void RemoveVmIfPresent(MessageViewModel vm)
    {
        if (Messages.Remove(vm)) MessagesChanged?.Invoke();
    }
}
