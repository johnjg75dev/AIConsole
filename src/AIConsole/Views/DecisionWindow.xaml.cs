using System.Windows;
using System.Windows.Controls;
using AIConsole.ViewModels;
using AIConsole.Models;

namespace AIConsole.Views;

public partial class DecisionWindow : Window
{
    private readonly ChatViewModel _viewModel;
    private readonly CancellationTokenSource _cancel = new();

    public DecisionWindow(ChatViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        UpdateCriteria();
        Closed += (_, _) => _cancel.Cancel();
    }

    private void Type_Changed(object sender, SelectionChangedEventArgs e) => UpdateCriteria();

    private void UpdateCriteria()
    {
        if (CriteriaBox is null || CriteriaHint is null || TypeBox is null) return;
        var type = (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        CriteriaBox.IsEnabled = type != "noul";
        CriteriaHint.Text = type switch
        {
            "choice" => "Options: one per line, optionally name = description.",
            "score" => "Score levels: one description per line, ordered from lowest to highest (2–10).",
            _ => "Noul evaluates a yes/no proposition; no options are needed."
        };
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var state = StateBox.Text.Trim();
        var task = TaskBox.Text.Trim();
        var type = (TypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "choice";
        var criteria = CriteriaBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (state.Length == 0 || task.Length == 0) { StatusText.Text = "Enter a state and question."; return; }
        if ((type == "choice" && (criteria.Length < 2 || criteria.Length > 255)) || (type == "score" && (criteria.Length < 2 || criteria.Length > 10)))
        { StatusText.Text = "Provide 2–255 choice options or 2–10 score levels."; return; }
        RunButton.IsEnabled = false;
        ResultPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = "Running…";
        try
        {
            var response = await _viewModel.DecideAsync(state, type, task, criteria, _cancel.Token);
            var result = DecisionResult.Parse(response);
            ResultPanel.DataContext = result;
            ResultPanel.Visibility = Visibility.Visible;
            StatusText.Text = result.Notice;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { RunButton.IsEnabled = true; }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
