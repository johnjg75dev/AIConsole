using System.Collections.Specialized;
using System.Windows.Controls;
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
}
