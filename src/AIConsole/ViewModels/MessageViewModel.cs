using System.Windows;
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

    [ObservableProperty] private string _rawMarkdown = "";
    [ObservableProperty] private bool _isStreaming;
    [ObservableProperty] private FrameworkElement? _rendered;

    public MessageViewModel(ChatMessage message)
    {
        Id = message.Id;
        IsUser = message.Role == ChatRole.User;
        Role = message.Role.ToString();
        Participant = message.Participant ?? (IsUser ? "You" : message.ModelId ?? "Assistant");
        DisplayName = Participant;
        Timestamp = message.Timestamp;
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

    /// <summary>Raw markdown source for copy.</summary>
    public string SourceText => RawMarkdown;

    [RelayCommand]
    private void Copy() { try { Clipboard.SetText(RawMarkdown); } catch { } }

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    public event Action<MessageViewModel>? RemoveRequested;
}
