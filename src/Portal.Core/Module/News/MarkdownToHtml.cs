using System.Text;
using System.Text.RegularExpressions;

namespace Portal.Core.Module.News;

/// <summary>轻量级 Markdown → HTML 转换器（无外部依赖），产物交给 <see cref="NewsHtmlRenderer"/> 渲染。</summary>
public static partial class MarkdownToHtml
{
    private static readonly Regex HeadingPattern = new(@"^(#{1,6})\s+(.*)$");
    private static readonly Regex HorizontalRulePattern = new(@"^\s*(?:-{3,}|\*{3,}|_{3,})\s*$");
    private static readonly Regex BlockquotePattern = new(@"^\s*>\s?(.*)$");
    private static readonly Regex UnorderedListPattern = new(@"^\s*[-*+]\s+(.*)$");
    private static readonly Regex OrderedListPattern = new(@"^\s*\d+\.\s+(.*)$");
    private static readonly Regex TaskMarkerPattern = new(@"^\[[ xX]\]\s+");
    private static readonly Regex FenceStartPattern = new(@"^\s*(?:```|~~~)\s*.*$");
    private static readonly Regex FenceEndPattern = new(@"^\s*(?:```|~~~)\s*$");
    private static readonly Regex InlineCodePattern = new(@"`([^`]+)`");
    private static readonly Regex ImagePattern = new(@"!\[([^\]]*)\]\(([^)\s]+)(?:\s+[^)]*)?\)");
    private static readonly Regex LinkPattern = new(@"\[([^\]]+)\]\(([^)\s]+)(?:\s+[^)]*)?\)");
    private static readonly Regex BoldPattern = new(@"\*\*([^*]+)\*\*|__([^_]+)__");
    private static readonly Regex ItalicPattern = new(@"(?<!\*)\*([^*]+)\*(?!\*)|(?<!_)_([^_]+)_(?!_)");
    private static readonly Regex HtmlTagPattern = new(@"</?[a-zA-Z][^>]*>|<!--[\s\S]*?-->");

    public static string Convert(string markdown)
    {
        var source = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = source.Split('\n');
        var sb = new StringBuilder();
        var paragraph = new List<string>();
        var listItems = new List<string>();
        var orderedList = false;
        var inFence = false;
        var codeLines = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            sb.Append("<p>").Append(string.Join("<br/>", paragraph.Select(ProcessInline))).Append("</p>\n");
            paragraph.Clear();
        }

        void FlushList()
        {
            if (listItems.Count == 0) return;
            var tag = orderedList ? "ol" : "ul";
            sb.Append('<').Append(tag).Append(">\n");
            foreach (var item in listItems)
                sb.Append("<li>").Append(ProcessInline(item)).Append("</li>\n");
            sb.Append("</").Append(tag).Append(">\n");
            listItems.Clear();
            orderedList = false;
        }

        void EmitCode()
        {
            if (codeLines.Count == 0) return;
            sb.Append("<pre><code>").Append(EscapeHtml(string.Join("\n", codeLines)))
                .Append("</code></pre>\n");
            codeLines = [];
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (inFence)
            {
                if (FenceEndPattern.IsMatch(line))
                {
                    inFence = false;
                    EmitCode();
                }
                else codeLines.Add(line);
                continue;
            }

            if (FenceStartPattern.IsMatch(line))
            {
                FlushParagraph();
                FlushList();
                inFence = true;
                codeLines = [];
                continue;
            }

            if (line.TrimStart().StartsWith('<'))
            {
                FlushParagraph();
                FlushList();
                sb.Append(line.Trim()).Append('\n');
                continue;
            }

            var heading = HeadingPattern.Match(line);
            if (heading.Success)
            {
                FlushParagraph();
                FlushList();
                var level = Math.Clamp(heading.Groups[1].Value.Length, 1, 6);
                var title = heading.Groups[2].Value.Trim().TrimEnd('#').Trim();
                sb.Append("<h").Append(level).Append('>').Append(ProcessInline(title))
                    .Append("</h").Append(level).Append(">\n");
                continue;
            }

            if (HorizontalRulePattern.IsMatch(line))
            {
                FlushParagraph();
                FlushList();
                sb.Append("<hr/>\n");
                continue;
            }

            var blockquote = BlockquotePattern.Match(line);
            if (blockquote.Success)
            {
                FlushParagraph();
                FlushList();
                sb.Append("<blockquote>").Append(ProcessInline(blockquote.Groups[1].Value))
                    .Append("</blockquote>\n");
                continue;
            }

            var unordered = UnorderedListPattern.Match(line);
            if (unordered.Success)
            {
                FlushParagraph();
                if (listItems.Count > 0 && orderedList) FlushList();
                orderedList = false;
                listItems.Add(TaskMarkerPattern.Replace(unordered.Groups[1].Value, string.Empty));
                continue;
            }

            var ordered = OrderedListPattern.Match(line);
            if (ordered.Success)
            {
                FlushParagraph();
                if (listItems.Count > 0 && !orderedList) FlushList();
                orderedList = true;
                listItems.Add(TaskMarkerPattern.Replace(ordered.Groups[1].Value, string.Empty));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            FlushList();
            paragraph.Add(line.Trim());
        }

        if (inFence) EmitCode();
        else FlushParagraph();
        FlushList();
        return sb.ToString();
    }

    private static string ProcessInline(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var escaped = text.Trim().Replace("&", "&amp;");

        var tags = new List<string>();
        escaped = HtmlTagPattern.Replace(escaped, match =>
        {
            tags.Add(match.Value);
            return "\u0001h" + (tags.Count - 1) + "\u0002";
        });
        escaped = escaped.Replace("<", "&lt;").Replace(">", "&gt;");

        var codes = new List<string>();
        escaped = InlineCodePattern.Replace(escaped, match =>
        {
            codes.Add(match.Groups[1].Value);
            return "\u0001" + (codes.Count - 1) + "\u0002";
        });
        escaped = BoldPattern.Replace(escaped,
            match => "<strong>" + (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) + "</strong>");
        escaped = ItalicPattern.Replace(escaped,
            match => "<em>" + (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) + "</em>");
        escaped = ImagePattern.Replace(escaped,
            match => $"<img src=\"{EscapeAttribute(match.Groups[2].Value)}\" alt=\"{EscapeAttribute(match.Groups[1].Value)}\"/>");
        escaped = LinkPattern.Replace(escaped,
            match => $"<a href=\"{EscapeAttribute(match.Groups[2].Value)}\">{match.Groups[1].Value}</a>");
        for (var i = 0; i < codes.Count; i++)
            escaped = escaped.Replace("\u0001" + i + "\u0002", "<code>" + codes[i] + "</code>");
        for (var i = 0; i < tags.Count; i++)
            escaped = escaped.Replace("\u0001h" + i + "\u0002", tags[i]);
        return escaped;
    }

    private static string EscapeHtml(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static string EscapeAttribute(string value)
    {
        return value.Replace("\"", "&quot;");
    }
}
