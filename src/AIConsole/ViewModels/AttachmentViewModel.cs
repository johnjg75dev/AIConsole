using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using AIConsole.Models;

namespace AIConsole.ViewModels;

/// <summary>A persisted attachment surfaced in the chat transcript and preview window.</summary>
public sealed class AttachmentViewModel
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public ChatAttachment Attachment { get; }

    public AttachmentViewModel(ChatAttachment attachment)
    {
        Attachment = attachment;
        if (IsImage)
        {
            try
            {
                using var stream = new MemoryStream(attachment.Data, writable: false);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                Preview = image;
            }
            catch
            {
                // Corrupt image data still gets a named chip; the viewer can offer the raw file.
                Preview = null;
            }
        }
        else
        {
            try { TextContent = StrictUtf8.GetString(attachment.Data); }
            catch { TextContent = Encoding.UTF8.GetString(attachment.Data); }
        }
    }

    public string Name => Attachment.Name;
    public string MediaType => Attachment.MediaType;
    public bool IsImage => Attachment.IsImage;
    public BitmapImage? Preview { get; }
    public bool HasPreview => Preview is not null;
    public string TextContent { get; } = "";
    public string SizeText => FormatSize(Attachment.Data.LongLength);
    public string Details => IsImage ? $"{MediaType} · {SizeText}" : $"Text · {SizeText}";

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }
}
