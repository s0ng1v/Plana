using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SkiaSharp;
using UglyToad.PdfPig;
using VGList.Core.Models;

namespace VGList.Agent.Attachments;

public sealed record ProcessedImage(string Name, byte[] JpegBytes, string MediaType);

public sealed record ProcessedTextDocument(string Name, string Content, bool Truncated);

public sealed record AttachmentProcessingResult(
    IReadOnlyList<ProcessedImage> Images,
    IReadOnlyList<ProcessedTextDocument> Documents,
    IReadOnlyList<string> Warnings,
    string? FatalError = null);

/// <summary>
/// 本地多模态附件处理器（单条消息 ≤ 5 个附件、单个 ≤ 20MB，ADR-0056 扩展格式）：
/// - 图片（png/jpg/webp/bmp/gif/tif/ico）：SkiaSharp 缩放到长边 ≤ 2048px 编码为 JPEG；模型不支持图片时明确提示；
/// - Office（docx/xlsx/pptx）：纯原生 Zip+XML 提取（Word 段落、Excel Markdown 表格、PPT 分页幻灯片）；
/// - PDF / EPUB / RTF / HTML / ICS / 结构化数据 / 代码与纯文本（自动探测 UTF-8 / GB18030）；
/// - 文本预算：单文件 ≤ 40k 字、总计 ≤ 80k 字，超出截断并附带 Warning。
/// </summary>
public static partial class AttachmentProcessor
{
    public const int MaxAttachments = 5;
    public const long MaxFileBytes = 20L * 1024 * 1024;
    public const int MaxImageEdge = 2048;
    public const int MaxCharsPerFile = 40_000;
    public const int MaxTotalChars = 80_000;

