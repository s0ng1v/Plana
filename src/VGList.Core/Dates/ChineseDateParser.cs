using System.Text.RegularExpressions;

namespace VGList.Core.Dates;

/// <summary>
/// 零依赖离线中文自然语言日期解析器：
/// 规则表懒编译，首次解析前不占用冷启动开销；覆盖相对日、星期、月日、偏移、月初/中/底、农历/节日与工作日，并严格过滤编号与词组反例。
/// </summary>
public sealed class ChineseDateParser : IChineseDateParser
{
    public static ChineseDateParser Shared { get; } = new();

    private static readonly Lazy<IReadOnlyList<DateRule>> RulesLazy = new(BuildRules);

    private static readonly char[] LeadingConnectors = [' ', '\t', '，', ',', '、', '：', ':', '；', ';', '-', '—', '·'];
    private static readonly string[] TrailingConnectorWords = ["之前", "以前", "前", "当天", "那天", "到", "止", "起"];

    // 紧随在「N号」之后的编号类后缀，遇到则排除
    private static readonly HashSet<char> NumberSuffixExclusions =
    [
        '线', '楼', '院', '栋', '幢', '座', '室', '厅', '店', '街', '路', '门', '口', '车', '房', '桌', '位', '排', '班', '机', '码', '球', '床', '仓', '馆',
    ];

    // 「周X」/「星期X」紧随这些字时是普通名词而非日期（如：周日历、星期天文馆）
    private static readonly HashSet<char> WeekdaySuffixExclusions = ['历', '刊', '报', '文', '记', '志', '表'];

    public DateParseResult? Parse(string text, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var best = FindCandidates(text, today)
            .OrderBy(c => c.Start)
            .ThenByDescending(c => c.Length)
            .FirstOrDefault();

        if (best is null)
        {
            return null;
        }

        var clean = BuildCleanTitle(text, best.Start, best.Length);
        return string.IsNullOrWhiteSpace(clean)
            ? null
            : best with { CleanTitle = clean };
    }

    public IReadOnlyList<DateParseResult> ParseAll(string text, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var candidates = FindCandidates(text, today)
            .OrderBy(c => c.Start)
            .ThenByDescending(c => c.Length)
            .ToList();

        // 去除重叠匹配，保留靠前且更长者
        var results = new List<DateParseResult>();
        var cursor = 0;
        foreach (var c in candidates)
        {
            if (c.Start < cursor)
            {
                continue;
            }

            var clean = BuildCleanTitle(text, c.Start, c.Length);
            results.Add(c with { CleanTitle = clean });
            cursor = c.Start + c.Length;
        }

        return results;
    }

    private static List<DateParseResult> FindCandidates(string text, DateOnly today)
    {
        var candidates = new List<DateParseResult>();
        foreach (var rule in RulesLazy.Value)
        {
            foreach (Match m in rule.Pattern.Matches(text))
            {
                if (!m.Success || m.Length == 0)
                {
                    continue;
                }

                if (rule.Resolve(text, m, today) is { } date)
                {
                    candidates.Add(new DateParseResult(date, m.Index, m.Length, m.Value, string.Empty));
                }
            }
        }

        return candidates;
    }

    private static string BuildCleanTitle(string text, int start, int length)
    {
        var end = start + length;

        // 移除紧跟在日期词后面的「之前/前/当天」等连接词（仅当后面不是纯数字/文字黏连或后面有其他内容时）
        foreach (var word in TrailingConnectorWords)
        {
            if (end + word.Length <= text.Length &&
                string.Compare(text, end, word, 0, word.Length, StringComparison.Ordinal) == 0)
            {
                // 避免把「前瞻」「前提」「到达」「起来」误切掉
                var afterIdx = end + word.Length;
                if (word.Length == 1 && afterIdx < text.Length && IsWordGlueChar(word[0], text[afterIdx]))
                {
                    continue;
                }

                end += word.Length;
                break;
            }
        }

        // 移除紧邻日期词之前的「于/在/到」单字介词（仅当它位于开头或空格/标点后）
        var removeStart = start;
        if (removeStart >= 1 && text[removeStart - 1] is '于' or '在' &&
            (removeStart == 1 || IsBoundary(text[removeStart - 2])))
        {
            removeStart--;
        }

        var left = text[..removeStart].TrimEnd(LeadingConnectors);
        var right = text[end..].TrimStart(LeadingConnectors);

        string combined;
        if (left.Length == 0)
        {
            combined = right;
        }
        else if (right.Length == 0)
        {
            combined = left;
        }
        else
        {
            var needSpace = char.IsAsciiLetterOrDigit(left[^1]) && char.IsAsciiLetterOrDigit(right[0]);
            combined = needSpace ? $"{left} {right}" : $"{left}{right}";
        }

        return combined.Trim();
    }

