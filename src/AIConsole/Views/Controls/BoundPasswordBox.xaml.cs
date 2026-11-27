using System.Windows;
using System.Windows.Controls;

namespace AIConsole.Views.Controls;

/// <summary>
/// PasswordBox wrapper with a bindable password. The inner box always listens for
/// PasswordChanged, so empty initial values still propagate typed keys.
/// </summary>
public partial class BoundPasswordBox : UserControl
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.Register(
        nameof(BoundPassword), typeof(string), typeof(BoundPasswordBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    private bool _updating;

    public BoundPasswordBox()
    {
        InitializeComponent();
    }

    public string BoundPassword
    {
        get => (string)GetValue(BoundPasswordProperty);
        set => SetValue(BoundPasswordProperty, value);
    }

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not BoundPasswordBox box || box._updating) return;
        box._updating = true;
        try { box.Inner.Password = e.NewValue as string ?? ""; }
        finally { box._updating = false; }
    }

    private void Inner_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _updating = true;
        try { SetCurrentValue(BoundPasswordProperty, Inner.Password); }
        finally { _updating = false; }
    }
}
