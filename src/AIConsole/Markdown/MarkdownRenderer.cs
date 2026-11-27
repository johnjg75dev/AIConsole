using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AIConsole.Markdown;

/// <summary>Renders a Markdown string into native WPF elements. No HTML.</summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static FrameworkElement Render(string markdown)
    {
        var root = new StackPanel { Orientation = Orientation.Vertical };
        if (string.IsNullOrEmpty(markdown)) return root;

        MarkdownDocument doc;
        try { doc = Markdig.Markdown.Parse(markdown, Pipeline); }
        catch { root.Children.Add(Plain(markdown)); return root; }

        foreach (var block in doc)
            RenderBlock(root, block, 0);

        return root;
    }

    private static void RenderBlock(Panel parent, Markdig.Syntax.Block block, int depth)
    {
        switch (block)
        {
            case HeadingBlock h:
                var heading = NewText();
                heading.FontWeight = FontWeights.Bold;
                heading.FontSize = h.Level switch { 1 => 20, 2 => 17.5, 3 => 15.5, 4 => 14, _ => 13 };
                heading.Margin = new Thickness(0, 6, 0, 3);
                if (h.Level == 1) heading.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                RenderInlines(heading.Inlines, h.Inline);
                parent.Children.Add(heading);
                break;

            case ParagraphBlock p:
                var para = NewText();
                para.Margin = new Thickness(0, 2, 0, 4);
                RenderInlines(para.Inlines, p.Inline);
                parent.Children.Add(para);
                break;

            case FencedCodeBlock f:
                parent.Children.Add(MakeCodeBlock(GetFencedCode(f), f.Info));
                break;

            case Markdig.Syntax.CodeBlock c:
                parent.Children.Add(MakeCodeBlock(GetIndentedCode(c), null));
                break;

            case ListBlock list:
                RenderList(parent, list, depth);
                break;

            case QuoteBlock quote:
                var quotePanel = new StackPanel { Margin = new Thickness(2, 4, 0, 4) };
                var quoteBorder = new Border
                {
                    Child = quotePanel,
                    Padding = new Thickness(10, 4, 8, 4),
                    Margin = new Thickness(0, 4, 0, 4),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                };
                quoteBorder.SetResourceReference(Border.BorderBrushProperty, "AccentDimBrush");
                foreach (var inner in quote) RenderBlock(quotePanel, inner, depth + 1);
                parent.Children.Add(quoteBorder);
                break;

            case ThematicBreakBlock:
                var rule = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 8) };
                rule.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
                parent.Children.Add(rule);
                break;

            case Markdig.Extensions.Tables.Table table:
                parent.Children.Add(RenderTable(table));
                break;

            case ContainerBlock container:
                foreach (var inner in container) RenderBlock(parent, inner, depth + 1);
                break;
        }
    }

    private static void RenderList(Panel parent, ListBlock list, int depth)
    {
        var panel = new StackPanel { Margin = new Thickness(4, 2, 0, 4) };
        int index = list.OrderedStart is { } s && int.TryParse(s, out var start) ? start : (list.IsOrdered ? 1 : 0);
        int itemNumber = index;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var row = new Grid { Margin = new Thickness(depth * 14, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var marker = NewText();
            marker.Foreground = Brush("AccentBrush");
            marker.Text = list.IsOrdered ? $"{itemNumber}." : (depth % 2 == 0 ? "\u2022" : "\u25E6");
            marker.VerticalAlignment = VerticalAlignment.Top;
            marker.HorizontalAlignment = HorizontalAlignment.Right;
            marker.Margin = new Thickness(0, 1, 6, 0);
            Grid.SetColumn(marker, 0);
            row.Children.Add(marker);

            var content = new StackPanel();
            Grid.SetColumn(content, 1);
            foreach (var inner in item) RenderBlock(content, inner, depth + 1);
            row.Children.Add(content);

            panel.Children.Add(row);
            itemNumber++;
        }
        parent.Children.Add(panel);
    }

    private static FrameworkElement RenderTable(Markdig.Extensions.Tables.Table table)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        var border = new Border
        {
            Child = grid,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
        };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        int colCount = table.ColumnDefinitions.Count;
        if (colCount == 0)
            colCount = table.OfType<Markdig.Extensions.Tables.TableRow>().Select(r => r.Count).DefaultIfEmpty(1).Max();
        for (int i = 0; i < colCount; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());

        int rowIndex = 0;
        foreach (var row in table.OfType<Markdig.Extensions.Tables.TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int colIndex = 0;
            foreach (var cell in row.OfType<Markdig.Extensions.Tables.TableCell>())
            {
                var cellText = NewText();
                cellText.Margin = new Thickness(9, 5, 9, 5);
                cellText.TextWrapping = TextWrapping.Wrap;
                if (row.IsHeader) cellText.FontWeight = FontWeights.SemiBold;
                foreach (var cellBlock in cell)
                    if (cellBlock is ParagraphBlock p) RenderInlines(cellText.Inlines, p.Inline);

                var cellBorder = new Border { Child = cellText };
                cellBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                cellBorder.BorderThickness = new Thickness(0, 0, colIndex < colCount - 1 ? 1 : 0, 1);
                if (row.IsHeader) cellBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");

                Grid.SetRow(cellBorder, rowIndex);
                Grid.SetColumn(cellBorder, colIndex);
                grid.Children.Add(cellBorder);
                colIndex++;
            }
            rowIndex++;
        }
        return border;
    }

    // ── inlines ──

    private static void RenderInlines(InlineCollection target, ContainerInline? inline)
    {
        if (inline is null) return;
        foreach (var child in inline) RenderInline(target, child);
    }

    private static void RenderInline(InlineCollection target, Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case LiteralInline lit:
                target.Add(new Run(lit.Content.ToString()));
                break;

            case CodeInline code:
                var codeRun = new Run(code.Content)
                {
                    FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
                    FontSize = 11.5,
                };
                codeRun.SetResourceReference(Run.ForegroundProperty, "SyntaxKeywordBrush");
                // subtle pill via nested UI container if desired; keep as run for reliability
                target.Add(codeRun);
                break;

            case EmphasisInline em:
                Span emSpan = em.DelimiterCount >= 2 ? new Bold() : new Italic();
                foreach (var child in em) RenderInline(emSpan.Inlines, child);
                target.Add(emSpan);
                break;

            case LinkInline link:
                if (link.IsImage)
                {
                    target.Add(new Run($"[image: {link.Url}]") { Foreground = Brush("TextSecondaryBrush") });
                    break;
                }
                var hyper = new Hyperlink { NavigateUri = SafeUri(link.Url) };
                hyper.SetResourceReference(Hyperlink.ForegroundProperty, "AccentBrush");
                foreach (var child in link) RenderInline(hyper.Inlines, child);
                if (hyper.Inlines.Count == 0) hyper.Inlines.Add(new Run(link.Url ?? "link"));
                hyper.RequestNavigate += (_, e) =>
                {
                    try { Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true }); }
                    catch { }
                    e.Handled = true;
                };
                target.Add(hyper);
                break;

            case AutolinkInline auto:
                var autoLink = new Hyperlink(new Run(auto.Url)) { NavigateUri = SafeUri(auto.Url) };
                autoLink.SetResourceReference(Hyperlink.ForegroundProperty, "AccentBrush");
                autoLink.RequestNavigate += (_, e) =>
                {
                    try { Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true }); }
                    catch { }
                    e.Handled = true;
                };
                target.Add(autoLink);
                break;

            case HtmlEntityInline entity:
                target.Add(new Run(entity.Transcoded.ToString()));
                break;

            case LineBreakInline lb:
                target.Add(lb.IsHard ? new LineBreak() : new Run(" "));
                break;

            case ContainerInline container:
                foreach (var child in container) RenderInline(target, child);
                break;
        }
    }

    // ── helpers ──

    /// <summary>Global toggle for line numbers on code blocks.</summary>
    public static bool ShowLineNumbers { get; set; } = true;

    private static CodeBlock MakeCodeBlock(string code, string? info)
    {
        var block = new CodeBlock { Language = SyntaxHighlighter.NormalizeLanguage(info), ShowLineNumbers = ShowLineNumbers };
        // trim one trailing newline common in fenced blocks
        block.Code = code.TrimEnd('\n').TrimEnd('\r');
        return block;
    }

    private static string GetFencedCode(FencedCodeBlock f) => f.Lines.ToString();
    private static string GetIndentedCode(Markdig.Syntax.CodeBlock c) => c.Lines.ToString();

    private static TextBlock NewText() => new() { TextWrapping = TextWrapping.Wrap };

    private static TextBlock Plain(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static Brush Brush(string key)
    {
        try { return (Brush)Application.Current.FindResource(key); }
        catch { return Brushes.White; }
    }

    private static Uri? SafeUri(string? url)
    {
        try { return new Uri(url ?? "", UriKind.RelativeOrAbsolute); }
        catch { return null; }
    }
}
