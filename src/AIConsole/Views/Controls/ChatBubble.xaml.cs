using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AIConsole.ViewModels;

namespace AIConsole.Views.Controls;

public partial class ChatBubble : UserControl
{
    public ChatBubble()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ApplyStyle();
        RootGrid.MouseEnter += (_, _) => Fade(Actions, 1);
        RootGrid.MouseLeave += (_, _) => Fade(Actions, 0);
        Bubble.MouseEnter += (_, _) => AnimateScale(1.008);
        Bubble.MouseLeave += (_, _) => AnimateScale(1.0);
        Loaded += (_, _) => ApplyStyle();
        Unloaded += (_, _) => { if (_hooked is not null) _hooked.PropertyChanged -= OnVmPropertyChanged; _hooked = null; };
    }

    private void ApplyStyle()
    {
        if (DataContext is not MessageViewModel vm) return;

        // participant-side styling
        Bubble.HorizontalAlignment = vm.IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        if (vm.IsUser)
        {
            Bubble.SetResourceReference(Border.BackgroundProperty, "BubbleUserBrush");
            Bubble.SetResourceReference(Border.BorderBrushProperty, "AccentDimBrush");
            Bubble.SetResourceReference(TextElement.ForegroundProperty, "BubbleUserTextBrush");
            StreamingText.SetResourceReference(TextBlock.ForegroundProperty, "BubbleUserTextBrush");
        }
        else
        {
            Bubble.SetResourceReference(Border.BackgroundProperty, "BubbleAssistantBrush");
            Bubble.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            Bubble.SetResourceReference(TextElement.ForegroundProperty, "BubbleAssistantTextBrush");
            StreamingText.SetResourceReference(TextBlock.ForegroundProperty, "BubbleAssistantTextBrush");
        }
        Bubble.BorderThickness = new Thickness(1);
        if (Bubble.ContextMenu is not null) Bubble.ContextMenu.DataContext = vm;

        UpdateStreaming(vm.IsStreaming, vm.RawMarkdown);
        if (_hooked is not null) _hooked.PropertyChanged -= OnVmPropertyChanged;
        _hooked = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    private MessageViewModel? _hooked;

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not MessageViewModel vm) return;
        if (e.PropertyName is nameof(MessageViewModel.IsStreaming)) UpdateStreaming(vm.IsStreaming, vm.RawMarkdown);
        else if (e.PropertyName is nameof(MessageViewModel.RawMarkdown)) StreamingText.Text = vm.RawMarkdown;
    }

    private void UpdateStreaming(bool streaming, string text)
    {
        StreamingText.Text = text;
        StreamingText.Visibility = streaming ? Visibility.Visible : Visibility.Collapsed;
        MarkdownHost.Visibility = streaming ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Fade(UIElement el, double to)
    {
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
    }

    private void AnimateScale(double to)
    {
        var dur = TimeSpan.FromMilliseconds(110);
        HoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, dur));
        HoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, dur));
    }
}
