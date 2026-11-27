using AIConsole.ViewModels;

namespace AIConsole.Tests;

public class ModelPickerViewModelTests
{
    private static ModelPickerViewModel Create() => new();

    private static ModelChoice[] Models() => new[]
    {
        new ModelChoice("a::gpt-4o", "gpt-4o", "Alpha"),
        new ModelChoice("a::gpt-4o-mini", "gpt-4o-mini", "Alpha"),
        new ModelChoice("b::llama-3", "llama-3", "Beta"),
        new ModelChoice("b::mistral", "mistral", "Beta"),
    };

    [Fact]
    public void Tabs_ContainAllThenDistinctProviders()
    {
        var vm = Create();
        vm.SetModels(Models());

        Assert.Equal(new[] { "All", "Alpha", "Beta" }, vm.Tabs.Select(t => t.Name));
        Assert.Equal(4, vm.Filtered.Count);
    }

    [Fact]
    public void ProviderTab_FiltersToOneEndpoint()
    {
        var vm = Create();
        vm.SetModels(Models());

        vm.SelectedTab = vm.Tabs.Single(t => t.Name == "Beta");

        Assert.Equal(new[] { "llama-3", "mistral" }, vm.Filtered.Select(m => m.ModelId));
    }

    [Fact]
    public void Search_MatchesModelAndEndpoint()
    {
        var vm = Create();
        vm.SetModels(Models());

        vm.SearchText = "GPT-4O-MINI";
        Assert.Equal(new[] { "gpt-4o-mini" }, vm.Filtered.Select(m => m.ModelId));

        vm.SearchText = "beta";
        Assert.Equal(2, vm.Filtered.Count);
        Assert.All(vm.Filtered, m => Assert.Equal("Beta", m.EndpointName));
    }

    [Fact]
    public void TabAndSearch_Combine()
    {
        var vm = Create();
        vm.SetModels(Models());
        vm.SelectedTab = vm.Tabs.Single(t => t.Name == "Alpha");

        vm.SearchText = "mini";

        Assert.Equal(new[] { "gpt-4o-mini" }, vm.Filtered.Select(m => m.ModelId));
    }

    [Fact]
    public void NoMatches_ReportEmptyState()
    {
        var vm = Create();
        vm.SetModels(Models());

        vm.SearchText = "does-not-exist";

        Assert.False(vm.HasResults);
        Assert.Empty(vm.Filtered);
    }
}
