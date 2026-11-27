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
}
