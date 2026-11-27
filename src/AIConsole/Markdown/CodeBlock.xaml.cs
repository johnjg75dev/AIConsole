using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AIConsole.Services;

namespace AIConsole.Markdown;

public partial class CodeBlock : UserControl
{
    public const double CodeLineHeight = 18.0;
    public const double CodeFontSize = 12.5;

    private string _code = "";
    private string? _language;

    public CodeBlock()
    {
        InitializeComponent();
        CopyButton.Click += (_, _) => Copy();
        ThemeManager.ThemeChanged += _ => Rebuild();
    }

    public string Code { get => _code; set { _code = value; Rebuild(); } }
    public string? Language { get => _language; set { _language = value; Rebuild(); } }

    /// <summary>Copy button icon can be any glyph; text set by caller. Default "Copy".</summary>
    public bool ShowLineNumbers { get; set; } = true;

    private void Copy() => Clipboard.SetText(_code);

    private void Rebuild()
    {
        var fontFamily = (FontFamily)Application.Current.FindResource("FontMono");

        LanguageLabel.Text = string.IsNullOrWhiteSpace(_language) ? "code" : _language;
        CodeLines.Children.Clear();
        GutterLines.Children.Clear();
        Gutter.Visibility = ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;
        GutterDivider.Visibility = ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;

        var lines = SyntaxHighlighter.Highlight(_code, _language);
        int total = lines.Count;
        int gutterDigits = total.ToString().Length;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = new TextBlock
            {
                FontFamily = fontFamily,
                FontSize = CodeFontSize,
                LineHeight = CodeLineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextWrapping = TextWrapping.NoWrap,
            };
            line.SetResourceReference(TextBlock.ForegroundProperty, "SyntaxPlainBrush");
            if (i == 0) line.Margin = new Thickness(0);
            BuildInlines(line, lines[i]);
            if (line.Inlines.Count == 0) line.Inlines.Add(new Run(" "));
            CodeLines.Children.Add(line);

            var num = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontFamily = fontFamily,
                FontSize = CodeFontSize - 1.5,
                LineHeight = CodeLineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                MinWidth = gutterDigits * 7.5,
            };
            num.SetResourceReference(TextBlock.ForegroundProperty, "SyntaxLineNumberBrush");
            GutterLines.Children.Add(num);
        }

        if (lines.Count == 0)
        {
            CodeLines.Children.Add(new TextBlock { Text = " ", LineHeight = CodeLineHeight, FontFamily = fontFamily, FontSize = CodeFontSize });
        }
    }

    private static void BuildInlines(TextBlock target, IReadOnlyList<SyntaxToken> tokens)
    {
        foreach (var tok in tokens)
        {
            if (tok.Text.Length == 0) continue;
            var run = new Run(tok.Text);
            var key = tok.Kind switch
            {
                SyntaxKind.Keyword => "SyntaxKeywordBrush",
                SyntaxKind.String => "SyntaxStringBrush",
                SyntaxKind.Comment => "SyntaxCommentBrush",
                SyntaxKind.Number => "SyntaxNumberBrush",
                SyntaxKind.Type => "SyntaxTypeBrush",
                SyntaxKind.Function => "SyntaxFunctionBrush",
                SyntaxKind.Operator => "SyntaxOperatorBrush",
                SyntaxKind.Punctuation => "SyntaxPunctuationBrush",
                SyntaxKind.Tag => "SyntaxTagBrush",
                SyntaxKind.Attribute => "SyntaxAttributeBrush",
                SyntaxKind.Preprocessor => "SyntaxPreprocessorBrush",
                SyntaxKind.Constant => "SyntaxConstantBrush",
                _ => "SyntaxPlainBrush",
            };
            run.SetResourceReference(Run.ForegroundProperty, key);
            if (tok.Kind == SyntaxKind.Comment) run.FontStyle = FontStyles.Italic;
            target.Inlines.Add(run);
        }
    }
}
