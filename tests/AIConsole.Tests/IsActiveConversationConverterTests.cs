using System.Globalization;
using System.Windows;
using AIConsole.Converters;
using AIConsole.Models;

namespace AIConsole.Tests;

public class IsActiveConversationConverterTests
{
    [Fact]
    public void SelectedConversation_MatchesByReference()
    {
        var selected = new Conversation { Id = "a" };

        Assert.True((bool)Convert(selected, selected));
    }

    [Fact]
    public void SelectedConversation_MatchesById()
    {
        var selected = new Conversation { Id = "a" };
        var copy = new Conversation { Id = "a" };

        Assert.True((bool)Convert(selected, copy));
    }

    [Fact]
    public void DifferentConversation_IsNotActive()
    {
        Assert.False((bool)Convert(new Conversation { Id = "a" }, new Conversation { Id = "b" }));
    }

    [Fact]
    public void NoSelection_IsNotActive()
    {
        Assert.False((bool)Convert(null, new Conversation { Id = "a" }));
    }

    [Fact]
    public void VisibilityTarget_UsesVisibleAndCollapsed()
    {
        var selected = new Conversation { Id = "a" };

        Assert.Equal(Visibility.Visible, (Visibility)ConvertVisibility(selected, selected));
        Assert.Equal(Visibility.Collapsed, (Visibility)ConvertVisibility(selected, new Conversation { Id = "b" }));
    }

    private static object Convert(Conversation? selected, Conversation? item) =>
        IsActiveConversationConverter.Instance.Convert(new object?[] { selected, item }!,
            typeof(bool), null!, CultureInfo.InvariantCulture);

    private static object ConvertVisibility(Conversation? selected, Conversation? item) =>
        IsActiveConversationConverter.Instance.Convert(new object?[] { selected, item }!,
            typeof(Visibility), null!, CultureInfo.InvariantCulture);
}
