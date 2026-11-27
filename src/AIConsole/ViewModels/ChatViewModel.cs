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

    [ObservableProperty] private Conversation? _selectedConversation;
    [ObservableProperty] private ModelChoice? _selectedModel;
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _sidebarVisible = true;
    [ObservableProperty] private bool _showLineNumbers = true;
    [ObservableProperty] private double _sidebarWidth = 210;

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
                Hook(vm);
                Messages.Add(vm);
            }
        }
        MessagesChanged?.Invoke();
    }

    [RelayCommand]
    public void NewChat()
    {
        if (IsStreaming) Stop();
        SelectedConversation = null;
        Messages.Clear();
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
        var key = SelectedModel?.Key ?? _appSettings.LastModelKey;
        Models.Clear();
        foreach (var m in _endpoints.EnabledModels())
            Models.Add(new ModelChoice(m.Key, m.ModelId, m.EndpointName));
        SelectedModel = Models.FirstOrDefault(x => x.Key == key) ?? Models.FirstOrDefault();
    }

    partial void OnSelectedModelChanged(ModelChoice? value)
    {
        if (value is null) return;
        _appSettings.LastModelKey = value.Key;
        _settings.Set(SettingKeys.Settings, _appSettings);
        if (SelectedConversation is { } c) { _chat.SetModel(c.Id, value.Key); c.Model = value.Key; }
    }

    // ── sending ──

    [RelayCommand]
    public async Task SendAsync()
    {
        var text = InputText?.Trim() ?? "";
        if (text.Length == 0 || IsStreaming) return;

        if (SelectedModel is not { } model)
        {
            StatusMessage = "No model selected — add an endpoint in Settings.";
            return;
        }

        var endpointKey = model.Key.Split("::", 2);
        var ep = _endpoints.Get(endpointKey[0]);
        if (ep is null) { StatusMessage = "Endpoint not found."; return; }

        InputText = "";
        StatusMessage = "";

        // ensure conversation
        if (SelectedConversation is null)
        {
            var title = text.Length <= 42 ? text : text[..42] + "…";
            SelectedConversation = _chat.Create(new Conversation { Title = title, Model = model.Key });
            Conversations.Insert(0, SelectedConversation);
        }
        var convId = SelectedConversation.Id;

        // user message
        var userVm = new MessageViewModel(true, "You") { RawMarkdown = text };
        userVm.RenderMarkdown();
        var userMsg = _chat.Add(new ChatMessage
        {
            ConversationId = convId, Role = ChatRole.User, Content = text,
            Participant = "You", ModelId = model.Key,
        });
        userVm.Id = userMsg.Id;
        Hook(userVm);
        Messages.Add(userVm);
        MessageAdded?.Invoke();

        // assistant placeholder
        var assistantVm = new MessageViewModel(false, model.ModelId) { IsStreaming = true };
        Messages.Add(assistantVm);
        MessageAdded?.Invoke();

        // history payload
        var history = _chat.Messages(convId)
            .Where(m => !m.Content.StartsWith("\u0000"))
            .Select(m => new ChatMessageDto(
                m.Role switch { ChatRole.User => "user", ChatRole.Assistant => "assistant", _ => "system" },
                m.Content))
            .ToList();
        // drop the just-added empty assistant turn (not persisted yet) — history already excludes it

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        IsStreaming = true;
        var sb = new System.Text.StringBuilder();

        try
        {
            await foreach (var delta in _client.StreamChatAsync(ep.BaseUrl, ep.ApiKey, model.ModelId, history, token))
            {
                sb.Append(delta);
                assistantVm.RawMarkdown = sb.ToString();
                MessageAdded?.Invoke(); // auto-scroll
            }
            assistantVm.RawMarkdown = sb.ToString();
            assistantVm.IsStreaming = false;
            assistantVm.RenderMarkdown();

            var saved = _chat.Add(new ChatMessage
            {
                ConversationId = convId, Role = ChatRole.Assistant,
                Content = sb.ToString(), ModelId = model.Key, Participant = model.ModelId,
            });
            assistantVm.Id = saved.Id;
            ReloadConversationsOrder();
        }
        catch (OperationCanceledException)
        {
            // keep partial output
            assistantVm.RawMarkdown = sb.ToString();
            assistantVm.IsStreaming = false;
            assistantVm.RenderMarkdown();
            if (sb.Length > 0)
            {
                var saved = _chat.Add(new ChatMessage
                {
                    ConversationId = convId, Role = ChatRole.Assistant,
                    Content = sb.ToString(), ModelId = model.Key, Participant = model.ModelId,
                });
                assistantVm.Id = saved.Id;
            }
            else RemoveVm(assistantVm);
            ReloadConversationsOrder();
        }
        catch (Exception ex)
        {
            assistantVm.IsStreaming = false;
            if (sb.Length > 0) { assistantVm.RawMarkdown = sb.ToString(); assistantVm.RenderMarkdown(); }
            else RemoveVm(assistantVm);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsStreaming = false;
            _cts?.Dispose(); _cts = null;
            MessagesChanged?.Invoke();
        }
    }

    [RelayCommand]
    public void Stop() => _cts?.Cancel();

    // ── message removal ──

    private void Hook(MessageViewModel vm) => vm.RemoveRequested += OnRemoveRequested;

    private void OnRemoveRequested(MessageViewModel vm)
    {
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


