using System.Collections.ObjectModel;
using AIConsole.Data;
using AIConsole.Models;
using AIConsole.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIConsole.ViewModels;

public partial class ModelItemVm : ObservableObject
{
    public long RowId { get; init; }
    public string ModelId { get; init; } = "";
    [ObservableProperty] private bool _isEnabled;
    public bool Suppress;
    private readonly Action<ModelItemVm> _onChanged;
    public ModelItemVm(Action<ModelItemVm> onChanged) => _onChanged = onChanged;
    partial void OnIsEnabledChanged(bool value) { if (!Suppress) _onChanged(this); }
}

public partial class EndpointVm : ObservableObject
{
    public string Id { get; init; } = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _baseUrl = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private bool _showAllModels;
    public ObservableCollection<ModelItemVm> Models { get; } = new();
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _fetchStatus = "";

    // tri-state Select All
    [ObservableProperty] private bool? _allSelected;
    public void RefreshAllSelected()
    {
        if (Models.Count == 0) { AllSelected = false; return; }
        int on = Models.Count(m => m.IsEnabled);
        AllSelected = on == 0 ? false : on == Models.Count ? true : null;
    }
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly EndpointRepo _endpoints;
    private readonly SettingsRepo _settings;
    private readonly OpenAiClient _client;
    private readonly ModelRefreshService _refresh;
    private HotkeyService _hotkey;
    private readonly ChatViewModel _chat;

    public ObservableCollection<EndpointVm> Endpoints { get; } = new();
    public ObservableCollection<string> Themes { get; } = new(Services.ThemeManager.Available.Select(t => t.Name));
    public ObservableCollection<string> AccentSwatches { get; } = new(Services.ThemeManager.AccentSwatches);
    public ObservableCollection<string> CodeFonts { get; } = new() { "Cascadia Code", "Consolas", "Courier New", "Lucida Console" };

    private AppSettings _app;

    // ── appearance / behavior settings ──
    [ObservableProperty] private string _theme;
    [ObservableProperty] private string? _accentOverride;
    [ObservableProperty] private bool _followWindowsTheme;
    [ObservableProperty] private bool _showLineNumbers = true;
    [ObservableProperty] private string _codeFont = "Cascadia Code";
    [ObservableProperty] private bool _panelAcrylic = true;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _hideOnFocusLoss = false;
    [ObservableProperty] private string _slideEdge = "Top";
    [ObservableProperty] private string _hotkeyText = "Ctrl+`";
    [ObservableProperty] private int _refreshMinutes = 360;
    [ObservableProperty] private bool _alwaysOnTop = true;
    [ObservableProperty] private double _panelWidth;
    [ObservableProperty] private double _panelHeight = 600;
    [ObservableProperty] private double _panelOpacity = 1.0;
    [ObservableProperty] private bool _notifyOnCompletion = true;
    public ObservableCollection<string> SlideEdges { get; } = new() { "Top", "Left", "Right" };

    public event Action<string>? Status;

    [ObservableProperty] private string _statusText = "";
    private void Emit(string msg) { StatusText = msg; Status?.Invoke(msg); }

    // ── import from other tools ──
    public ObservableCollection<Services.ImportedEndpoint> Imports { get; } = new();
    private readonly Services.ConfigImporter _importer;

    public SettingsViewModel(EndpointRepo endpoints, SettingsRepo settings, OpenAiClient client,
        ModelRefreshService refresh, HotkeyService hotkey, ChatViewModel chat)
    {
        _endpoints = endpoints; _settings = settings; _client = client; _refresh = refresh; _hotkey = hotkey; _chat = chat;
        _importer = new Services.ConfigImporter(client);
        _app = _settings.Get(SettingKeys.Settings, new AppSettings());

        _theme = _app.Theme; _accentOverride = _app.AccentOverride; _followWindowsTheme = _app.FollowWindowsTheme;
        _showLineNumbers = _app.ShowLineNumbers; _codeFont = _app.CodeFont; _panelAcrylic = _app.PanelAcrylic;
        _startWithWindows = _app.StartWithWindows; _hideOnFocusLoss = _app.HideOnFocusLoss;
        _alwaysOnTop = _app.AlwaysOnTop; _panelWidth = _app.PanelWidth; _panelHeight = _app.PanelHeight; _notifyOnCompletion = _app.NotifyOnCompletion;
        _panelOpacity = Math.Clamp(_app.PanelOpacity, 0.3, 1.0);
        _slideEdge = _app.SlideEdge.ToString(); _hotkeyText = _app.Hotkey; _refreshMinutes = _app.RefreshMinutes;

        Reload();
    }

    // ── endpoints ──

    public void Reload()
    {
        Endpoints.Clear();
        foreach (var e in _endpoints.All())
        {
            var vm = new EndpointVm
            {
                Id = e.Id, Name = e.Name, BaseUrl = e.BaseUrl, ApiKey = e.ApiKey ?? "",
                ShowAllModels = e.ShowAllModels,
            };
            foreach (var m in _endpoints.ModelsFor(e.Id))
            {
                var item = new ModelItemVm(OnModelToggled) { RowId = m.Id, ModelId = m.ModelId, Suppress = true };
                item.IsEnabled = m.Enabled; item.Suppress = false;
                vm.Models.Add(item);
            }
            vm.RefreshAllSelected();
            Endpoints.Add(vm);
        }
    }

