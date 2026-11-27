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
        RootGrid.MouseEnter += (_, _) => ShowActions(true);
        RootGrid.MouseLeave += (_, _) => ShowActions(false);
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
        Footer.HorizontalAlignment = vm.IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

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
        UpdateContextStyle(vm);
        if (_hooked is not null) _hooked.PropertyChanged -= OnVmPropertyChanged;
        _hooked = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    private MessageViewModel? _hooked;

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not MessageViewModel vm) return;
        if (e.PropertyName is nameof(MessageViewModel.IsStreaming)) UpdateStreaming(vm.IsStreaming, vm.RawMarkdown);
        else if (e.PropertyName is nameof(MessageViewModel.IsOutOfContext)) UpdateContextStyle(vm);
    }

    private void UpdateStreaming(bool streaming, string text)
    {
        // The view model re-renders Markdown while text arrives, so the bubble is
        // always the live view. The plain text block remains only as a fallback.
        StreamingText.Visibility = Visibility.Collapsed;
        MarkdownHost.Visibility = Visibility.Visible;
    }

    private void UpdateContextStyle(MessageViewModel vm)
    {
        RootGrid.Opacity = vm.IsOutOfContext ? 0.45 : 1;
        RootGrid.ToolTip = vm.IsOutOfContext ? "This message is outside the outgoing context." : null;
    }

    private void Fade(UIElement el, double to)
    {
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)));
    }

    private void ShowActions(bool show)
    {
        if (show)
        {
            Actions.Visibility = Visibility.Visible;
            Fade(Actions, 1);
            return;
        }

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) => { if (!RootGrid.IsMouseOver) Actions.Visibility = Visibility.Collapsed; };
        Actions.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void AnimateScale(double to)
    {
        var dur = TimeSpan.FromMilliseconds(110);
        HoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, dur));
        HoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, dur));
    }

    private void Attachment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AttachmentViewModel attachment) return;
        var main = Window.GetWindow(this) as MainWindow;
        var hide = main?.HideOnFocusLoss ?? false;
        if (main is not null) main.HideOnFocusLoss = false;
        try
        {
            new AttachmentWindow(attachment) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
        finally
        {
            if (main is not null) main.HideOnFocusLoss = hide;
        }
    }
}
