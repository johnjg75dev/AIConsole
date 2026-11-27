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
    }

    private void HookVm()
    {
        if (ViewModel is null) return;
        ViewModel.MessageAdded += ScrollToBottom;
        ViewModel.MessagesChanged += ScrollToBottom;
        ViewModel.Messages.CollectionChanged += OnMessagesChanged;
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

        // Enter sends (when no shift), unless VM prefers not
        if (e.Key == Key.Return && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        { e.Handled = true; await Send(); return; }
    }

    private async Task Send()
    {
        if (ViewModel is { } vm) await vm.SendCommand.ExecuteAsync(null);
    }

    private void Conversation_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe && fe.DataContext is Conversation c)
            ViewModel?.SelectConversation(c);
    }
}
