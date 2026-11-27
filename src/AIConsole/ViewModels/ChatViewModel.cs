using System.Collections.ObjectModel;
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
    private readonly ChatRepo _chat;
    private readonly EndpointRepo _endpoints;
    private readonly SettingsRepo _settings;
    private readonly OpenAiClient _client;
    private readonly ModelRefreshService _refresh;

    private CancellationTokenSource? _cts;
    private AppSettings _appSettings;

    public ObservableCollection<Conversation> Conversations { get; } = new();
    public ObservableCollection<MessageViewModel> Messages { get; } = new();
    public ObservableCollection<ModelChoice> Models { get; } = new();
    public ObservableCollection<ChatAttachment> PendingAttachments { get; } = new();

    [ObservableProperty] private Conversation? _selectedConversation;
    [ObservableProperty] private ModelChoice? _selectedModel;
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _sidebarVisible = true;
    [ObservableProperty] private bool _showLineNumbers = true;
    [ObservableProperty] private double _sidebarWidth = 210;

    public bool EnterToSend => _appSettings.EnterToSend;

    public event Action<string, string>? MessageCompleted;
    public event Action? MessageAdded;
    public event Action? MessagesChanged;

    public ChatViewModel(ChatRepo chat, EndpointRepo endpoints, SettingsRepo settings, OpenAiClient client, ModelRefreshService refresh)
    {
        _chat = chat; _endpoints = endpoints; _settings = settings; _client = client; _refresh = refresh;
        _appSettings = settings.Get(SettingKeys.Settings, new AppSettings());
        _showLineNumbers = _appSettings.ShowLineNumbers;
        Markdown.MarkdownRenderer.ShowLineNumbers = _appSettings.ShowLineNumbers;
        _refresh.ModelsChanged += RebuildModels;
        ReloadConversations();
        RebuildModels();
    }

    // ── conversations ──

    public void RerenderMarkdown() { foreach (var m in Messages) m.RenderMarkdown(); }

    public void ReloadConversations()
    {
        Conversations.Clear();
        foreach (var c in _chat.Conversations()) Conversations.Add(c);
    }

    partial void OnSelectedConversationChanged(Conversation? value)
    {
        Messages.Clear();
        if (value is not null)
        {
            foreach (var m in _chat.Messages(value.Id))
            {
                var vm = new MessageViewModel(m);
                vm.AttachmentsLabel = AttachmentLabel(_chat.Attachments(m.Id));
                Hook(vm);
                Messages.Add(vm);
            }
        }
        // restore per-conversation model
        if (value?.Model is { } k && Models.FirstOrDefault(m => m.Key == k) is { } match && SelectedModel?.Key != k)
            SelectedModel = match;
        MessagesChanged?.Invoke();
    }

    [RelayCommand]
    public void NewChat()
    {
        if (IsStreaming) Stop();
        SelectedConversation = null;
        Messages.Clear();
        PendingAttachments.Clear();
        StatusMessage = "";
        MessagesChanged?.Invoke();
    }

    public void SelectConversation(Conversation c) => SelectedConversation = c;

    [RelayCommand]
    public void DeleteConversation(Conversation c)
    {
        _chat.DeleteConversation(c.Id);
        if (ReferenceEquals(c, SelectedConversation)) SelectedConversation = null;
        Conversations.Remove(c);
    }

    public void RenameConversation(Conversation c, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        _chat.Rename(c.Id, title.Trim());
        c.Title = title.Trim();
        // nudge UI
        var idx = Conversations.IndexOf(c);
        if (idx >= 0) { Conversations.RemoveAt(idx); Conversations.Insert(idx, c); }
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
        var text = InputText?.Trim() ?? "";
        if ((text.Length == 0 && PendingAttachments.Count == 0) || IsStreaming) return;

        if (SelectedModel is not { } model)
        {
            StatusMessage = "No model selected — add an endpoint in Settings.";
            return;
        }

        string? convId = null;
        MessageViewModel? assistantVm = null;
        var sb = new System.Text.StringBuilder();

        try
        {
            var endpointKey = model.Key.Split("::", 2);
            var ep = _endpoints.Get(endpointKey[0]);
            if (ep is null) { StatusMessage = "Endpoint not found."; return; }

            InputText = "";
            StatusMessage = "";

            // ensure conversation
            if (SelectedConversation is null)
            {
                var title = text.Length == 0 ? PendingAttachments[0].Name : text.Length <= 42 ? text : text[..42] + "…";
                SelectedConversation = _chat.Create(new Conversation { Title = title, Model = model.Key });
                Conversations.Insert(0, SelectedConversation);
            }
            convId = SelectedConversation.Id;

            // user message
            var userVm = new MessageViewModel(true, "You") { RawMarkdown = text };
            userVm.RenderMarkdown();
            var userMsg = _chat.Add(new ChatMessage
            {
                ConversationId = convId, Role = ChatRole.User, Content = text,
                Participant = "You", ModelId = model.Key,
            });
            userVm.Id = userMsg.Id;
            foreach (var attachment in PendingAttachments) _chat.AddAttachment(userMsg.Id, attachment);
            userVm.AttachmentsLabel = AttachmentLabel(PendingAttachments);
            PendingAttachments.Clear();
            Hook(userVm);
            Messages.Add(userVm);
            MessageAdded?.Invoke();

            // assistant placeholder
            assistantVm = new MessageViewModel(false, model.ModelId) { IsStreaming = true };
            Hook(assistantVm);
            Messages.Add(assistantVm);
            MessageAdded?.Invoke();

            // history payload (user msg persisted; assistant not yet)
            var history = _chat.Messages(convId)
                .Select(m => new ChatMessageDto(
                    m.Role switch { ChatRole.User => "user", ChatRole.Assistant => "assistant", _ => "system" },
                    BuildContent(m)))
                .ToList();

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            IsStreaming = true;

            await foreach (var delta in _client.StreamChatAsync(ep.BaseUrl, ep.ApiKey, model.ModelId, history, token))
            {
                sb.Append(delta);
                assistantVm.RawMarkdown = sb.ToString();
                MessageAdded?.Invoke(); // auto-scroll
            }
            assistantVm.RawMarkdown = sb.ToString();
            assistantVm.IsStreaming = false;
            assistantVm.RenderMarkdown();

            PersistAssistant(convId, sb.ToString(), model, assistantVm);
            ReloadConversationsOrder();
            var preview = sb.Length > 120 ? sb.ToString(0, 120) + "…" : sb.ToString();
            MessageCompleted?.Invoke(model.ModelId, preview);
        }
        catch (OperationCanceledException)
        {
            // keep partial output
            if (assistantVm is not null)
            {
                assistantVm.RawMarkdown = sb.ToString();
                assistantVm.IsStreaming = false;
                assistantVm.RenderMarkdown();
                if (sb.Length > 0 && convId is not null) PersistAssistant(convId, sb.ToString(), model, assistantVm);
                else RemoveVm(assistantVm);
            }
            ReloadConversationsOrder();
        }
        catch (Exception ex)
        {
            if (assistantVm is not null)
            {
                assistantVm.IsStreaming = false;
                if (sb.Length > 0) { assistantVm.RawMarkdown = sb.ToString(); assistantVm.RenderMarkdown(); }
                else RemoveVm(assistantVm);
            }
            StatusMessage = ex.Message;
        }
        finally
        {
            IsStreaming = false;
            _cts?.Dispose(); _cts = null;
            MessagesChanged?.Invoke();
        }
    }

    private void PersistAssistant(string convId, string content, ModelChoice model, MessageViewModel vm)
    {
        try
        {
            var saved = _chat.Add(new ChatMessage
            {
                ConversationId = convId, Role = ChatRole.Assistant,
                Content = content, ModelId = model.Key, Participant = model.ModelId,
            });
            vm.Id = saved.Id;
        }
        catch { /* keep streamed output visible even if persistence fails */ }
    }

    [RelayCommand]
    public void Stop() => _cts?.Cancel();

    // ── message removal ──

    private static string AttachmentLabel(IEnumerable<ChatAttachment> attachments) =>
        string.Join("  ·  ", attachments.Select(a => "📎 " + a.Name));

    private object BuildContent(ChatMessage message)
    {
        var attachments = _chat.Attachments(message.Id);
        if (attachments.Count == 0) return message.Content;
        if (attachments.All(a => !a.IsImage))
            return message.Content + string.Concat(attachments.Select(a => $"\n\nAttachment: {a.Name}\n\n{System.Text.Encoding.UTF8.GetString(a.Data)}"));
        var parts = new List<object>();
        if (!string.IsNullOrWhiteSpace(message.Content)) parts.Add(new { type = "text", text = message.Content });
        foreach (var attachment in attachments)
        {
            if (attachment.IsImage)
                parts.Add(new { type = "image_url", image_url = new { url = $"data:{attachment.MediaType};base64,{Convert.ToBase64String(attachment.Data)}" } });
            else
                parts.Add(new { type = "text", text = $"Attachment: {attachment.Name}\n\n{System.Text.Encoding.UTF8.GetString(attachment.Data)}" });
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
    }

    private void OnEditRequested(MessageViewModel vm)
    {
        if (IsStreaming) { StatusMessage = "Stop the current response before editing history."; return; }
        if (vm.Id == 0 || string.IsNullOrWhiteSpace(vm.EditText)) { StatusMessage = "Message text cannot be empty."; return; }
        _chat.UpdateContent(vm.Id, vm.EditText);
        vm.RawMarkdown = vm.EditText;
        vm.RenderMarkdown();
        vm.IsEditing = false;
        StatusMessage = "Message updated. Later replies were not regenerated.";
    }

    private void OnRemoveRequested(MessageViewModel vm)
    {
        if (IsStreaming) { StatusMessage = "Stop the current response before removing messages."; return; }
        if (vm.Id != 0) _chat.DeleteMessage(vm.Id);
        RemoveVm(vm);
    }

    private void RemoveVm(MessageViewModel vm)
    {
        Messages.Remove(vm);
        MessagesChanged?.Invoke();
    }

    private void ReloadConversationsOrder() => ReloadConversations();
}


