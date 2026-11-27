using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIConsole.ViewModels;

namespace AIConsole.Views.Controls;

public partial class ModelPicker : UserControl
{
    private readonly ModelPickerViewModel _vm = new();
    private INotifyCollectionChanged? _observed;

    public ModelPicker()
    {
        InitializeComponent();
        // Keep the UserControl's DataContext available for the ItemsSource and
        // SelectedItem bindings; only the inner picker UI uses the picker VM.
        Root.DataContext = _vm;
        Loaded += (_, _) => UpdateSelectedDisplay();
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable<ModelChoice>), typeof(ModelPicker),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(ModelChoice), typeof(ModelPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    public IEnumerable<ModelChoice>? ItemsSource
    {
        get => (IEnumerable<ModelChoice>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ModelChoice? SelectedItem
    {
        get => (ModelChoice?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ModelPicker picker) return;

        if (picker._observed is not null)
            picker._observed.CollectionChanged -= picker.OnModelsCollectionChanged;

        picker._observed = e.NewValue as INotifyCollectionChanged;
        if (picker._observed is not null)
            picker._observed.CollectionChanged += picker.OnModelsCollectionChanged;

        picker.RefreshModels();
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ModelPicker picker) picker.UpdateSelectedDisplay();
    }

    private void OnModelsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshModels();

    private void RefreshModels() => _vm.SetModels(ItemsSource ?? Array.Empty<ModelChoice>());

    private void UpdateSelectedDisplay()
    {
        if (SelectedModelText is null) return;
        SelectedModelText.Text = SelectedItem?.ModelId ?? "Select a model";
        var endpoint = SelectedItem?.EndpointName ?? "";
        SelectedEndpointText.Text = endpoint;
        SelectedEndpointText.Visibility = string.IsNullOrWhiteSpace(endpoint) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        _vm.ResetForOpen();
        Dropdown.IsOpen = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }));
    }

    private void CloseDropdown()
    {
        Dropdown.IsOpen = false;
        OpenButton.Focus();
    }

    private void Select(ModelChoice? choice)
    {
        if (choice is null) return;
        SetCurrentValue(SelectedItemProperty, choice);
        UpdateSelectedDisplay();
        CloseDropdown();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CloseDropdown(); return; }

        if (e.Key == Key.Down)
        {
            if (_vm.Filtered.Count == 0) return;
            ModelList.SelectedIndex = 0;
            ModelList.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            Select(ModelList.SelectedItem as ModelChoice ?? _vm.Filtered.FirstOrDefault());
            e.Handled = true;
        }
    }

    private void ModelList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CloseDropdown(); return; }
        if (e.Key == Key.Enter)
        {
            Select(ModelList.SelectedItem as ModelChoice);
            e.Handled = true;
        }
    }

    private void ModelList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { } item &&
            item.DataContext is ModelChoice choice)
        {
            Select(choice);
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}
