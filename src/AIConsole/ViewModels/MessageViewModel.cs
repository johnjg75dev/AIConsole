using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using AIConsole.Models;
using AIConsole.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIConsole.ViewModels;

public partial class MessageViewModel : ObservableObject
{
    public long Id { get; set; }
    public bool IsUser { get; }
    public string Participant { get; }
    public string DisplayName { get; }
    public DateTimeOffset Timestamp { get; }
    public string TimestampText => Timestamp.ToLocalTime().ToString("HH:mm");
    public string Role { get; }
    public string? ModelKey { get; set; }

    [ObservableProperty] private string _rawMarkdown = "";
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private bool _isAwaitingFirstToken;
    [ObservableProperty] private FrameworkElement? _rendered;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editText = "";
    [ObservableProperty] private string _attachmentsLabel = "";
    [ObservableProperty] private bool _isOutOfContext;

    private DispatcherTimer? _renderTimer;

    public ObservableCollection<AttachmentViewModel> Attachments { get; } = new();
    public bool HasAttachments => Attachments.Count > 0;

    public MessageViewModel(ChatMessage message)
    {
        Id = message.Id;
        IsUser = message.Role == ChatRole.User;
        Role = message.Role.ToString();
        Participant = message.Participant ?? (IsUser ? "You" : message.ModelId ?? "Assistant");
        DisplayName = Participant;
        Timestamp = message.Timestamp;
        ModelKey = message.ModelId;
        _rawMarkdown = message.Content;
        RenderMarkdown();
    }

    public MessageViewModel(bool isUser, string participant)
    {
        IsUser = isUser;
        Role = isUser ? "User" : "Assistant";
        Participant = participant;
        DisplayName = participant;
        Timestamp = DateTimeOffset.Now;
    }

    public void RenderMarkdown()
    {
        Rendered = Markdown.MarkdownRenderer.Render(RawMarkdown);
    }

    partial void OnRawMarkdownChanged(string value)
    {
        // The typing indicator stays up until real content exists. Render that first
        // content immediately so the bubble never blinks to an empty state.
        if (IsAwaitingFirstToken && value.Length > 0)
        {
            IsAwaitingFirstToken = false;
            RenderMarkdown();
            return;
        }

        if (IsStreaming) ScheduleLiveRender();
    }

    partial void OnIsStreamingChanged(bool value)
    {
        if (value) return;
        // An empty or failed reply still needs to stop the waiting animation.
        IsAwaitingFirstToken = false;
        StopLiveRender();
        RenderMarkdown();
    }

    /// <summary>Re-renders Markdown at most once per frame-ish interval while text arrives.</summary>
    private void ScheduleLiveRender()
    {
        var dispatcher = Dispatcher.FromThread(System.Threading.Thread.CurrentThread);
        if (dispatcher is null)
        {
            RenderMarkdown();
            return;
        }

        if (_renderTimer is null)
        {
            _renderTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(60),
            };
            _renderTimer.Tick += (_, _) =>
            {
                StopLiveRender();
                Rendered = Markdown.MarkdownRenderer.Render(RawMarkdown);
            };
        }
        if (!_renderTimer.IsEnabled) _renderTimer.Start();
    }

    private void StopLiveRender()
    {
        if (_renderTimer is { IsEnabled: true }) _renderTimer.Stop();
    }

    /// <summary>Raw markdown source for copy.</summary>
    public string SourceText => RawMarkdown;

    public void SetAttachments(IReadOnlyList<ChatAttachment> attachments)
    {
        Attachments.Clear();
        foreach (var attachment in attachments) Attachments.Add(new AttachmentViewModel(attachment));
        AttachmentsLabel = string.Join("  ·  ", attachments.Select(a => "📎 " + a.Name));
        OnPropertyChanged(nameof(HasAttachments));
    }

    [RelayCommand]
    private void Copy() { try { Clipboard.SetText(RawMarkdown); } catch { } }

    [RelayCommand]
    private void Edit() { if (!IsStreaming && Id != 0) { EditText = RawMarkdown; IsEditing = true; } }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private void SaveEdit() => EditRequested?.Invoke(this);

    public event Action<MessageViewModel>? EditRequested;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    public event Action<MessageViewModel>? RemoveRequested;

    [RelayCommand]
    private void EditAsNew() => EditAsNewRequested?.Invoke(this);

    public event Action<MessageViewModel>? EditAsNewRequested;

    [RelayCommand]
    private void EndContext() => ContextEndRequested?.Invoke(this);

    public event Action<MessageViewModel>? ContextEndRequested;

    [RelayCommand]
    private void FullContext() => FullContextRequested?.Invoke();

    public event Action? FullContextRequested;
}
