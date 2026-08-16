using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using HtmlAgilityPack;

namespace Portal.Core.Module.News;

public static class NewsHtmlRenderer
{
    private const FontWeight BodyWeight = FontWeight.DemiBold;
    private const FontWeight EmphasisWeight = FontWeight.Bold;

    private static readonly FontFamily MonospaceFamily =
        new("Cascadia Code,Consolas,Menlo,Monaco,monospace");

    
    private static Avalonia.Styling.ControlTheme? s_hyperlinkTheme;

        public static HtmlDocument Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html ?? string.Empty);
        return doc;
    }

        public static IReadOnlyList<Control> Render(string html)
    {
        return Render(Parse(html));
    }

        public static IReadOnlyList<Control> Render(HtmlDocument doc)
    {
        return RenderEnumerable(doc).ToList();
    }

        public static IEnumerable<Control> RenderEnumerable(HtmlDocument doc)
    {
        var root = doc.DocumentNode;
        var container = root.SelectSingleNode("//body") ?? root;
        foreach (var node in container.ChildNodes)
        {
            var controls = RenderBlockNode(node, indentLevel: 0);
            foreach (var c in controls) yield return c;
        }
    }

    private static IReadOnlyList<Control> RenderBlockNode(HtmlNode node, int indentLevel)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            var text = node.InnerText?.Trim();
            if (string.IsNullOrEmpty(text)) return [];
            
            return [CreateParagraph([new Run(text) { FontWeight = BodyWeight }], indentLevel)];
        }

        if (node.NodeType != HtmlNodeType.Element) return [];

        switch (node.Name.ToLowerInvariant())
        {
            case "p":
            {
                var inlines = BuildInlines(node);
                return inlines.Count == 0 ? [] : [CreateParagraph(inlines, indentLevel)];
            }
            case "br":
                return [];
            case "h1": return [CreateHeading(BuildInlines(node), 24, indentLevel)];
            case "h2": return [CreateHeading(BuildInlines(node), 20, indentLevel)];
            case "h3": return [CreateHeading(BuildInlines(node), 17, indentLevel)];
            case "h4": return [CreateHeading(BuildInlines(node), 15, indentLevel)];
            case "h5": return [CreateHeading(BuildInlines(node), 14, indentLevel)];
            case "h6": return [CreateHeading(BuildInlines(node), 13, indentLevel)];
            case "ul":
                return [RenderList(node, indentLevel, isOrdered: false)];
            case "ol":
                return [RenderList(node, indentLevel, isOrdered: true)];
            case "li":
                return [CreateParagraph(BuildInlines(node), indentLevel)];
            case "a":
            {
                var btn = CreateHyperlinkButton(node);
                if (btn == null) return [];
                btn.Margin = new Thickness(indentLevel * 20, 4, 0, 4);
                return [btn];
            }
            case "div":
            case "section":
            case "article":
            case "main":
            {
                var output = new List<Control>();
                foreach (var child in node.ChildNodes)
                {
                    output.AddRange(RenderBlockNode(child, indentLevel));
                }
                return output;
            }
            default:
            {
                var inlines = BuildInlines(node);
                return inlines.Count == 0 ? [] : [CreateParagraph(inlines, indentLevel)];
            }
        }
    }

    private static Control RenderList(HtmlNode listNode, int indentLevel, bool isOrdered)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Margin = new Thickness(indentLevel * 20, 4, 0, 4)
        };

        var index = 1;
        foreach (var child in listNode.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element) continue;
            if (child.Name.ToLowerInvariant() != "li") continue;

            var inlines = BuildInlines(child, out var nestedLists);
            var prefix = isOrdered ? $"{index}. " : "•  ";
            var itemInlines = new InlineCollection();
            itemInlines.Add(new Run(prefix) { FontWeight = BodyWeight });
            foreach (var inline in inlines) itemInlines.Add(inline);

            var itemText = new TextBlock
            {
                Inlines = itemInlines,
                FontSize = 14,
                FontWeight = BodyWeight,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, nestedLists.Count > 0 ? 0 : 2)
            };
            panel.Children.Add(itemText);

            foreach (var nested in nestedLists)
            {
                panel.Children.Add(RenderList(nested, indentLevel + 1, nested.Name.ToLowerInvariant() == "ol"));
            }

            index++;
        }
        return panel;
    }

    private static TextBlock CreateParagraph(IReadOnlyList<Inline> inlines, int indentLevel)
    {
        return new TextBlock
        {
            Inlines = ToInlineCollection(inlines),
            FontSize = 14,
            FontWeight = BodyWeight,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(indentLevel * 20, 4, 0, 4)
        };
    }

    private static TextBlock CreateHeading(IReadOnlyList<Inline> inlines, double fontSize, int indentLevel)
    {
        return new TextBlock
        {
            Inlines = ToInlineCollection(inlines),
            FontSize = fontSize,
            FontWeight = EmphasisWeight,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(indentLevel * 20, 12, 0, 4)
        };
    }

    private static InlineCollection ToInlineCollection(IReadOnlyList<Inline> inlines)
    {
        var collection = new InlineCollection();
        foreach (var inline in inlines) collection.Add(inline);
        return collection;
    }

    private static IReadOnlyList<Inline> BuildInlines(HtmlNode node) => BuildInlines(node, out _);

    private static IReadOnlyList<Inline> BuildInlines(HtmlNode node, out List<HtmlNode> nestedLists)
    {
        nestedLists = [];
        var inlines = new List<Inline>();
        foreach (var child in node.ChildNodes)
        {
            AppendInline(child, inlines, nestedLists);
        }
        return inlines;
    }

    private static void AppendInline(HtmlNode node, List<Inline> output, List<HtmlNode> nestedLists)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Text:
            {
                var text = node.InnerText;
                if (string.IsNullOrEmpty(text)) return;
                
                
                text = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
                output.Add(new Run(text) { FontWeight = BodyWeight });
                return;
            }
            case HtmlNodeType.Element:
                break;
            default:
                return;
        }

        switch (node.Name.ToLowerInvariant())
        {
            case "b":
            case "strong":
            {
                var bold = new Bold { FontWeight = EmphasisWeight };
                FillSpan(bold.Inlines, node, nestedLists);
                output.Add(bold);
                return;
            }
            case "i":
            case "em":
            {
                var italic = new Italic { FontWeight = BodyWeight };
                FillSpan(italic.Inlines, node, nestedLists);
                output.Add(italic);
                return;
            }
            case "u":
            {
                var underline = new Underline();
                FillSpan(underline.Inlines, node, nestedLists);
                output.Add(underline);
                return;
            }
            case "code":
            {
                output.Add(new Run(node.InnerText)
                {
                    FontFamily = MonospaceFamily,
                    FontSize = 13,
                    FontWeight = BodyWeight
                });
                return;
            }
            case "a":
            {
                var btn = CreateHyperlinkButton(node);
                if (btn != null)
                {
                    output.Add(new InlineUIContainer
                    {
                        Child = btn,
                        BaselineAlignment = BaselineAlignment.TextBottom
                    });
                }
                return;
            }
            case "br":
                output.Add(new LineBreak());
                return;
            case "span":
            {
                var span = new Span();
                FillSpan(span.Inlines, node, nestedLists);
                output.Add(span);
                return;
            }
            case "ul":
            case "ol":
                nestedLists.Add(node);
                return;
            default:
            {
                
                foreach (var child in node.ChildNodes)
                {
                    AppendInline(child, output, nestedLists);
                }
                return;
            }
        }
    }

        private static void FillSpan(InlineCollection target, HtmlNode parent, List<HtmlNode> nestedLists)
    {
        foreach (var child in parent.ChildNodes)
        {
            var temp = new List<Inline>();
            AppendInline(child, temp, nestedLists);
            foreach (var inline in temp) target.Add(inline);
        }
    }

    private static HyperlinkButton? CreateHyperlinkButton(HtmlNode anchorNode)
    {
        var href = anchorNode.GetAttributeValue("href", string.Empty);
        var text = anchorNode.InnerText;
        if (string.IsNullOrWhiteSpace(text)) return null;

        var button = new HyperlinkButton
        {
            Content = text,
            FontSize = 14,
            FontWeight = BodyWeight,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (s_hyperlinkTheme is null
            && Application.Current?.FindResource("UnderlineHyperlinkButton") is Avalonia.Styling.ControlTheme theme)
        {
            s_hyperlinkTheme = theme;
        }
        if (s_hyperlinkTheme is not null)
        {
            button.Theme = s_hyperlinkTheme;
        }

        if (!string.IsNullOrWhiteSpace(href) && Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            button.NavigateUri = uri;
        }
        else
        {
            button.Click += (_, _) =>
            {
                if (!string.IsNullOrEmpty(href)) TryOpenUrl(href);
            };
        }
        return button;
    }

    private static void TryOpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            
        }
    }
}
