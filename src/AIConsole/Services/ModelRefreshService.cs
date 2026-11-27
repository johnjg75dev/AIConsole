using AIConsole.Data;

namespace AIConsole.Services;

/// <summary>Background re-sync of model lists for endpoints with "always show all models" enabled.</summary>
public sealed class ModelRefreshService : IDisposable
{
    private readonly EndpointRepo _endpoints;
    private readonly OpenAiClient _client;
    private readonly SettingsRepo _settings;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event Action? ModelsChanged;

    public void NotifyModelsChanged() => ModelsChanged?.Invoke();

    public ModelRefreshService(EndpointRepo endpoints, OpenAiClient client, SettingsRepo settings)
    {
        _endpoints = endpoints; _client = client; _settings = settings;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public int IntervalMinutes =>
        Math.Clamp(_settings.Get(SettingKeys.Settings, new Models.AppSettings()).RefreshMinutes, 1, 10080);

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await RefreshAllAsync(ct).ConfigureAwait(false); // initial sync on startup
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(IntervalMinutes));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await RefreshAllAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task RefreshAllAsync(CancellationToken ct = default)
    {
        foreach (var ep in _endpoints.All())
        {
            if (!ep.ShowAllModels) continue;
            try
            {
                var models = await _client.ListModelsAsync(ep.BaseUrl, ep.ApiKey, ct).ConfigureAwait(false);
                _endpoints.SyncModels(ep.Id, models, autoEnable: true, prune: true);
            }
            catch { /* keep last known list; retry next tick */ }
        }
        ModelsChanged?.Invoke();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts?.Dispose();
    }
}
