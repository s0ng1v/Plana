namespace VGList.Core.Models;

/// <summary>附件大类（用于 UI 展示徽标与后端解析分流）。</summary>
public enum AttachmentCategory
{
    Unsupported,
    Image,
    Pdf,
    Word,
    Excel,
    PowerPoint,
    Epub,
    Rtf,
    Html,
    Calendar,
    Data,
    Code,
    Text,
}

/// <summary>
/// Plana 助理支持的全部附件格式唯一真源（ADR-0056）：
/// Desktop（拖放/选择框/徽标）与 Agent（AttachmentProcessor）共用同一清单，杜绝前后端脱节。
/// </summary>
public static class AttachmentFormats
{
    private static readonly Dictionary<string, AttachmentCategory> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // 图片（多模态视觉输入）
        [".png"] = AttachmentCategory.Image,
        [".jpg"] = AttachmentCategory.Image,
        [".jpeg"] = AttachmentCategory.Image,
        [".webp"] = AttachmentCategory.Image,
        [".bmp"] = AttachmentCategory.Image,
        [".gif"] = AttachmentCategory.Image,
        [".tif"] = AttachmentCategory.Image,
        [".tiff"] = AttachmentCategory.Image,
        [".ico"] = AttachmentCategory.Image,

        // Office 与富文档
        [".pdf"] = AttachmentCategory.Pdf,
        [".docx"] = AttachmentCategory.Word,
        [".xlsx"] = AttachmentCategory.Excel,
        [".pptx"] = AttachmentCategory.PowerPoint,
        [".epub"] = AttachmentCategory.Epub,
        [".rtf"] = AttachmentCategory.Rtf,

        // 网页与日历
        [".html"] = AttachmentCategory.Html,
        [".htm"] = AttachmentCategory.Html,
        [".xhtml"] = AttachmentCategory.Html,
        [".ics"] = AttachmentCategory.Calendar,

        // 结构化数据与配置
        [".json"] = AttachmentCategory.Data,
        [".jsonl"] = AttachmentCategory.Data,
        [".xml"] = AttachmentCategory.Data,
        [".svg"] = AttachmentCategory.Data,
        [".yaml"] = AttachmentCategory.Data,
        [".yml"] = AttachmentCategory.Data,
        [".toml"] = AttachmentCategory.Data,
        [".csv"] = AttachmentCategory.Data,
        [".tsv"] = AttachmentCategory.Data,
        [".ini"] = AttachmentCategory.Data,
        [".conf"] = AttachmentCategory.Data,
        [".cfg"] = AttachmentCategory.Data,
        [".env"] = AttachmentCategory.Data,
        [".sql"] = AttachmentCategory.Data,

        // 纯文本与笔记
        [".txt"] = AttachmentCategory.Text,
        [".md"] = AttachmentCategory.Text,
        [".markdown"] = AttachmentCategory.Text,
        [".rst"] = AttachmentCategory.Text,
        [".tex"] = AttachmentCategory.Text,
        [".org"] = AttachmentCategory.Text,
        [".log"] = AttachmentCategory.Text,

        // 常见代码与工程文件
        [".cs"] = AttachmentCategory.Code,
        [".py"] = AttachmentCategory.Code,
        [".js"] = AttachmentCategory.Code,
        [".ts"] = AttachmentCategory.Code,
        [".tsx"] = AttachmentCategory.Code,
        [".jsx"] = AttachmentCategory.Code,
        [".java"] = AttachmentCategory.Code,
        [".kt"] = AttachmentCategory.Code,
        [".go"] = AttachmentCategory.Code,
        [".rs"] = AttachmentCategory.Code,
        [".c"] = AttachmentCategory.Code,
        [".cpp"] = AttachmentCategory.Code,
        [".h"] = AttachmentCategory.Code,
        [".hpp"] = AttachmentCategory.Code,
        [".css"] = AttachmentCategory.Code,
        [".scss"] = AttachmentCategory.Code,
        [".less"] = AttachmentCategory.Code,
        [".sh"] = AttachmentCategory.Code,
        [".ps1"] = AttachmentCategory.Code,
        [".bat"] = AttachmentCategory.Code,
        [".cmd"] = AttachmentCategory.Code,
        [".rb"] = AttachmentCategory.Code,
        [".php"] = AttachmentCategory.Code,
        [".swift"] = AttachmentCategory.Code,
        [".dart"] = AttachmentCategory.Code,
        [".vue"] = AttachmentCategory.Code,
        [".svelte"] = AttachmentCategory.Code,
        [".axaml"] = AttachmentCategory.Code,
        [".xaml"] = AttachmentCategory.Code,
        [".csproj"] = AttachmentCategory.Code,
        [".sln"] = AttachmentCategory.Code,
        [".slnx"] = AttachmentCategory.Code,
    };

    public static AttachmentCategory Classify(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return AttachmentCategory.Unsupported;
        }

        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        return ExtensionMap.TryGetValue(normalized, out var cat) ? cat : AttachmentCategory.Unsupported;
    }

    public static bool IsSupported(string? extension) =>
        Classify(extension) != AttachmentCategory.Unsupported;

    public static bool IsImage(string? extension) =>
        Classify(extension) == AttachmentCategory.Image;

    public static string GetKindLabel(string? extension) => Classify(extension) switch
    {
        AttachmentCategory.Image => "图片",
        AttachmentCategory.Pdf => "PDF",
        AttachmentCategory.Word => "Word",
        AttachmentCategory.Excel => "Excel",
        AttachmentCategory.PowerPoint => "PPT",
        AttachmentCategory.Epub => "EPUB",
        AttachmentCategory.Rtf => "RTF",
        AttachmentCategory.Html => "网页",
        AttachmentCategory.Calendar => "日历",
        AttachmentCategory.Data => "数据",
        AttachmentCategory.Code => "代码",
        AttachmentCategory.Text => "文本",
        _ => string.Empty,
    };

    public static IReadOnlyList<string> AllPickerPatterns =>
        ExtensionMap.Keys.Select(ext => "*" + ext).OrderBy(p => p, StringComparer.Ordinal).ToList();

    public const string SupportedSummaryText = "支持图片、PDF、Word/Excel/PPT、HTML、ICS、EPUB、RTF、数据及代码/文本文件";
}
