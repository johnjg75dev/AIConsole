using System.Text;
using AIConsole.Models;
using AIConsole.ViewModels;

namespace AIConsole.Tests;

public class AttachmentViewModelTests
{
    [Fact]
    public void TextAttachment_IsReadableAndDescribed()
    {
        var viewModel = new AttachmentViewModel(
            new ChatAttachment("notes.txt", "text/plain", Encoding.UTF8.GetBytes("hello")));

        Assert.Equal("notes.txt", viewModel.Name);
        Assert.False(viewModel.IsImage);
        Assert.False(viewModel.HasPreview);
        Assert.Equal("hello", viewModel.TextContent);
        Assert.Contains("Text", viewModel.Details);
    }

    [Fact]
    public void MessageSetAttachments_PopulatesCollectionAndLabel()
    {
        var vm = new MessageViewModel(isUser: true, "You");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SetAttachments(new[]
        {
            new ChatAttachment("a.txt", "text/plain", Encoding.UTF8.GetBytes("A")),
            new ChatAttachment("b.txt", "text/plain", Encoding.UTF8.GetBytes("B")),
        });

        Assert.True(vm.HasAttachments);
        Assert.Equal(new[] { "a.txt", "b.txt" }, vm.Attachments.Select(a => a.Name));
        Assert.Contains("📎 a.txt", vm.AttachmentsLabel);
        Assert.Contains(nameof(MessageViewModel.HasAttachments), raised);
    }
}
