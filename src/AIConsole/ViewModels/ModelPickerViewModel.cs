using System.Collections.ObjectModel;
using AIConsole.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIConsole.ViewModels;

public sealed record ModelTab(string Name, string? Endpoint, int Count)
{
    public string Display => Count > 0 ? $"{Name} ({Count})" : Name;
}

/// <summary>Filtering state for the model picker popup.</summary>
public partial class ModelPickerViewModel : ObservableObject
{
    private readonly List<ModelChoice> _all = new();
    private bool _rebuilding;

    public ObservableCollection<ModelTab> Tabs { get; } = new();
    public ObservableCollection<ModelChoice> Filtered { get; } = new();

    [ObservableProperty] private ModelTab? _selectedTab;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _hasResults;

    public bool IsEmpty => _all.Count == 0;

    public void SetModels(IEnumerable<ModelChoice> models)
    {
        var keepEndpoint = SelectedTab?.Endpoint;
        _all.Clear();
        _all.AddRange(models);

        _rebuilding = true;
        try
        {
            Tabs.Clear();
            Tabs.Add(new ModelTab("All", null, _all.Count));
            foreach (var endpoint in _all
                         .Select(m => m.EndpointName)
                         .Where(n => !string.IsNullOrWhiteSpace(n))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                Tabs.Add(new ModelTab(endpoint, endpoint, _all.Count(m => string.Equals(m.EndpointName, endpoint, StringComparison.OrdinalIgnoreCase))));

            SelectedTab = Tabs.FirstOrDefault(t => keepEndpoint is not null && string.Equals(t.Endpoint, keepEndpoint, StringComparison.OrdinalIgnoreCase))
                          ?? Tabs.FirstOrDefault();
        }
        finally { _rebuilding = false; }

        ApplyFilter();
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void ResetForOpen() => SearchText = "";

    partial void OnSelectedTabChanged(ModelTab? value)
    {
        if (!_rebuilding) ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<ModelChoice> query = _all;
        if (SelectedTab?.Endpoint is { } endpoint)
            query = query.Where(m => string.Equals(m.EndpointName, endpoint, StringComparison.OrdinalIgnoreCase));

        var search = SearchText.Trim();
        if (search.Length > 0)
            query = query.Where(m =>
                m.ModelId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                m.EndpointName.Contains(search, StringComparison.OrdinalIgnoreCase));

        Filtered.Clear();
        foreach (var model in query) Filtered.Add(model);
        HasResults = Filtered.Count > 0;
    }
}