    private static bool IsWordGlueChar(char singleConnector, char next) => singleConnector switch
    {
        '前' => next is '提' or '瞻' or '端' or '台' or '景' or '列' or '进' or '置' or '后' or '期' or '辈' or '锋' or '夕',
        '到' => next is '达' or '期' or '底' or '手' or '位' or '账' or '场' or '访' or '货',
        '起' => next is '床' or '草' or '飞' or '始' or '点' or '来' or '步' or '诉',
        '止' => next is '损' or '步' or '痛' or '血',
        _ => false,
    };

    private static bool IsBoundary(char ch) =>
        char.IsWhiteSpace(ch) || ch is '，' or ',' or '。' or '；' or ';' or '、' or '(' or '（' or '[' or '【';

    private static IReadOnlyList<DateRule> BuildRules()
    {
        const RegexOptions opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;

        return
        [
            // 1. 完整年月日：2027年3月5日 / 2027-03-05 / 2027/3/5
            new DateRule(
                new Regex(@"(?<!\d)(20\d{2})\s*(?:年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*[日号]?|[-/.](\d{1,2})[-/.](\d{1,2}))(?!\d)", opts),
                (_, m, _) =>
                {
                    if (!int.TryParse(m.Groups[1].Value, out var year))
                    {
                        return null;
                    }

                    var mStr = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[4].Value;
                    var dStr = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[5].Value;
                    if (!int.TryParse(mStr, out var month) || !int.TryParse(dStr, out var day))
                    {
                        return null;
                    }

                    return TryCreateDate(year, month, day, out var d) ? d : null;
                }),

            // 2. 农历明确月日：农历八月十五 / 农历正月初一 / 阴历腊月廿八 / 农历8月15
            new DateRule(
                new Regex(@"(?:农历|阴历)\s*(正|冬|腊|十[一二]?|[一二三四五六七八九]|1[0-2]|[1-9])月\s*(初[一二三四五六七八九十]|十[一二三四五六七八九]?|廿[一二三四五六七八九]?|二十[一二三四五六七八九]?|三十|[1-2]\d|30|[1-9])[日号]?", opts),
                (_, m, today) =>
                {
                    if (!TryParseLunarMonth(m.Groups[1].Value, out var lm) ||
                        !TryParseLunarDay(m.Groups[2].Value, out var ld))
                    {
                        return null;
                    }

                    return CnCalendar.TryFindNextLunarDate(lm, ld, today, out var d) ? d : null;
                }),

            // 3. 公农历节日与常见节气（不含容易误判的单字词，按长词优先）
            new DateRule(
                new Regex(@"(除夕|春节|元宵节?|清明节?|端午节?|七夕节?|中秋节?|重阳节?|腊八节?|元旦节?|情人节|妇女节|劳动节|儿童节|教师节|国庆节?|圣诞节?|平安夜|立春|雨水|惊蛰|春分|谷雨|立夏|小满|芒种|夏至|小暑|大暑|立秋|处暑|白露|秋分|寒露|霜降|立冬|小雪|大雪|冬至)", opts),
                (text, m, today) =>
                {
                    if (m.Value == "清明" && m.Index + 2 < text.Length && text[m.Index + 2] == '上')
                    {
                        return null;
                    }

                    return CnCalendar.TryFindNextFestival(m.Value, today, out var d) ? d : null;
                }),

            // 4. 下个月/本月 + X号/日
            new DateRule(
                new Regex(@"(本|这|下|下个|这个)月\s*([12]\d|3[01]|0?[1-9]|三十[一]?|二十[一二三四五六七八九]?|廿[一二三四五六七八九]?|十[一二三四五六七八九]?|[一二三四五六七八九])\s*[日号]", opts),
                (text, m, today) =>
                {
                    if (HasNumberExclusionSuffix(text, m))
                    {
                        return null;
                    }

                    if (!TryParseCnOrArabicNumber(m.Groups[2].Value, out var day))
                    {
                        return null;
                    }

                    var targetMonth = m.Groups[1].Value.StartsWith('下')
                        ? new DateOnly(today.Year, today.Month, 1).AddMonths(1)
                        : new DateOnly(today.Year, today.Month, 1);
                    return TryCreateDate(targetMonth.Year, targetMonth.Month, day, out var d) ? d : null;
                }),

            // 5. 月初/月中/月底/年底/年初
            new DateRule(
                new Regex(@"(本|这|下|下个|这个)?(月初|月中|月底|月末)|(本|今|明)?(年底|年末|年初)", opts),
                (_, m, today) =>
                {
                    if (m.Groups[2].Success)
                    {
                        var prefix = m.Groups[1].Value;
                        var part = m.Groups[2].Value;
                        var monthStart = new DateOnly(today.Year, today.Month, 1);
                        if (prefix.StartsWith('下'))
                        {
                            monthStart = monthStart.AddMonths(1);
                        }

                        var day = part switch
                        {
                            "月初" => 1,
                            "月中" => 15,
                            _ => DateTime.DaysInMonth(monthStart.Year, monthStart.Month),
                        };

                        var candidate = new DateOnly(monthStart.Year, monthStart.Month, day);
                        if (string.IsNullOrEmpty(prefix) && candidate < today)
                        {
                            monthStart = monthStart.AddMonths(1);
                            day = part switch
                            {
                                "月初" => 1,
                                "月中" => 15,
                                _ => DateTime.DaysInMonth(monthStart.Year, monthStart.Month),
                            };
                            candidate = new DateOnly(monthStart.Year, monthStart.Month, day);
                        }

                        return candidate;
                    }

                    var yPrefix = m.Groups[3].Value;
                    var yPart = m.Groups[4].Value;
                    var year = yPrefix == "明" ? today.Year + 1 : today.Year;
                    if (yPart == "年初")
                    {
                        var candidate = new DateOnly(year, 1, 1);
                        if (string.IsNullOrEmpty(yPrefix) && candidate < today)
                        {
                            candidate = new DateOnly(year + 1, 1, 1);
                        }

                        return candidate;
                    }

                    return new DateOnly(year, 12, 31);
                }),

            // 6. 偏移表达：3天后 / 两周后 / 1个月后 / 一周后
            new DateRule(
                new Regex(@"(?<!\d)(\d{1,3}|[一二两三四五六七八九十]+)\s*(天|日|周|星期|礼拜|个月)后", opts),
                (_, m, today) =>
                {
                    if (!TryParseCnOrArabicNumber(m.Groups[1].Value, out var n) || n <= 0 || n > 365)
                    {
                        return null;
                    }

                    return m.Groups[2].Value switch
                    {
                        "天" or "日" => today.AddDays(n),
                        "周" or "星期" or "礼拜" => today.AddDays(n * 7),
                        "个月" => today.AddMonths(n),
                        _ => null,
                    };
                }),

            // 7. 工作日：下个工作日 / 下一个工作日
            new DateRule(
                new Regex(@"下一?个工作日", opts),
                (_, _, today) =>
                {
                    var d = today.AddDays(1);
                    while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    {
                        d = d.AddDays(1);
                    }

                    return d;
                }),

            // 8. 星期表达：下下周一 / 下周三 / 本周五 / 这周日 / 周五 / 星期三 / 礼拜天 / 单独「下周」
            new DateRule(
                new Regex(@"(下下|上|本|这|下)?(?:周|星期|礼拜)([一二三四五六日天末])|(下周)(?![一二三四五六日天末])", opts),
                (text, m, today) =>
                {
                    if (m.Groups[3].Success)
                    {
                        var nextIdx = m.Index + m.Length;
                        if (nextIdx < text.Length && WeekdaySuffixExclusions.Contains(text[nextIdx]))
                        {
                            return null;
                        }

                        return today.AddDays(7);
                    }

                    var endIdx = m.Index + m.Length;
                    if (endIdx < text.Length && WeekdaySuffixExclusions.Contains(text[endIdx]))
                    {
                        return null;
                    }

                    var targetDow = m.Groups[2].Value switch
                    {
                        "一" => DayOfWeek.Monday,
                        "二" => DayOfWeek.Tuesday,
                        "三" => DayOfWeek.Wednesday,
                        "四" => DayOfWeek.Thursday,
                        "五" => DayOfWeek.Friday,
                        "六" or "末" => DayOfWeek.Saturday,
                        "日" or "天" => DayOfWeek.Sunday,
                        _ => (DayOfWeek)(-1),
                    };
                    if ((int)targetDow < 0)
                    {
                        return null;
                    }

                    var todayOffsetFromMon = ((int)today.DayOfWeek + 6) % 7;
                    var mondayOfThisWeek = today.AddDays(-todayOffsetFromMon);
                    var targetOffsetFromMon = ((int)targetDow + 6) % 7;
                    var prefix = m.Groups[1].Value;

                    return prefix switch
                    {
                        "上" => mondayOfThisWeek.AddDays(targetOffsetFromMon - 7),
                        "本" or "这" => mondayOfThisWeek.AddDays(targetOffsetFromMon),
                        "下" => mondayOfThisWeek.AddDays(targetOffsetFromMon + 7),
                        "下下" => mondayOfThisWeek.AddDays(targetOffsetFromMon + 14),
                        _ => ResolveNearestWeekday(today, targetDow),
                    };
                }),

            // 9. 月日：3月5日 / 12月1号 / 三月五日 / 3/5 / 12-05
            new DateRule(
                new Regex(@"(?<!\d)(?:(1[0-2]|0?[1-9]|十[一二]?|[一二三四五六七八九])\s*月\s*([12]\d|3[01]|0?[1-9]|三十[一]?|二十[一二三四五六七八九]?|廿[一二三四五六七八九]?|十[一二三四五六七八九]?|[一二三四五六七八九])\s*[日号]|(1[0-2]|0?[1-9])[/.-]([12]\d|3[01]|0?[1-9]))(?!\d)", opts),
                (text, m, today) =>
                {
                    if (HasNumberExclusionSuffix(text, m))
                    {
                        return null;
                    }

                    int month, day;
                    if (m.Groups[1].Success)
                    {
                        if (!TryParseCnOrArabicNumber(m.Groups[1].Value, out month) ||
                            !TryParseCnOrArabicNumber(m.Groups[2].Value, out day))
                        {
                            return null;
                        }
                    }
                    else
                    {
                        if (m.Index > 0 && (char.IsAsciiLetter(text[m.Index - 1]) || text[m.Index - 1] is '.' or '/' or '-'))
                        {
                            return null;
                        }

                        var after = m.Index + m.Length;
                        if (after < text.Length && text[after] is '.' or '/' or '-' or '%')
                        {
                            return null;
                        }

                        if (!int.TryParse(m.Groups[3].Value, out month) ||
                            !int.TryParse(m.Groups[4].Value, out day))
                        {
                            return null;
                        }
                    }

                    return TryFindNearestMonthDay(month, day, today, out var d) ? d : null;
                }),

            // 10. 相对日：大后天 / 后天 / 明天 / 今天 / 昨天 / 前天 / 今晚 / 明晚 / 明早
            new DateRule(
                new Regex(@"(大后天|后天|明天|明早|明晚|今天|今晚|今早|昨天|前天)", opts),
                (_, m, today) => m.Value switch
                {
                    "前天" => today.AddDays(-2),
                    "昨天" => today.AddDays(-1),
                    "今天" or "今晚" or "今早" => today,
                    "明天" or "明早" or "明晚" => today.AddDays(1),
                    "后天" => today.AddDays(2),
                    "大后天" => today.AddDays(3),
                    _ => null,
                }),

            // 11. 单独「Y号」：15号 / 二十号（严格排除「第2号」「5号线」「3号楼」「编号12号」「2023年报」）
            new DateRule(
                new Regex(@"(?<![\d第月年#])([12]\d|3[01]|0?[1-9]|三十[一]?|二十[一二三四五六七八九]?|廿[一二三四五六七八九]?|十[一二三四五六七八九]?|[一二三四五六七八九])号", opts),
                (text, m, today) =>
                {
                    if (HasNumberExclusionSuffix(text, m))
                    {
                        return null;
                    }

                    if (m.Index > 0)
                    {
                        var before = text.AsSpan(0, m.Index).TrimEnd(" :：#");
                        if (!before.IsEmpty && before[^1] is '号' or '编' or '型' or '牌' or '学' or '工' or '房' or '车' or '桌' or '位' or '单' or '座' or '排' or '栋' or '楼' or '序' or '票' or '账' or '卡' or '款')
                        {
                            return null;
                        }
                    }

                    if (!TryParseCnOrArabicNumber(m.Groups[1].Value, out var day) || day is < 1 or > 31)
                    {
                        return null;
                    }

                    var cursor = new DateOnly(today.Year, today.Month, 1);
                    for (var i = 0; i < 12; i++)
                    {
                        if (day <= DateTime.DaysInMonth(cursor.Year, cursor.Month))
                        {
                            var candidate = new DateOnly(cursor.Year, cursor.Month, day);
                            if (candidate >= today)
                            {
                                return candidate;
                            }
                        }

                        cursor = cursor.AddMonths(1);
                    }

                    return null;
                }),
        ];
    }

