using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Windows.Input;
using AIConsole.Models;
using AIConsole.ViewModels;

namespace AIConsole.Views;

public partial class ChatView : UserControl
{
    public ChatViewModel? ViewModel => DataContext as ChatViewModel;

    public ChatView()
    {
        InitializeComponent();
        Loaded += (_, _) => HookVm();
        DataContextChanged += (_, _) => HookVm();
        Unloaded += (_, _) => Unhook();
    }

    private ChatViewModel? _hookedVm;

    private void HookVm()
    {
        if (ReferenceEquals(_hookedVm, ViewModel)) return;
        Unhook();
        _hookedVm = ViewModel;
        if (_hookedVm is null) return;
        _hookedVm.MessageAdded += ScrollToBottom;
        _hookedVm.MessagesChanged += ScrollToBottom;
        _hookedVm.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void Unhook()
    {
        if (_hookedVm is null) return;
        _hookedVm.MessageAdded -= ScrollToBottom;
        _hookedVm.MessagesChanged -= ScrollToBottom;
        _hookedVm.Messages.CollectionChanged -= OnMessagesChanged;
        _hookedVm = null;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToBottom();

    public void ScrollToBottom() => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
        new Action(() => MsgScroll.ScrollToBottom()));

    public void FocusInput() => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
        new Action(() => InputBox.Focus()));

    private void Attach_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var window = System.Windows.Window.GetWindow(this) as MainWindow;
        var hide = window?.HideOnFocusLoss ?? false;
        if (window is not null) window.HideOnFocusLoss = false;
        try
        {
            var dialog = new OpenFileDialog { Multiselect = true, Filter = "Supported files|*.txt;*.md;*.json;*.csv;*.xml;*.log;*.yaml;*.yml;*.cs;*.py;*.js;*.ts;*.html;*.css;*.png;*.jpg;*.jpeg;*.gif;*.webp|All files|*.*" };
            if (dialog.ShowDialog(window) != true) return;
            foreach (var path in dialog.FileNames)
            {
                try
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    var media = ext switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", _ => "text/plain" };
                    var info = new FileInfo(path);
                    if (info.Length > 5 * 1024 * 1024 || vm.PendingAttachments.Sum(a => (long)a.Data.Length) + info.Length > 10 * 1024 * 1024)
                        throw new InvalidOperationException("Attachments are limited to 5 MB each and 10 MB total.");
                    var data = File.ReadAllBytes(path);
                    if (media == "text/plain")
                    {
                        var text = new UTF8Encoding(false, true).GetString(data);
                        if (text.Contains('\0')) throw new InvalidOperationException("Only UTF-8 text and PNG, JPEG, GIF or WebP images are supported.");
                    }
                    vm.PendingAttachments.Add(new ChatAttachment(Path.GetFileName(path), media, data));
                }
                catch (Exception ex) { vm.StatusMessage = $"Could not attach {Path.GetFileName(path)}: {ex.Message}"; }
            }
        }
        finally { if (window is not null) window.HideOnFocusLoss = hide; }
    }

    private void Decision_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if (vm.SelectedModel is null) { vm.StatusMessage = "Select a decision model first."; return; }
        var window = System.Windows.Window.GetWindow(this) as MainWindow;
        var hide = window?.HideOnFocusLoss ?? false;
        if (window is not null) window.HideOnFocusLoss = false;
        try { new DecisionWindow(vm) { Owner = window }.ShowDialog(); }
        finally { if (window is not null) window.HideOnFocusLoss = hide; }
    }

    private async void Send_Click(object sender, System.Windows.RoutedEventArgs e) => await Send();

    private async void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null) return;

        // Ctrl+Enter always sends
        if (e.Key == Key.Return && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        { e.Handled = true; await Send(); return; }

        // Enter sends (when no shift) if EnterToSend; otherwise only Ctrl+Enter sends
        if (e.Key == Key.Return && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && vm.EnterToSend)
        { e.Handled = true; await Send(); return; }
    }

    private async Task Send()
    {
        try { if (ViewModel is { } vm) await vm.SendCommand.ExecuteAsync(null); }
        catch { /* SendAsync reports via StatusMessage */ }
    }

    private void Conversation_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe && fe.DataContext is Conversation c)
            ViewModel?.SelectConversation(c);
    }

    private void ConversationMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && menu.PlacementTarget is FrameworkElement target)
            menu.DataContext = target.DataContext;
    }

    private static Conversation? ConversationFrom(object sender)
    {
        if ((sender as FrameworkElement)?.DataContext is Conversation direct) return direct;
        if (sender is MenuItem item)
        {
            var menu = item.Parent as ContextMenu
                       ?? ItemsControl.ItemsControlFromItemContainer(item) as ContextMenu;
            if (menu?.PlacementTarget is FrameworkElement target)
                return target.DataContext as Conversation;
        }
        return null;
    }

    private void RenameConversation_Click(object sender, RoutedEventArgs e)
    {
        if (ConversationFrom(sender) is not { } conversation || ViewModel is not { } vm) return;

        var window = Window.GetWindow(this) as MainWindow;
        var hide = window?.HideOnFocusLoss ?? false;
        if (window is not null) window.HideOnFocusLoss = false;
        try
        {
            var title = TextPromptWindow.Ask(window, "Rename conversation", "Conversation name", conversation.Title);
            if (title is not null) vm.RenameConversation(conversation, title);
        }
        finally { if (window is not null) window.HideOnFocusLoss = hide; }
    }

    private void RemoveConversation_Click(object sender, RoutedEventArgs e)
    {
        if (ConversationFrom(sender) is { } conversation)
            ViewModel?.DeleteConversation(conversation);
    }
}
