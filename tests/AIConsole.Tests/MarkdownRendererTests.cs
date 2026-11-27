using System.Windows.Controls;
using AIConsole.Markdown;

namespace AIConsole.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void Render_CreatesOneCodeBlockPerFencedBlock()
    {
        TestThread.RunSta(() =>
        {
            const string markdown = """
                Intro text.

                ```csharp
                var first = 1;
                ```

                Between the blocks.

                ```json
                { "second": true }
                ```
                """;

            var root = Assert.IsType<StackPanel>(MarkdownRenderer.Render(markdown));

            Assert.Equal(2, root.Children.OfType<CodeBlock>().Count());
            Assert.Equal(2, root.Children.OfType<TextBlock>().Count());
        });
    }
}
