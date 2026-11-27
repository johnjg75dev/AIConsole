using System.Windows;
using System.Windows.Threading;
using AIConsole.ViewModels;
using AIConsole.Views.Controls;

namespace AIConsole.Tests;

public class ChatBubbleTests
{
    [Fact]
    public void TypingIndicator_IsVisibleUntilTheFirstToken()
    {
        TestThread.RunSta(() =>
        {
            var vm = new MessageViewModel(isUser: false, "bot")
            {
                IsStreaming = true,
                IsAwaitingFirstToken = true,
            };
            var bubble = new ChatBubble { DataContext = vm };
            bubble.Measure(new Size(500, 200));
            bubble.Arrange(new Rect(0, 0, 500, 200));

            var typing = Assert.IsType<TypingIndicator>(bubble.FindName("Typing"));
            Assert.Equal(Visibility.Visible, typing.Visibility);

            vm.RawMarkdown = "hello";
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

            Assert.False(vm.IsAwaitingFirstToken);
            Assert.Equal(Visibility.Collapsed, typing.Visibility);
        });
    }
}
