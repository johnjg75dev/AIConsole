using System.Windows;
using System.Windows.Interop;
using AIConsole.Data;
using AIConsole.Models;
using AIConsole.Services;
using AIConsole.ViewModels;
using AIConsole.Views;

namespace AIConsole;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    private Db _db = null!;
    private ChatRepo _chatRepo = null!;
    private EndpointRepo _endpointRepo = null!;
    private SettingsRepo _settingsRepo = null!;
    private OpenAiClient _client = null!;
    private ModelRefreshService _refresh = null!;
    private ChatViewModel _chatVm = null!;
    private SettingsViewModel _settingsVm = null!;

    private MainWindow _window = null!;
    private ChatView _chatView = null!;
    private SettingsView _settingsView = null!;
    private TrayService _tray = null!;
    private HotkeyService _hotkey = null!;
    private AppSettings _appSettings = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // single instance
        _singleInstance = new Mutex(true, "AIConsole.SingleInstance", out bool createdNew);
        if (!createdNew) { Shutdown(); return; }

        // data layer
        _db = new Db();
        _chatRepo = new ChatRepo(_db);
        _endpointRepo = new EndpointRepo(_db);
        _settingsRepo = new SettingsRepo(_db);
        _appSettings = _settingsRepo.Get(SettingKeys.Settings, new AppSettings());

        _client = new OpenAiClient();
        _refresh = new ModelRefreshService(_endpointRepo, _client, _settingsRepo);

        // theme first, so everything renders correctly
        ThemeManager.Apply(_appSettings.Theme, _appSettings.AccentOverride, _appSettings.FollowWindowsTheme);
        Markdown.MarkdownRenderer.ShowLineNumbers = _appSettings.ShowLineNumbers;

        // view models
        _chatVm = new ChatViewModel(_chatRepo, _endpointRepo, _settingsRepo, _client, _refresh);
        _chatView = new ChatView { DataContext = _chatVm };

        // window
        _window = new MainWindow
        {
            HideOnFocusLoss = _appSettings.HideOnFocusLoss,
        };
        _window.SetSlideEdge(_appSettings.SlideEdge);
        _window.SetAcrylic(_appSettings.PanelAcrylic);
        _window.SetPanelOpacity(_appSettings.PanelOpacity);

        _settingsVm = new SettingsViewModel(_endpointRepo, _settingsRepo, _client, _refresh, null!, _chatVm);
        _settingsView = new SettingsView { DataContext = _settingsVm };
        _window.AttachContent(_chatView, _settingsView);

        // hotkey (needs HWND)
        var hwnd = _window.Handle;
        var source = (HwndSource)HwndSource.FromHwnd(hwnd)!;
        _hotkey = new HotkeyService(hwnd, source, () => _window.TogglePanel());
        _settingsVm.SetHotkeyService(_hotkey);

        var hk = Hotkey.Parse(_appSettings.Hotkey);
        _hotkey.Register(hk);
        _hotkey.RegistrationFailed += msg => { /* surfaced in Settings UI; keep panel usable */ };

        _window.SetShowingSettings(false);

        // tray
        _tray = new TrayService();
        _tray.ToggleRequested += () => _window.TogglePanel();
        _tray.NewChatRequested += () => { _chatVm.NewChat(); _window.SetShowingSettings(false); _window.ShowPanel(); };
        _tray.SettingsRequested += () => { _window.SetShowingSettings(true); _window.ShowPanel(); };
        _tray.QuitRequested += Quit;

        // windows notification when a reply completes
        _chatVm.MessageCompleted += (model, preview) =>
        {
            _appSettings = _settingsVm.App;
            if (_appSettings.NotifyOnCompletion) _tray.ShowToast("AIConsole - " + model, preview);
        };

        // settings apply/cancel + opening settings starts edit session
        _settingsVm.RequestClose += () => _window.SetShowingSettings(false);
        _window.ToggleSettings += () => { if (_window.ShowingSettings) _settingsVm.BeginEdit(); };

        // sizing + always-on-top
        _window.SetAlwaysOnTop(_appSettings.AlwaysOnTop);
        _window.SetPanelSize(_appSettings.PanelWidth, _appSettings.PanelHeight);
        _window.SetPanelOffset(_appSettings.PanelOffsetX, _appSettings.PanelOffsetY);

        // react to appearance changes
        ThemeManager.ThemeChanged += _ => { };

        // live-apply window-related settings saved from Settings UI
        _settingsVm.PropertyChanged += (_, ev) =>
        {
            _appSettings = _settingsVm.App;
            switch (ev.PropertyName)
            {
                case nameof(SettingsViewModel.SlideEdge):
                    _window.SetSlideEdge(_appSettings.SlideEdge); break;
                case nameof(SettingsViewModel.PanelAcrylic):
                    _window.SetAcrylic(_appSettings.PanelAcrylic); break;
                case nameof(SettingsViewModel.HideOnFocusLoss):
                    _window.HideOnFocusLoss = _appSettings.HideOnFocusLoss; break;
                case nameof(SettingsViewModel.AlwaysOnTop):
                    _window.SetAlwaysOnTop(_appSettings.AlwaysOnTop); break;
                case nameof(SettingsViewModel.PanelWidth):
                case nameof(SettingsViewModel.PanelHeight):
                    _window.SetPanelSize(_appSettings.PanelWidth, _appSettings.PanelHeight); break;
                case nameof(SettingsViewModel.PanelOpacity):
                    _window.SetPanelOpacity(_appSettings.PanelOpacity); break;
                case nameof(SettingsViewModel.PanelOffsetX):
                case nameof(SettingsViewModel.PanelOffsetY):
                    _window.SetPanelOffset(_appSettings.PanelOffsetX, _appSettings.PanelOffsetY); break;
            }
        };

        // background model refresh
        _refresh.Start();

        // start hidden in tray
    }

    private void Quit()
    {
        _tray?.Dispose();
        _hotkey?.Dispose();
        _refresh?.Dispose();
        _client?.Dispose();
        _db?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstance?.ReleaseMutex(); _singleInstance?.Dispose(); } catch { }
        base.OnExit(e);
    }
}
