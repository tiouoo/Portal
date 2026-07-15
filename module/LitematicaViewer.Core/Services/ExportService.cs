using System.Text;
using LitematicaViewer.Core.Helpers;

namespace LitematicaViewer.Core.Services;

public enum ExportFormat
{
    Txt,
    Csv
}

public class ExportService
{
    public void Export(AnalysisResult result, string filePath, ExportFormat format, string lang = "zh")
    {
        var blocks = result.BlockCounts
            .OrderByDescending(kv => kv.Value)
            .ToList();

        var content = format switch
        {
            ExportFormat.Csv => BuildCsv(blocks, lang),
            ExportFormat.Txt => BuildTxt(blocks, lang),
            _ => BuildTxt(blocks, lang)
        };

        File.WriteAllText(filePath, content, Encoding.UTF8);
    }

    public void ExportCategorized(AnalysisResult result, string filePath, ExportFormat format, string lang = "zh")
    {
        var content = format switch
        {
            ExportFormat.Csv => BuildCategorizedCsv(result, lang),
            ExportFormat.Txt => BuildCategorizedTxt(result, lang),
            _ => BuildCategorizedTxt(result, lang)
        };

        File.WriteAllText(filePath, content, Encoding.UTF8);
    }

    private static string BuildTxt(List<KeyValuePair<string, long>> blocks, string lang)
    {
        var sb = new StringBuilder();
        foreach (var (blockId, count) in blocks)
        {
            var name = CnTranslateHelper.ToChinese(blockId);
            var units = UnitConverter.Convert(count, lang);
            var id = blockId.StartsWith("minecraft:") ? blockId[10..] : blockId;
            sb.AppendLine($"{count}[{units}] | {name} [{id}]");
        }
        return sb.ToString();
    }

    private static string BuildCsv(List<KeyValuePair<string, long>> blocks, string lang)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,ID,Count,Units");
        foreach (var (blockId, count) in blocks)
        {
            var name = CnTranslateHelper.ToChinese(blockId);
            var units = UnitConverter.Convert(count, lang);
            var id = blockId.StartsWith("minecraft:") ? blockId[10..] : blockId;
            sb.AppendLine($"{name},{id},{count},{units}");
        }
        return sb.ToString();
    }

    private static string BuildCategorizedTxt(AnalysisResult result, string lang)
    {
        var sb = new StringBuilder();
        foreach (var (category, blocks) in result.Categories)
        {
            if (blocks.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine(category.ToString());
            sb.AppendLine(new string('-', 20));
            foreach (var (count, blockId) in blocks)
            {
                var name = CnTranslateHelper.ToChinese(blockId);
                var units = UnitConverter.Convert(count, lang);
                var id = blockId.StartsWith("minecraft:") ? blockId[10..] : blockId;
                sb.AppendLine($"{count}[{units}] | {name} [{id}]");
            }
        }
        return sb.ToString();
    }

    private static string BuildCategorizedCsv(AnalysisResult result, string lang)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Category,Name,ID,Count,Units");
        foreach (var (category, blocks) in result.Categories)
        {
            foreach (var (count, blockId) in blocks)
            {
                var name = CnTranslateHelper.ToChinese(blockId);
                var units = UnitConverter.Convert(count, lang);
                var id = blockId.StartsWith("minecraft:") ? blockId[10..] : blockId;
                sb.AppendLine($"{category},{name},{id},{count},{units}");
            }
        }
        return sb.ToString();
    }
}