    static AttachmentProcessor()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [GeneratedRegex(@"<(script|style|noscript|template)\b[^>]*>[\s\S]*?</\1>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlNoiseBlockRegex();

    [GeneratedRegex(@"</?(?:p|div|br|li|tr|h[1-6]|section|article|header|footer|blockquote|hr)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBlockTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlAnyTagRegex();

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessBlankLinesRegex();

    [GeneratedRegex(@"slide(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex SlideIndexRegex();

    [GeneratedRegex(@"^([A-Z]+)(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CellRefRegex();

    public static AttachmentProcessingResult Process(
        IReadOnlyList<AttachmentRef>? attachments,
        bool modelSupportsImages)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return new AttachmentProcessingResult([], [], []);
        }

        if (attachments.Count > MaxAttachments)
        {
            return new AttachmentProcessingResult(
                [],
                [],
                [],
                $"单条消息最多附加 {MaxAttachments} 个文件（当前 {attachments.Count} 个）");
        }

        var images = new List<ProcessedImage>();
        var docs = new List<ProcessedTextDocument>();
        var warnings = new List<string>();
        var remainingBudget = MaxTotalChars;

        foreach (var att in attachments)
        {
            if (string.IsNullOrWhiteSpace(att.FullPath) || !File.Exists(att.FullPath))
            {
                warnings.Add($"附件「{att.Name}」不存在或已移动，已跳过");
                continue;
            }

            var info = new FileInfo(att.FullPath);
            if (info.Length > MaxFileBytes)
            {
                return new AttachmentProcessingResult(
                    [],
                    [],
                    warnings,
                    $"附件「{att.Name}」超过 20MB 大小限制");
            }

            var ext = info.Extension.ToLowerInvariant();
            var category = AttachmentFormats.Classify(ext);

            if (category == AttachmentCategory.Image)
            {
                if (!modelSupportsImages)
                {
                    return new AttachmentProcessingResult(
                        [],
                        [],
                        warnings,
                        "当前选用的模型不支持图片输入，请切换支持视觉的模型或在「设置 → 模型服务」中检查配置。");
                }

                try
                {
                    var jpeg = ResizeImageToJpeg(att.FullPath);
                    images.Add(new ProcessedImage(att.Name, jpeg, "image/jpeg"));
                }
                catch
                {
                    warnings.Add($"图片「{att.Name}」无法解码，已跳过");
                }

                continue;
            }

            string? extracted = category switch
            {
                AttachmentCategory.Pdf => ExtractPdfText(att.FullPath, att.Name, warnings),
                AttachmentCategory.Word => ExtractDocxText(att.FullPath, att.Name, warnings),
                AttachmentCategory.Excel => ExtractXlsxText(att.FullPath, att.Name, warnings),
                AttachmentCategory.PowerPoint => ExtractPptxText(att.FullPath, att.Name, warnings),
                AttachmentCategory.Epub => ExtractEpubText(att.FullPath, att.Name, warnings),
                AttachmentCategory.Rtf => ExtractRtfText(att.FullPath, att.Name, warnings),
                AttachmentCategory.Html => ExtractHtmlText(ReadTextWithEncodingDetection(att.FullPath)),
                AttachmentCategory.Calendar or AttachmentCategory.Data or AttachmentCategory.Code or AttachmentCategory.Text
                    => ReadTextWithEncodingDetection(att.FullPath),
                _ => null,
            };

            if (category == AttachmentCategory.Unsupported)
            {
                warnings.Add($"暂不支持「{att.Name}」的格式（{ext}），已跳过");
                continue;
            }

            if (string.IsNullOrWhiteSpace(extracted))
            {
                continue;
            }

            if (remainingBudget <= 0)
            {
                warnings.Add($"附件总字数已达 {MaxTotalChars:N0} 字上限，「{att.Name}」未载入");
                continue;
            }

            var cap = Math.Min(MaxCharsPerFile, remainingBudget);
            var truncated = extracted.Length > cap;
            var content = truncated ? extracted[..cap] + "\n…（内容已截断）" : extracted;
            if (truncated)
            {
                warnings.Add($"附件「{att.Name}」较长，已截断至前 {cap:N0} 字");
            }

            remainingBudget -= Math.Min(extracted.Length, cap);
            docs.Add(new ProcessedTextDocument(att.Name, content, truncated));
        }

        return new AttachmentProcessingResult(images, docs, warnings);
    }

    private static byte[] ResizeImageToJpeg(string path)
    {
        using var input = File.OpenRead(path);
        using var bitmap = SKBitmap.Decode(input)
            ?? throw new InvalidDataException("无法解码图片");

        var maxSide = Math.Max(bitmap.Width, bitmap.Height);
        SKBitmap toEncode = bitmap;
        SKBitmap? resized = null;

        try
        {
            if (maxSide > MaxImageEdge)
            {
                var scale = (double)MaxImageEdge / maxSide;
                var newW = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                var newH = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                var info = new SKImageInfo(newW, newH, bitmap.ColorType, bitmap.AlphaType);
                resized = bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                if (resized is not null)
                {
                    toEncode = resized;
                }
            }

            using var image = SKImage.FromBitmap(toEncode);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            return data.ToArray();
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static string? ExtractPdfText(string path, string name, List<string> warnings)
    {
        try
        {
            using var doc = PdfDocument.Open(path);
            var sb = new StringBuilder();
            foreach (var page in doc.GetPages())
            {
                var t = page.Text?.Trim();
                if (!string.IsNullOrEmpty(t))
                {
                    sb.AppendLine(t);
                }
            }

            var result = sb.ToString().Trim();
            if (result.Length == 0)
            {
                warnings.Add($"PDF「{name}」没有可提取的文字层（可能是扫描件），建议截图后作为图片发送。");
                return null;
            }

            return result;
        }
        catch
        {
            warnings.Add($"PDF「{name}」读取失败或已加密，已跳过");
            return null;
        }
    }

    private static string? ExtractDocxText(string path, string name, List<string> warnings)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry("word/document.xml");
            if (entry is null)
            {
                warnings.Add($"Word 文档「{name}」缺少正文内容，已跳过");
                return null;
            }

            using var stream = entry.Open();
            var xdoc = XDocument.Load(stream);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var sb = new StringBuilder();
            foreach (var para in xdoc.Descendants(w + "p"))
            {
                var line = string.Concat(para.Descendants(w + "t").Select(t => t.Value));
                if (!string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine(line);
                }
            }

            return sb.ToString().Trim();
        }
        catch
        {
            warnings.Add($"Word 文档「{name}」解析失败，请确认为标准 .docx 格式");
            return null;
        }
    }

    private static string? ExtractXlsxText(string path, string name, List<string> warnings)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var sharedStrings = new List<string>();
            var sstEntry = zip.GetEntry("xl/sharedStrings.xml");
            if (sstEntry is not null)
            {
                using var ssStream = sstEntry.Open();
                var ssDoc = XDocument.Load(ssStream);
                foreach (var si in ssDoc.Descendants().Where(e => e.Name.LocalName == "si"))
                {
                    var text = string.Concat(si.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value));
                    sharedStrings.Add(text);
                }
            }

            var sheetNames = new List<string>();
            var wbEntry = zip.GetEntry("xl/workbook.xml");
            if (wbEntry is not null)
            {
                using var wbStream = wbEntry.Open();
                var wbDoc = XDocument.Load(wbStream);
                foreach (var sh in wbDoc.Descendants().Where(e => e.Name.LocalName == "sheet"))
                {
                    var n = sh.Attribute("name")?.Value;
                    if (!string.IsNullOrWhiteSpace(n))
                    {
                        sheetNames.Add(n);
                    }
                }
            }

            var sheetEntries = zip.Entries
                .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => ExtractTrailingNumber(e.Name))
                .ToList();

            if (sheetEntries.Count == 0)
            {
                warnings.Add($"Excel「{name}」没有可读取的工作表，已跳过");
                return null;
            }

            var sb = new StringBuilder();
            for (var sIdx = 0; sIdx < sheetEntries.Count; sIdx++)
            {
                using var stream = sheetEntries[sIdx].Open();
                var doc = XDocument.Load(stream);
                var rows = new List<List<string>>();

                foreach (var rowEl in doc.Descendants().Where(e => e.Name.LocalName == "row"))
                {
                    var cells = new List<string>();
                    foreach (var cell in rowEl.Elements().Where(e => e.Name.LocalName == "c"))
                    {
                        var colIdx = ParseColumnIndex(cell.Attribute("r")?.Value);
                        while (colIdx >= 0 && cells.Count < colIdx)
                        {
                            cells.Add(string.Empty);
                        }

                        var cellType = cell.Attribute("t")?.Value;
                        string val;
                        if (string.Equals(cellType, "s", StringComparison.Ordinal))
                        {
                            var rawIdx = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value;
                            val = int.TryParse(rawIdx, out var idx) && idx >= 0 && idx < sharedStrings.Count
                                ? sharedStrings[idx]
                                : string.Empty;
                        }
                        else if (string.Equals(cellType, "inlineStr", StringComparison.Ordinal))
                        {
                            val = string.Concat(cell.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value));
                        }
                        else
                        {
                            val = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? string.Empty;
                        }

                        cells.Add(val.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim());
                    }

                    while (cells.Count > 0 && string.IsNullOrEmpty(cells[^1]))
                    {
                        cells.RemoveAt(cells.Count - 1);
                    }

                    if (cells.Count > 0)
                    {
                        rows.Add(cells);
                    }
                }

                if (rows.Count == 0)
                {
                    continue;
                }

                var sheetTitle = sIdx < sheetNames.Count ? sheetNames[sIdx] : $"Sheet{sIdx + 1}";
                sb.Append("## ").AppendLine(sheetTitle);
                var maxCols = Math.Min(32, rows.Max(r => r.Count));
                for (var rIdx = 0; rIdx < rows.Count; rIdx++)
                {
                    var row = rows[rIdx];
                    sb.Append('|');
                    for (var c = 0; c < maxCols; c++)
                    {
                        var txt = c < row.Count ? row[c].Replace("|", "\\|", StringComparison.Ordinal) : string.Empty;
                        sb.Append(' ').Append(txt).Append(" |");
                    }

                    sb.AppendLine();
                    if (rIdx == 0)
                    {
                        sb.Append('|');
                        for (var c = 0; c < maxCols; c++)
                        {
                            sb.Append(" --- |");
                        }

                        sb.AppendLine();
                    }
                }

                sb.AppendLine();
            }

            var output = sb.ToString().Trim();
            if (output.Length == 0)
            {
                warnings.Add($"Excel「{name}」内容为空，已跳过");
                return null;
            }

            return output;
        }
        catch
        {
            warnings.Add($"Excel「{name}」解析失败，请确认为标准 .xlsx 格式");
            return null;
        }
    }

    private static string? ExtractPptxText(string path, string name, List<string> warnings)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var slides = zip.Entries
                .Select(e => (Entry: e, Match: SlideIndexRegex().Match(e.FullName)))
                .Where(x => x.Entry.FullName.StartsWith("ppt/slides/", StringComparison.OrdinalIgnoreCase) && x.Match.Success)
                .OrderBy(x => int.TryParse(x.Match.Groups[1].Value, out var n) ? n : int.MaxValue)
                .ToList();

            if (slides.Count == 0)
            {
                warnings.Add($"PPT「{name}」未找到幻灯片正文，已跳过");
                return null;
            }

            var sb = new StringBuilder();
            foreach (var (entry, match) in slides)
            {
                using var stream = entry.Open();
                var doc = XDocument.Load(stream);
                var lines = new List<string>();
                foreach (var p in doc.Descendants().Where(e => e.Name.LocalName == "p"))
                {
                    var line = string.Concat(p.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value)).Trim();
                    if (!string.IsNullOrEmpty(line))
                    {
                        lines.Add(line);
                    }
                }

                if (lines.Count > 0)
                {
                    sb.Append("## 第 ").Append(match.Groups[1].Value).AppendLine(" 页");
                    foreach (var line in lines)
                    {
                        sb.AppendLine(line);
                    }

                    sb.AppendLine();
                }
            }