    [RelayCommand]
    public void AddEndpoint()
    {
        var vm = new EndpointVm { Id = Guid.NewGuid().ToString("N"), Name = "New endpoint", BaseUrl = "" };
        Endpoints.Add(vm);
    }

    [RelayCommand]
    public async Task SaveEndpointAsync(EndpointVm vm)
    {
        if (string.IsNullOrWhiteSpace(vm.Name)) vm.Name = "Endpoint";
        if (string.IsNullOrWhiteSpace(vm.BaseUrl)) { Emit("Base URL is required."); return; }

        var ep = new Endpoint
        {
            Id = vm.Id, Name = vm.Name.Trim(), BaseUrl = vm.BaseUrl.Trim(),
            ApiKey = string.IsNullOrWhiteSpace(vm.ApiKey) ? null : vm.ApiKey.Trim(),
            ShowAllModels = vm.ShowAllModels,
        };
        _endpoints.Upsert(ep);

        vm.IsBusy = true; vm.FetchStatus = "Fetching models…";
        try
        {
            var models = await _client.ListModelsAsync(ep.BaseUrl, ep.ApiKey);
            _endpoints.SyncModels(ep.Id, models, autoEnable: vm.ShowAllModels, prune: true);
            Reload();
            _refresh.NotifyModelsChanged();
            Emit($"Loaded {models.Count} model(s).");
        }
        catch (Exception ex)
        {
            Reload();
            Emit("Saved, but model fetch failed: " + ex.Message);
        }
        vm.IsBusy = false;
    }

    [RelayCommand]
    public async Task FetchModelsAsync(EndpointVm vm)
    {
        // persist first, then fetch
        await SaveEndpointAsync(vm);
    }

    [RelayCommand]
    public void DeleteEndpoint(EndpointVm vm)
    {
        _endpoints.Delete(vm.Id);
        Endpoints.Remove(vm);
        _refresh.NotifyModelsChanged();
    }

    public void AddManualModel(EndpointVm vm, string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return;
        _endpoints.AddManualModel(vm.Id, modelId.Trim());
        Reload();
        _refresh.NotifyModelsChanged();
    }

    [RelayCommand]
    public void SelectAll(EndpointVm vm)
    {
        bool target = !(vm.Models.Count > 0 && vm.Models.All(m => m.IsEnabled)); // all on → off; else → on
        foreach (var m in vm.Models) { m.Suppress = true; m.IsEnabled = target; m.Suppress = false; }
        _endpoints.SetAllEnabled(vm.Id, target);
        vm.RefreshAllSelected();
        _refresh.NotifyModelsChanged();
    }

    private void OnModelToggled(ModelItemVm item)
    {
        _endpoints.SetEnabled(item.RowId, item.IsEnabled);
        _refresh.NotifyModelsChanged();
    }

    // ── appearance ──

    partial void OnThemeChanged(string value) => ApplyAppearance();
    partial void OnAccentOverrideChanged(string? value) => ApplyAppearance();
    partial void OnFollowWindowsThemeChanged(bool value) => ApplyAppearance();

    partial void OnShowLineNumbersChanged(bool value)
    {
        _app.ShowLineNumbers = value; Markdown.MarkdownRenderer.ShowLineNumbers = value;  _chat.RerenderMarkdown();
    }

    partial void OnCodeFontChanged(string value)
    {
        _app.CodeFont = value; 
        try { System.Windows.Application.Current.Resources["FontMono"] = new System.Windows.Media.FontFamily(value + ", Cascadia Code, Consolas, Courier New"); } catch { }
    }
    partial void OnPanelAcrylicChanged(bool value) { _app.PanelAcrylic = value;  }
    partial void OnHideOnFocusLossChanged(bool value) { _app.HideOnFocusLoss = value;  }
    partial void OnSlideEdgeChanged(string value) { if (Enum.TryParse<SlideEdge>(value, out var e)) { _app.SlideEdge = e;  } }
    partial void OnRefreshMinutesChanged(int value) { _app.RefreshMinutes = Math.Clamp(value, 1, 10080);  }
    partial void OnAlwaysOnTopChanged(bool value) { _app.AlwaysOnTop = value; }
    partial void OnPanelWidthChanged(double value) { _app.PanelWidth = Math.Max(0, value); }
    partial void OnPanelHeightChanged(double value) { _app.PanelHeight = value <= 0 ? 600 : value; }
    partial void OnPanelOpacityChanged(double value) { _app.PanelOpacity = Math.Clamp(value, 0.3, 1.0); }
    partial void OnNotifyOnCompletionChanged(bool value) { _app.NotifyOnCompletion = value; }

