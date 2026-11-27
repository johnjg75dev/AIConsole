using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace AIConsole.Services;

public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _tray;

    public event Action? ToggleRequested;
    public event Action? NewChatRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    public TrayService()
    {
        _tray = new TaskbarIcon
        {
            ToolTipText = "AIConsole",
            Icon = LoadIcon(),
        };
        _tray.TrayLeftMouseUp += (_, _) => ToggleRequested?.Invoke();
        _tray.TrayMouseDoubleClick += (_, _) => ToggleRequested?.Invoke();

        var menu = new ContextMenu();

        var toggle = new MenuItem { Header = "Show / hide  (Ctrl+`)" };
        toggle.Click += (_, _) => ToggleRequested?.Invoke();
        menu.Items.Add(toggle);

        var newChat = new MenuItem { Header = "New chat" };
        newChat.Click += (_, _) => NewChatRequested?.Invoke();
        menu.Items.Add(newChat);

        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        menu.Items.Add(settings);

        menu.Items.Add(new Separator());

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => QuitRequested?.Invoke();
        menu.Items.Add(quit);

        _tray.ContextMenu = menu;
    }

    private static Icon? LoadIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/aiconsole.ico");
            var info = Application.GetResourceStream(uri);
            if (info is not null) return new Icon(info.Stream);
        }
        catch { }
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "aiconsole.ico");
            if (File.Exists(path)) return new Icon(path);
        }
        catch { }
        return SystemIcons.Application;
    }

    public void ShowToast(string title, string message)
    {
        try { _tray.ShowBalloonTip(title, message, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info); } catch { }
    }

    public void Dispose() => _tray.Dispose();
}
