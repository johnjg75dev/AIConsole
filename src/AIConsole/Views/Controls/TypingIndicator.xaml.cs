using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace AIConsole.Views.Controls;

public partial class TypingIndicator : UserControl
{
    private Storyboard? _running;

    public TypingIndicator()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => StopAnimation();
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        // Collapsed bubbles should not keep an animation clock running.
        if (!IsLoaded || !IsVisible || !SystemParameters.ClientAreaAnimation)
        {
            StopAnimation();
            return;
        }

        if (_running is not null) return;
        _running = ((Storyboard)FindResource("Bounce")).Clone();
        _running.Begin(this, true);
    }

    private void StopAnimation()
    {
        if (_running is null) return;
        _running.Stop(this);
        _running = null;
    }
}