    partial void OnStartWithWindowsChanged(bool value)
    {
        _app.StartWithWindows = value;  Services.StartupService.SetEnabled(value);
    }

    public void SetAccent(string? hex) => AccentOverride = hex;

    private void ApplyAppearance()
    {
        _app.Theme = Theme; _app.AccentOverride = AccentOverride; _app.FollowWindowsTheme = FollowWindowsTheme;
        Services.ThemeManager.Apply(FollowWindowsTheme ? null : Theme, AccentOverride, FollowWindowsTheme);
        
    }

    // ── import ──

    [RelayCommand]
    public void ScanImports()
    {
        Imports.Clear();
        foreach (var i in _importer.ScanKnownLocations()) Imports.Add(i);
        Emit(Imports.Count == 0 ? "No other tool configs found." : $"Found {Imports.Count} importable endpoint(s).");
    }

    [RelayCommand]
    public async Task DetectLocalServersAsync()
    {
        Imports.Clear();
        foreach (var i in await _importer.DetectLocalServersAsync()) Imports.Add(i);
        Emit(Imports.Count == 0 ? "No local servers detected." : $"Detected {Imports.Count} local server(s).");
    }

    [RelayCommand]
    public void ImportEndpoint(Services.ImportedEndpoint imp)
    {
        var ep = new Endpoint
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(imp.Name) ? "Imported" : imp.Name,
            BaseUrl = imp.BaseUrl,
            ApiKey = string.IsNullOrWhiteSpace(imp.ApiKey) ? null : imp.ApiKey,
            ShowAllModels = false,
        };
        _endpoints.Upsert(ep);
        _endpoints.SyncModels(ep.Id, imp.Models, autoEnable: true, prune: false);
        Reload();
        _refresh.NotifyModelsChanged();
        Emit($"Imported '{ep.Name}'.");
    }

    public void ImportFromFile(string path)
    {
        foreach (var i in _importer.ParseConfigFile(path)) ImportEndpoint(i);
    }

    public void SetHotkeyService(HotkeyService hk)
    {
        _hotkey = hk;
        _hotkey.RegistrationFailed += msg => Emit(msg);
    }

    // ── hotkey ──

    public void SetHotkey(Hotkey hk)
    {
        if (_hotkey.Register(hk))
        {
            _app.Hotkey = hk.ToString(); HotkeyText = hk.ToString(); 
        }
        else HotkeyText = _hotkey.Current?.ToString() ?? _app.Hotkey; // revert display
    }

    // ── Apply / Cancel with snapshot + theme revert ──
    private AppSettings _snapshot = new();
    public event Action? RequestClose;

    public void BeginEdit() { _snapshot = Clone(_app); }

    [RelayCommand]
    public void Apply()
    {
        Save();
        Services.StartupService.SetEnabled(_app.StartWithWindows);
        if (!string.IsNullOrWhiteSpace(_app.Hotkey)) _hotkey.Register(Hotkey.Parse(_app.Hotkey));
        Emit("Settings applied.");
        RequestClose?.Invoke();
    }

    [RelayCommand]
    public void Cancel()
    {
        _app = Clone(_snapshot);
        _settings.Set(SettingKeys.Settings, _app);
        Services.ThemeManager.Apply(_app.FollowWindowsTheme ? null : _app.Theme, _app.AccentOverride, _app.FollowWindowsTheme);
        Services.StartupService.SetEnabled(_app.StartWithWindows);
        _hotkey.Register(Hotkey.Parse(_app.Hotkey));
        RestoreFromApp();
        RequestClose?.Invoke();
    }

    private void RestoreFromApp()
    {
        Theme = _app.Theme; AccentOverride = _app.AccentOverride; FollowWindowsTheme = _app.FollowWindowsTheme;
        ShowLineNumbers = _app.ShowLineNumbers; CodeFont = _app.CodeFont; PanelAcrylic = _app.PanelAcrylic;
        StartWithWindows = _app.StartWithWindows; HideOnFocusLoss = _app.HideOnFocusLoss; SlideEdge = _app.SlideEdge.ToString();
        HotkeyText = _app.Hotkey; RefreshMinutes = _app.RefreshMinutes; AlwaysOnTop = _app.AlwaysOnTop;
        PanelWidth = _app.PanelWidth; PanelHeight = _app.PanelHeight; NotifyOnCompletion = _app.NotifyOnCompletion;
        PanelOpacity = Math.Clamp(_app.PanelOpacity, 0.3, 1.0);
        Markdown.MarkdownRenderer.ShowLineNumbers = _app.ShowLineNumbers;
        _chat.RerenderMarkdown();
    }

    private static AppSettings Clone(AppSettings a) => System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
        System.Text.Json.JsonSerializer.Serialize(a))!;

    private void Save()
    {
        var stored = _settings.Get(SettingKeys.Settings, new AppSettings());
        _app.LastModelKey = stored.LastModelKey; // don't clobber the chat-selected model
        _settings.Set(SettingKeys.Settings, _app);
    }

    public AppSettings App => _app;
}


