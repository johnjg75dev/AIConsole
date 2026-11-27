using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Data;
using AIConsole.Models;
using AIConsole.ViewModels;
using AIConsole.Views.Controls;

namespace AIConsole.Tests;

public class ModelPickerTests
{
    [Fact]
    public void InheritedDataContext_FeedsItemsSourceAndSelection()
    {
        TestThread.RunSta(() =>
        {
            var host = new Host();
            host.Models.Add(new ModelChoice("a::m1", "m1", "Alpha"));
            host.Models.Add(new ModelChoice("b::m2", "m2", "Beta"));
            host.SelectedModel = host.Models[0];

            var picker = new ModelPicker();
            _ = new ContentControl { Content = picker, DataContext = host };

            BindingOperations.SetBinding(picker, ModelPicker.ItemsSourceProperty,
                new Binding(nameof(Host.Models)));
            BindingOperations.SetBinding(picker, ModelPicker.SelectedItemProperty,
                new Binding(nameof(Host.SelectedModel)) { Mode = BindingMode.TwoWay });

            Assert.Same(host, picker.DataContext);
            Assert.Same(host.Models, picker.ItemsSource);
            Assert.Equal(host.Models[0], picker.SelectedItem);

            var label = (TextBlock)picker.FindName("SelectedModelText");
            Assert.Equal("m1", label.Text);
        });
    }

    private sealed class Host
    {
        public ObservableCollection<ModelChoice> Models { get; } = new();
        public ModelChoice? SelectedModel { get; set; }
    }
}
