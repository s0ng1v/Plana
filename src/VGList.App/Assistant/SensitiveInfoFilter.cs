using System.Text.RegularExpressions;

namespace VGList.App.Assistant;

/// <summary>
/// 敏感信息本地双保险拦截器（A5）：
/// 阻止密码、身份证号、银行卡号、验证码、API Key / 私钥等写入本地记忆文件。
/// </summary>
public static class SensitiveInfoFilter
{
    private static readonly (Regex Pattern, string Label)[] Rules =
    [
        (new Regex(@"\b[1-9]\d{5}(?:19|20)\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])\d{3}[\dXx]\b", RegexOptions.Compiled), "身份证号"),
        (new Regex(@"(?<!\d)(?:4|5|6)\d{15,18}(?!\d)", RegexOptions.Compiled), "银行卡号"),
        (new Regex(@"(?:密码|口令|password|passwd|pwd|pin码)\s*[:：是为=]?\s*\S{3,}", RegexOptions.Compiled | RegexOptions.IgnoreCase), "密码或口令"),
        (new Regex(@"(?:验证码|动态码|校验码|otp)\s*[:：是为=]?\s*\d{4,8}", RegexOptions.Compiled | RegexOptions.IgnoreCase), "一次性验证码"),
        (new Regex(@"\b(?:sk-[A-Za-z0-9_-]{12,}|AIza[0-9A-Za-z_-]{20,}|ghp_[A-Za-z0-9]{20,}|BEGIN\s+[A-Z ]*PRIVATE\s+KEY)\b", RegexOptions.Compiled), "密钥或私钥"),
    ];

    public static bool ContainsSensitiveInfo(string? text, out string? detectedKind)
    {
        detectedKind = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var (pattern, label) in Rules)
        {
            if (pattern.IsMatch(text))
            {
                detectedKind = label;
                return true;
            }
        }

        return false;
    }
}
