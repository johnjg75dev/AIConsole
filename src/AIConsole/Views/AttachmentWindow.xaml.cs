using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AIConsole.ViewModels;
using Microsoft.Win32;

namespace AIConsole.Views;

public partial class AttachmentWindow : Window
{
    private readonly AttachmentViewModel _attachment;

    public AttachmentWindow(AttachmentViewModel attachment)
    {
        _attachment = attachment;
        InitializeComponent();

        Title = attachment.Name;
        NameText.Text = attachment.Name;
        DetailsText.Text = attachment.Details;

        if (attachment.HasPreview)
        {
            ImageView.Source = attachment.Preview;
            ImageScroller.Visibility = Visibility.Visible;
        }
        else if (!attachment.IsImage)
        {
            TextView.Text = attachment.TextContent;
            TextView.Visibility = Visibility.Visible;
        }
        else
        {
            UnavailableText.Visibility = Visibility.Visible;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = SafeFileName(_attachment.Name),
            Filter = "All files|*.*",
            Title = "Save attachment",
        };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllBytes(dialog.FileName, _attachment.Attachment.Data); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save attachment", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "AIConsole", "attachments", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, SafeFileName(_attachment.Name));
            File.WriteAllBytes(path, _attachment.Attachment.Data);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not open attachment", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SafeFileName(string name)
    {
        var file = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(file)) file = "attachment";
        foreach (var c in Path.GetInvalidFileNameChars()) file = file.Replace(c, '_');
        return file;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
