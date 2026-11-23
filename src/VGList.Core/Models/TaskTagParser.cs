using System.Text.RegularExpressions;

namespace VGList.Core.Models;

/// <summary>
/// 任务标签规范化与行内 #标签 解析器（P1，ADR-0053）：
/// - 从「完成评审 #工作 #重要」提取标签并清理标题，保留原文字内容；
/// - 忽略 #FF00AA 等十六进制颜色与纯数字编号，避免误提取。
/// </summary>
public static partial class TaskTagParser
{
    public const int MaxTagsPerTask = 10;
    public const int MaxTagLength = 20;

    [GeneratedRegex(@"(?:^|\s)#([^\s#，,。.;；:：!！?？/\\]{1,20})(?=\s|$|[，,。.;；!！?？])", RegexOptions.Compiled)]
    private static partial Regex InlineTagRegex();

    [GeneratedRegex(@"^[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$", RegexOptions.Compiled)]
    private static partial Regex HexColorRegex();

    public static List<string> Normalize(IEnumerable<string>? rawTags)
    {
        if (rawTags is null)
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawTags)
        {
            var tag = CleanSingle(raw);
            if (tag is null || !seen.Add(tag))
            {
                continue;
            }

            result.Add(tag);
            if (result.Count >= MaxTagsPerTask)
            {
                break;
            }
        }

        return result;
    }

    public static string? CleanSingle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim().TrimStart('#').Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxTagLength)
        {
            return null;
        }

        if (HexColorRegex().IsMatch(trimmed))
        {
            return null;
        }

        return trimmed;
    }

    /// <summary>从输入文本中提取行内 #标签；若剥离后标题为空则保留原文本不剥离。</summary>
    public static (string CleanTitle, List<string> Tags) ExtractInlineTags(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.Contains('#'))
        {
            return (input?.Trim() ?? string.Empty, []);
        }

        var found = new List<string>();
        var cleaned = InlineTagRegex().Replace(input, match =>
        {
            var candidate = match.Groups[1].Value;
            if (HexColorRegex().IsMatch(candidate) || candidate.All(char.IsDigit))
            {
                return match.Value;
            }

            found.Add(candidate);
            return " ";
        });

        cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return (input.Trim(), []);
        }

        return (cleaned, Normalize(found));
    }
}