    private static bool HasNumberExclusionSuffix(string text, Match m)
    {
        var end = m.Index + m.Length;
        if (end >= text.Length)
        {
            return false;
        }

        if (NumberSuffixExclusions.Contains(text[end]))
        {
            return true;
        }

        // 「号」后紧跟「单元」或「电池」
        if (end + 1 < text.Length &&
            (text.AsSpan(end, 2).SequenceEqual("单元") || text.AsSpan(end, 2).SequenceEqual("电池")))
        {
            return true;
        }

        return false;
    }

    private static DateOnly ResolveNearestWeekday(DateOnly today, DayOfWeek target)
    {
        var diff = ((int)target - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(diff);
    }

    private static bool TryFindNearestMonthDay(int month, int day, DateOnly today, out DateOnly result)
    {
        result = default;
        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            return false;
        }

        // 检查今年（含闰年校验，2月30日直接无效）
        if (month == 2 && day > 29)
        {
            return false;
        }

        for (var y = today.Year; y <= today.Year + 4; y++)
        {
            if (TryCreateDate(y, month, day, out var candidate) && candidate >= today)
            {
                result = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateDate(int year, int month, int day, out DateOnly date)
    {
        date = default;
        if (year is < 1900 or > 2100 || month is < 1 or > 12)
        {
            return false;
        }

        if (day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    private static bool TryParseLunarMonth(string token, out int month)
    {
        month = token switch
        {
            "正" => 1,
            "冬" => 11,
            "腊" => 12,
            _ => TryParseCnOrArabicNumber(token, out var n) ? n : 0,
        };
        return month is >= 1 and <= 12;
    }

    private static bool TryParseLunarDay(string token, out int day)
    {
        if (token.StartsWith('初'))
        {
            return TryParseCnOrArabicNumber(token[1..], out day) && day is >= 1 and <= 10;
        }

        return TryParseCnOrArabicNumber(token, out day) && day is >= 1 and <= 30;
    }

    private static bool TryParseCnOrArabicNumber(string token, out int value)
    {
        if (int.TryParse(token, out value))
        {
            return true;
        }

        value = 0;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        if (token[0] == '廿')
        {
            if (token.Length == 1)
            {
                value = 20;
                return true;
            }

            if (TryDigit(token[1], out var ones))
            {
                value = 20 + ones;
                return true;
            }

            return false;
        }

        var total = 0;
        var current = 0;
        foreach (var ch in token)
        {
            if (ch == '十')
            {
                total += current == 0 ? 10 : current * 10;
                current = 0;
            }
            else if (TryDigit(ch, out var d))
            {
                current = d;
            }
            else
            {
                return false;
            }
        }

        value = total + current;
        return value > 0;
    }

    private static bool TryDigit(char ch, out int d)
    {
        d = ch switch
        {
            '〇' or '零' => 0,
            '一' => 1,
            '二' or '两' => 2,
            '三' => 3,
            '四' => 4,
            '五' => 5,
            '六' => 6,
            '七' => 7,
            '八' => 8,
            '九' => 9,
            _ => -1,
        };
        return d >= 0;
    }

    private sealed record DateRule(Regex Pattern, Func<string, Match, DateOnly, DateOnly?> Resolve);
}
