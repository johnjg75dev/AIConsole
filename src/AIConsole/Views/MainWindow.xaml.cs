using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AIConsole.Models;
using AIConsole.Services;

namespace AIConsole.Views;

public partial class MainWindow : Window
{
    // ── acrylic backdrop ──
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_TRANSIENTWINDOW = 3; // acrylic
    private const int DWMSBT_MAINWINDOW = 2;      // mica
    private const int DWMSBT_NONE = 1;

    private readonly TranslateTransform _slide;
    private SlideEdge _edge = SlideEdge.Top;
    private double _customWidth;
    private double _panelHeight = 600;
    private bool _alwaysOnTop = true;
    private bool _acrylic = true;
    private bool _visible;
    private HwndSource? _source;

    public Func<AppSettings>? SettingsProvider { get; set; }
    public bool HideOnFocusLoss { get; set; } = true;
    public event Action? ToggleSettings;

    public MainWindow()
    {
        InitializeComponent();
        _slide = (TranslateTransform)RootGrid.RenderTransform;
        Deactivated += OnDeactivated;
        KeyDown += OnKeyDown;
        SourceInitialized += (_, _) => ApplyAcrylic();
    }

    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();
    public HwndSource HwndSource => _source ??= (HwndSource)PresentationSource.FromVisual(this)!;

    public void AttachContent(FrameworkElement chat, FrameworkElement settings)
    {
        ChatHost.Children.Add(chat);
        SettingsHost.Children.Add(settings);
    }

    public void SetSlideEdge(SlideEdge edge) => _edge = edge;
    public void SetAcrylic(bool on) { _acrylic = on; ApplyAcrylic(); }
    public void SetAlwaysOnTop(bool on) { _alwaysOnTop = on; Topmost = on; }
    public void SetPanelSize(double width, double height)
    {
        _customWidth = width; _panelHeight = height <= 0 ? 600 : height; ApplySize();
    }

    private void ApplyAcrylic()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int dark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int backdrop = _acrylic ? DWMSBT_TRANSIENTWINDOW : DWMSBT_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }
        catch { /* older Windows: solid surface */ }
    }

    // ── show / hide with slide animation ──

    public void ShowPanel()
    {
        _visible = true;
        PositionWindow();
        Visibility = Visibility.Visible;
        Show();
        Activate();
        Topmost = _alwaysOnTop;

        var from = _edge switch
        {
            SlideEdge.Top => -(Height + 24),
            SlideEdge.Left => -(Width + 24),
            SlideEdge.Right => Width + 24,
            _ => -(Height + 24),
        };
        if (_edge == SlideEdge.Top)
        {
            _slide.X = 0; _slide.Y = from;
        }
        else
        {
            _slide.Y = 0; _slide.X = from;
        }

        var anim = new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        var target = _edge == SlideEdge.Top
            ? (DependencyProperty)TranslateTransform.YProperty
            : TranslateTransform.XProperty;
        _slide.BeginAnimation(target, anim);
        // reset opposite axis
        _slide.BeginAnimation(_edge == SlideEdge.Top ? TranslateTransform.XProperty : TranslateTransform.YProperty, null);

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() => _ = FocusFirst()));
    }

    private bool FocusFirst()
    {
        // focus composer if chat is showing
        var box = FindVisualChild<TextBox>(ChatHost);
        box?.Focus();
        return true;
    }

    public void HidePanel()
    {
        if (!_visible) return;
        _visible = false;

        var to = _edge switch
        {
            SlideEdge.Top => -(Height + 24),
            SlideEdge.Left => -(Width + 24),
            SlideEdge.Right => Width + 24,
            _ => -(Height + 24),
        };
        var target = _edge == SlideEdge.Top
            ? (DependencyProperty)TranslateTransform.YProperty
            : TranslateTransform.XProperty;

        var anim = new DoubleAnimation(0, to, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        anim.Completed += (_, _) => { if (!_visible) Hide(); };
        _slide.BeginAnimation(target, anim);
    }

    public void TogglePanel() { if (_visible) HidePanel(); else ShowPanel(); }

    private void ApplySize()
    {
        var work = SystemParameters.WorkArea;
        Height = _panelHeight;
        if (_edge == SlideEdge.Top)
            Width = _customWidth > 0 ? _customWidth : work.Width;   // full screen width by default
        else Width = _customWidth > 0 ? _customWidth : 880;
    }

    private void PositionWindow()
    {
        ApplySize();
        var work = SystemParameters.WorkArea;
        switch (_edge)
        {
            case SlideEdge.Top:
                Left = _customWidth > 0 ? work.Left + (work.Width - Width) / 2 : work.Left;
                Top = work.Top;
                break;
            case SlideEdge.Left:
                Left = work.Left;
                Top = work.Top + (work.Height - Height) / 2;
                break;
            case SlideEdge.Right:
                Left = work.Right - Width;
                Top = work.Top + (work.Height - Height) / 2;
                break;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (HideOnFocusLoss && _visible) HidePanel();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { HidePanel(); e.Handled = true; }
    }

    // ── chrome ──

    private void DragHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    public bool ShowingSettings => SettingsHost.Visibility == Visibility.Visible;

    private void Settings_Click(object sender, RoutedEventArgs e) => SetShowingSettings(!ShowingSettings);
    private void Close_Click(object sender, RoutedEventArgs e) => HidePanel();

    public void SetShowingSettings(bool show)
    {
        SettingsHost.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ChatHost.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        ToggleSettings?.Invoke();
    }

    public static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            if (FindVisualChild<T>(child) is { } found) return found;
        }
        return null;
    }
}
