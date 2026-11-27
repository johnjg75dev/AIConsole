using AIConsole.ViewModels;

namespace AIConsole.Tests;

public class MessageViewModelTests
{
    [Fact]
    public void AttachmentsLabel_RaisesPropertyChanged()
    {
        var vm = new MessageViewModel(isUser: true, "You");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.AttachmentsLabel = "📎 a.txt";

        Assert.Contains(nameof(MessageViewModel.AttachmentsLabel), raised);
        Assert.Equal("📎 a.txt", vm.AttachmentsLabel);
    }

    [Fact]
    public void AttachmentsLabel_DefaultsToEmpty()
    {
        var vm = new MessageViewModel(isUser: false, "bot");

        Assert.Equal("", vm.AttachmentsLabel);
    }

    [Fact]
    public void Edit_PrefillsTheEditorWithTheRawMessage()
    {
        var vm = Editable("Hello **world**");

        vm.EditCommand.Execute(null);

        Assert.True(vm.IsEditing);
        Assert.Equal("Hello **world**", vm.EditText);
    }

    [Fact]
    public void CancelEdit_HidesTheEditorWithoutSaving()
    {
        var vm = Editable("original");
        vm.EditCommand.Execute(null);
        vm.EditText = "changed";

        vm.CancelEditCommand.Execute(null);

        Assert.False(vm.IsEditing);
        Assert.Equal("original", vm.RawMarkdown);
    }

    [Fact]
    public void SaveEdit_RaisesEditRequested()
    {
        var vm = Editable("original");
        vm.EditCommand.Execute(null);
        vm.EditText = "changed";
        MessageViewModel? requested = null;
        vm.EditRequested += message => requested = message;

        vm.SaveEditCommand.Execute(null);

        Assert.Same(vm, requested);
    }

    [Fact]
    public void FirstToken_ClearsAwaitingAndRendersImmediately()
    {
        TestThread.RunSta(() =>
        {
            var vm = new MessageViewModel(isUser: false, "bot")
            {
                IsStreaming = true,
                IsAwaitingFirstToken = true,
            };

            vm.RawMarkdown = "hello";

            Assert.False(vm.IsAwaitingFirstToken);
            Assert.NotNull(vm.Rendered);
        });
    }

    [Fact]
    public void EmptyReply_StopsAwaitingWhenStreamingEnds()
    {
        TestThread.RunSta(() =>
        {
            var vm = new MessageViewModel(isUser: false, "bot")
            {
                IsStreaming = true,
                IsAwaitingFirstToken = true,
            };

            vm.IsStreaming = false;

            Assert.False(vm.IsAwaitingFirstToken);
        });
    }

    private static MessageViewModel Editable(string content) =>
        new(isUser: true, "You") { Id = 7, RawMarkdown = content };
}