            var result = sb.ToString().Trim();
            if (result.Length == 0)
            {
                warnings.Add($"PPT「{name}」没有可提取的文字（可能全为图片），已跳过");
                return null;
            }

            return result;
        }
        catch
        {
            warnings.Add($"PPT「{name}」解析失败，请确认为标准 .pptx 格式");
            return null;
        }
    }

    private static string? ExtractEpubText(string path, string name, List<string> warnings)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var htmlEntries = zip.Entries
                .Where(e => e.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) ||
                            e.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                            e.FullName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sb = new StringBuilder();
            foreach (var entry in htmlEntries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var text = ExtractHtmlText(reader.ReadToEnd());
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                    sb.AppendLine();
                    if (sb.Length > MaxCharsPerFile + 2000)
                    {
                        break;
                    }
                }
            }

            var result = sb.ToString().Trim();
            if (result.Length == 0)
            {
                warnings.Add($"EPUB「{name}」未提取到正文，已跳过");
                return null;
            }

            return result;
        }
        catch
        {
            warnings.Add($"EPUB「{name}」解析失败，已跳过");
            return null;
        }
    }

    private static string? ExtractRtfText(string path, string name, List<string> warnings)
    {
        try
        {
            var raw = ReadTextWithEncodingDetection(path);
            var sb = new StringBuilder(raw.Length / 2);
            var skipDepth = 0;
            var groupDepth = 0;
            for (var i = 0; i < raw.Length; i++)
            {
                var ch = raw[i];
                if (ch == '{')
                {
                    groupDepth++;
                    if (i + 2 < raw.Length && raw[i + 1] == '\\' && raw[i + 2] == '*')
                    {
                        skipDepth = skipDepth == 0 ? groupDepth : skipDepth;
                    }

                    continue;
                }

                if (ch == '}')
                {
                    if (skipDepth == groupDepth)
                    {
                        skipDepth = 0;
                    }

                    groupDepth = Math.Max(0, groupDepth - 1);
                    continue;
                }

                if (skipDepth > 0)
                {
                    continue;
                }

                if (ch == '\\' && i + 1 < raw.Length)
                {
                    var next = raw[i + 1];
                    if (next is '\\' or '{' or '}')
                    {
                        sb.Append(next);
                        i++;
                        continue;
                    }

                    if (next == '\'' && i + 3 < raw.Length &&
                        byte.TryParse(raw.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                    {
                        sb.Append((char)b);
                        i += 3;
                        continue;
                    }

                    var start = i + 1;
                    var j = start;
                    while (j < raw.Length && char.IsLetter(raw[j]))
                    {
                        j++;
                    }

                    var word = raw[start..j];
                    var numStart = j;
                    if (j < raw.Length && raw[j] == '-')
                    {
                        j++;
                    }

                    while (j < raw.Length && char.IsDigit(raw[j]))
                    {
                        j++;
                    }

                    var numStr = raw[numStart..j];
                    if (j < raw.Length && raw[j] == ' ')
                    {
                        j++;
                    }

                    if (word is "par" or "line")
                    {
                        sb.AppendLine();
                    }
                    else if (word == "tab")
                    {
                        sb.Append('\t');
                    }
                    else if (word == "u" && int.TryParse(numStr, out var codePoint))
                    {
                        if (codePoint < 0)
                        {
                            codePoint += 65536;
                        }

                        sb.Append((char)codePoint);
                        // 跳过 RTF \uN 后的 1 个占位字符
                        if (j < raw.Length && raw[j] != '\\' && raw[j] != '{' && raw[j] != '}')
                        {
                            j++;
                        }
                    }
                    else if (word is "fonttbl" or "colortbl" or "stylesheet" or "info")
                    {
                        skipDepth = groupDepth;
                    }

                    i = j - 1;
                    continue;
                }

                if (ch is '\r' or '\n')
                {
                    continue;
                }

                sb.Append(ch);
            }

            return ExcessBlankLinesRegex().Replace(sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), "\n\n").Trim();
        }
        catch
        {
            warnings.Add($"RTF「{name}」解析失败，已跳过");
            return null;
        }
    }

    public static string ExtractHtmlText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var cleaned = HtmlNoiseBlockRegex().Replace(html, " ");
        cleaned = HtmlBlockTagRegex().Replace(cleaned, "\n");
        cleaned = HtmlAnyTagRegex().Replace(cleaned, " ");
        cleaned = WebUtility.HtmlDecode(cleaned);
        cleaned = cleaned.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        var lines = cleaned.Split('\n')
            .Select(l => HorizontalWhitespaceRegex().Replace(l, " ").Trim())
            .Where(l => l.Length > 0);
        return ExcessBlankLinesRegex().Replace(string.Join("\n", lines), "\n\n").Trim();
    }

    private static int ExtractTrailingNumber(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var i = stem.Length - 1;
        while (i >= 0 && char.IsDigit(stem[i]))
        {
            i--;
        }

        return int.TryParse(stem[(i + 1)..], out var num) ? num : int.MaxValue;
    }

    private static int ParseColumnIndex(string? cellRef)
    {
        if (string.IsNullOrWhiteSpace(cellRef))
        {
            return -1;
        }

        var match = CellRefRegex().Match(cellRef.Trim());
        if (!match.Success)
        {
            return -1;
        }

        var letters = match.Groups[1].Value.ToUpperInvariant();
        var index = 0;
        foreach (var ch in letters)
        {
            index = index * 26 + (ch - 'A' + 1);
        }

        return index - 1;
    }

    private static string ReadTextWithEncodingDetection(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        // UTF-8 BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        // 严格 UTF-8 尝试；非法字节序列自动回退 GB18030（兼容 GBK）
        try
        {
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
    }
}
