using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace VGList.Desktop.Main.Assistant;

public enum MarkdownColumnAlign
{
    Left,
    Center,
    Right,
}

public abstract record MarkdownBlock;

public sealed record MarkdownProseBlock(string Markdown) : MarkdownBlock;

public sealed record MarkdownTableBlock(
    IReadOnlyList<string> Headers,
    IReadOnlyList<MarkdownColumnAlign> Alignments,
    IReadOnlyList<IReadOnlyList<string>> Rows) : MarkdownBlock;

public sealed record MarkdownCodeBlock(string Language, string Code) : MarkdownBlock;

/// <summary>
/// 块级流式 Markdown 渲染控件（ADR-0047）：
/// - 支持标准 Markdown 表格（| ... | + |---|---| 分隔行，含单元格内粗体/行内代码、水平滚动、表头柔底与斑马纹）；
/// - 支持围栏代码块（```lang）；
/// - 普通正文/标题/列表复用 ChatMarkdown，保留 220ms SSE 尾部柔滑渐入与原生选字复制；
/// - 流式增量更新时按块复用已有控件，已稳定段落与表格零闪烁。
/// </summary>
public sealed class ChatMarkdownView : Border
{
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<ChatMarkdownView, string?>(nameof(Source));

    public static readonly StyledProperty<bool> IsStreamingProperty =
        AvaloniaProperty.Register<ChatMarkdownView, bool>(nameof(IsStreaming));

    private readonly StackPanel _host;
    private IReadOnlyList<MarkdownBlock> _renderedBlocks = [];

    static ChatMarkdownView()
    {
        SourceProperty.Changed.AddClassHandler<ChatMarkdownView>((v, _) => v.Rebuild());
        IsStreamingProperty.Changed.AddClassHandler<ChatMarkdownView>((v, _) => v.SyncStreamingFlag());
    }

    public ChatMarkdownView()
    {
        _host = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
        };
        Child = _host;
    }

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool IsStreaming
    {
        get => GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    /// <summary>将 Markdown 文本切分为正文块、表格块与围栏代码块（纯函数，可直接单测）。</summary>
    public static IReadOnlyList<MarkdownBlock> ParseBlocks(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return [];
        }

        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var blocks = new List<MarkdownBlock>();
        var prose = new List<string>();

        void FlushProse()
        {
            if (prose.Count == 0)
            {
                return;
            }

            // 去掉首尾纯空行
            var start = 0;
            var end = prose.Count - 1;
            while (start <= end && string.IsNullOrWhiteSpace(prose[start])) start++;
            while (end >= start && string.IsNullOrWhiteSpace(prose[end])) end--;
            if (start <= end)
            {
                blocks.Add(new MarkdownProseBlock(string.Join('\n', prose.Skip(start).Take(end - start + 1))));
            }

            prose.Clear();
        }

        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // 1. 围栏代码块 ```
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushProse();
                var lang = trimmed[3..].Trim();
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }

                if (i < lines.Length)
                {
                    i++; // 跳过闭合 ```
                }

                blocks.Add(new MarkdownCodeBlock(lang, string.Join('\n', codeLines)));
                continue;
            }

            // 2. Markdown 表格：当前行含 | 且下一行是合法的 |---|---| 分隔行
            if (i + 1 < lines.Length && LooksLikeTableRow(line) && TryParseSeparatorRow(lines[i + 1], out var aligns))
            {
                var headers = SplitTableCells(line);
                if (headers.Count >= 1 && aligns.Count >= 1)
                {
                    FlushProse();
                    var colCount = Math.Max(headers.Count, aligns.Count);
                    var normalizedHeaders = PadCells(headers, colCount);
                    var normalizedAligns = PadAligns(aligns, colCount);
                    var rows = new List<IReadOnlyList<string>>();

                    i += 2;
                    while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && LooksLikeTableRow(lines[i]))
                    {
                        var cells = SplitTableCells(lines[i]);
                        rows.Add(PadCells(cells, colCount));
                        i++;
                    }

                    blocks.Add(new MarkdownTableBlock(normalizedHeaders, normalizedAligns, rows));
                    continue;
                }
            }

            prose.Add(line);
            i++;
        }

        FlushProse();
        return blocks;
    }

    private void Rebuild()
    {
        var blocks = ParseBlocks(Source);
        var streaming = IsStreaming;

        // 增量复用已有块，避免流式输出时重绘前面已稳定的段落与表格
        for (var idx = 0; idx < blocks.Count; idx++)
        {
            var block = blocks[idx];
            if (idx < _host.Children.Count && idx < _renderedBlocks.Count)
            {
                if (BlockEquals(_renderedBlocks[idx], block))
                {
                    continue;
                }

                if (block is MarkdownProseBlock pb && _host.Children[idx] is SelectableTextBlock existingTb)
                {
                    ChatMarkdown.SetIsStreaming(existingTb, streaming);
                    ChatMarkdown.SetSource(existingTb, pb.Markdown);
                    continue;
                }

                _host.Children[idx] = BuildControl(block, streaming);
            }
            else
            {
                _host.Children.Add(BuildControl(block, streaming));
            }
        }

        while (_host.Children.Count > blocks.Count)
        {
            _host.Children.RemoveAt(_host.Children.Count - 1);
        }

        _renderedBlocks = blocks;
    }

    private void SyncStreamingFlag()
    {
        var streaming = IsStreaming;
        foreach (var child in _host.Children)
        {
            if (child is SelectableTextBlock tb)
            {
                ChatMarkdown.SetIsStreaming(tb, streaming);
            }
        }
    }

    private Control BuildControl(MarkdownBlock block, bool streaming) => block switch
    {
        MarkdownProseBlock prose => BuildProseControl(prose.Markdown, streaming),
        MarkdownTableBlock table => BuildTableControl(table),
        MarkdownCodeBlock code => BuildCodeControl(code),
        _ => new Panel(),
    };

    private static SelectableTextBlock BuildProseControl(string markdown, bool streaming)
    {
        var tb = new SelectableTextBlock
        {
            FontSize = 14,
            LineHeight = 24,
            TextWrapping = TextWrapping.Wrap,
        };
        tb.Bind(TextBlock.FontFamilyProperty, tb.GetResourceObservable("Font.Display"));
        tb.Bind(TextBlock.ForegroundProperty, tb.GetResourceObservable("Main.Text.Primary"));
        ChatMarkdown.SetIsStreaming(tb, streaming);
        ChatMarkdown.SetSource(tb, markdown);
        return tb;
    }

    /// <summary>按列文本视觉宽度估算紧凑度，便于智能分配自适应列宽。</summary>
    public static double EstimateVisualWidth(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        double score = 0;
        foreach (var ch in text.Trim())
        {
            if (ch == '`' || ch == '*')
            {
                continue;
            }

            score += ch > 0x7F ? 1.85 : 1.0;
        }

        return score;
    }

    /// <summary>
    /// 计算 Markdown 表格列宽分配（ADR-0058）：
    /// 取消横向滚动条，短列（日期/优先级/状态）使用 Auto 不折行，长文本列使用加权 * 比例列宽自动换行，行高自适应内容。
    /// </summary>
    public static IReadOnlyList<GridLength> ComputeTableColumnWidths(MarkdownTableBlock table)
    {
        var colCount = table.Headers.Count;
        if (colCount <= 0)
        {
            return [];
        }

        var maxScores = new double[colCount];
        for (var c = 0; c < colCount; c++)
        {
            maxScores[c] = EstimateVisualWidth(table.Headers[c]);
        }

        foreach (var row in table.Rows)
        {
            for (var c = 0; c < colCount && c < row.Count; c++)
            {
                var w = EstimateVisualWidth(row[c]);
                if (w > maxScores[c])
                {
                    maxScores[c] = w;
                }
            }
        }

        // 若列较多（>=5列）或所有列都很宽，统一按比例 * 分配确保整表完整落入容器不溢出
        var widths = new GridLength[colCount];
        var isAuto = new bool[colCount];
        var autoCount = 0;
        var autoScoreSum = 0.0;

        if (colCount <= 4)
        {
            for (var c = 0; c < colCount; c++)
            {
                if (maxScores[c] <= 15.0 && autoScoreSum + maxScores[c] <= 30.0)
                {
                    widths[c] = GridLength.Auto;
                    isAuto[c] = true;
                    autoCount++;
                    autoScoreSum += maxScores[c];
                }
            }
        }

        // 确保至少有 1 列（优先最宽列）承担 * 自适应换行，否则紧凑短表也可用 Auto
        var totalScore = maxScores.Sum();
        if (autoCount == colCount && (colCount >= 3 || totalScore > 36.0))
        {
            var widestIdx = 0;
            for (var c = 1; c < colCount; c++)
            {
                if (maxScores[c] > maxScores[widestIdx])
                {
                    widestIdx = c;
                }
            }

            isAuto[widestIdx] = false;
            autoCount--;
        }

        for (var c = 0; c < colCount; c++)
        {
            if (isAuto[c])
            {
                continue;
            }

            var weight = Math.Clamp(Math.Sqrt(Math.Max(maxScores[c], 4.0)), 1.2, 4.2);
            widths[c] = new GridLength(weight, GridUnitType.Star);
        }

        return widths;
    }

    private static Control BuildTableControl(MarkdownTableBlock table)
    {
        var colCount = table.Headers.Count;
        var colWidths = ComputeTableColumnWidths(table);
        var hasStarCol = colWidths.Any(w => w.IsStar);

        var grid = new Grid();
        for (var c = 0; c < colCount; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(colWidths[c]));
        }

        // 表头 + 数据行（每行高度随换行内容自适应）
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var r = 0; r < table.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        // 表头背景
        var headerBg = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        headerBg.Bind(Border.BackgroundProperty, headerBg.GetResourceObservable("Markdown.Table.HeaderBg"));
        headerBg.Bind(Border.BorderBrushProperty, headerBg.GetResourceObservable("Markdown.Table.Border"));
        Grid.SetRow(headerBg, 0);
        Grid.SetColumnSpan(headerBg, colCount);
        grid.Children.Add(headerBg);

        // 表头单元格
        for (var c = 0; c < colCount; c++)
        {
            var cell = BuildTableCell(
                table.Headers[c],
                isHeader: true,
                align: table.Alignments[c],
                showRightBorder: c < colCount - 1,
                showBottomBorder: false);
            Grid.SetRow(cell, 0);
            Grid.SetColumn(cell, c);
            grid.Children.Add(cell);
        }

        // 数据行
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var rowIdx = r + 1;
            var isLast = r == table.Rows.Count - 1;

            if (r % 2 == 1)
            {
                var stripe = new Border();
                stripe.Bind(Border.BackgroundProperty, stripe.GetResourceObservable("Markdown.Table.StripeBg"));
                Grid.SetRow(stripe, rowIdx);
                Grid.SetColumnSpan(stripe, colCount);
                grid.Children.Add(stripe);
            }

            for (var c = 0; c < colCount; c++)
            {
                var cell = BuildTableCell(
                    table.Rows[r][c],
                    isHeader: false,
                    align: table.Alignments[c],
                    showRightBorder: c < colCount - 1,
                    showBottomBorder: !isLast);
                Grid.SetRow(cell, rowIdx);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        // 取消横向 ScrollViewer，含 * 弹性列时直接铺满气泡宽度并自动换行显示完整内容
        var outer = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            HorizontalAlignment = hasStarCol ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            Margin = new Thickness(0, 2, 0, 2),
            Child = grid,
        };
        outer.Bind(Border.BackgroundProperty, outer.GetResourceObservable("Main.Bg"));
        outer.Bind(Border.BorderBrushProperty, outer.GetResourceObservable("Markdown.Table.Border"));
        return outer;
    }

    private static Border BuildTableCell(
        string rawText,
        bool isHeader,
        MarkdownColumnAlign align,
        bool showRightBorder,
        bool showBottomBorder)
    {
        var tb = new SelectableTextBlock
        {
            FontSize = isHeader ? 12.5 : 13,
            FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Regular,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = align switch
            {
                MarkdownColumnAlign.Center => HorizontalAlignment.Center,
                MarkdownColumnAlign.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Stretch,
            },
            TextAlignment = align switch
            {
                MarkdownColumnAlign.Center => TextAlignment.Center,
                MarkdownColumnAlign.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
            VerticalAlignment = VerticalAlignment.Center,
        };
        tb.Bind(TextBlock.FontFamilyProperty, tb.GetResourceObservable("Font.Display"));
        tb.Bind(TextBlock.ForegroundProperty, tb.GetResourceObservable(isHeader ? "Main.Text.Secondary" : "Main.Text.Primary"));
        ChatMarkdown.SetIsStreaming(tb, false);
        ChatMarkdown.SetSource(tb, rawText);

        var cellBorder = new Border
        {
            MinWidth = 0,
            Padding = new Thickness(10, isHeader ? 8 : 7),
            BorderThickness = new Thickness(0, 0, showRightBorder ? 1 : 0, showBottomBorder ? 1 : 0),
            Child = tb,
        };
        cellBorder.Bind(Border.BorderBrushProperty, cellBorder.GetResourceObservable("Main.Border.Subtle"));
        return cellBorder;
    }

    private static Control BuildCodeControl(MarkdownCodeBlock code)
    {
        var stack = new StackPanel { Spacing = 0 };
        if (!string.IsNullOrWhiteSpace(code.Language))
        {
            var langText = new TextBlock
            {
                Text = code.Language,
                FontSize = 11,
                FontWeight = FontWeight.Medium,
            };
            langText.Bind(TextBlock.ForegroundProperty, langText.GetResourceObservable("Main.Text.Tertiary"));

            var langHeader = new Border
            {
                Padding = new Thickness(12, 5),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = langText,
            };
            langHeader.Bind(Border.BorderBrushProperty, langHeader.GetResourceObservable("Markdown.Code.Border"));
            stack.Children.Add(langHeader);
        }

        var codeText = new SelectableTextBlock
        {
            Text = code.Code,
            FontSize = 12.5,
            LineHeight = 20,
            Margin = new Thickness(12, 9),
        };
        codeText.Bind(TextBlock.FontFamilyProperty, codeText.GetResourceObservable("Font.Mono"));
        codeText.Bind(TextBlock.ForegroundProperty, codeText.GetResourceObservable("Main.Text.Primary"));

        stack.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = codeText,
        });

        var outer = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = stack,
        };
        outer.Bind(Border.BackgroundProperty, outer.GetResourceObservable("Markdown.Code.Bg"));
        outer.Bind(Border.BorderBrushProperty, outer.GetResourceObservable("Markdown.Code.Border"));
        return outer;
    }

    private static bool LooksLikeTableRow(string line)
    {
        var t = line.Trim();
        return t.Contains('|', StringComparison.Ordinal) && t.Length >= 3;
    }

    private static bool TryParseSeparatorRow(string line, out List<MarkdownColumnAlign> aligns)
    {
        aligns = [];
        var trimmed = line.Trim();
        if (!trimmed.Contains('-', StringComparison.Ordinal) || !trimmed.Contains('|', StringComparison.Ordinal))
        {
            return false;
        }

        var cells = SplitTableCells(trimmed);
        if (cells.Count == 0)
        {
            return false;
        }

        foreach (var raw in cells)
        {
            var cell = raw.Trim();
            if (cell.Length == 0)
            {
                return false;
            }

            var leftColon = cell.StartsWith(':');
            var rightColon = cell.EndsWith(':');
            var core = cell.Trim(':');
            if (core.Length < 1 || core.Any(ch => ch != '-'))
            {
                return false;
            }

            aligns.Add((leftColon, rightColon) switch
            {
                (true, true) => MarkdownColumnAlign.Center,
                (false, true) => MarkdownColumnAlign.Right,
                _ => MarkdownColumnAlign.Left,
            });
        }

        return true;
    }

    private static List<string> SplitTableCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('|') && !trimmed.EndsWith("\\|", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^1];
        }

        var cells = new List<string>();
        var sb = new StringBuilder();
        var escaped = false;
        foreach (var ch in trimmed)
        {
            if (escaped)
            {
                sb.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (ch == '|')
            {
                cells.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }

        cells.Add(sb.ToString().Trim());
        return cells;
    }

    private static IReadOnlyList<string> PadCells(List<string> cells, int count)
    {
        if (cells.Count == count) return cells;
        var res = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            res.Add(i < cells.Count ? cells[i] : string.Empty);
        }

        return res;
    }

    private static IReadOnlyList<MarkdownColumnAlign> PadAligns(List<MarkdownColumnAlign> aligns, int count)
    {
        if (aligns.Count == count) return aligns;
        var res = new List<MarkdownColumnAlign>(count);
        for (var i = 0; i < count; i++)
        {
            res.Add(i < aligns.Count ? aligns[i] : MarkdownColumnAlign.Left);
        }

        return res;
    }

    private static bool BlockEquals(MarkdownBlock a, MarkdownBlock b)
    {
        if (ReferenceEquals(a, b)) return true;
        return (a, b) switch
        {
            (MarkdownProseBlock pa, MarkdownProseBlock pb) => pa.Markdown == pb.Markdown,
            (MarkdownCodeBlock ca, MarkdownCodeBlock cb) => ca.Language == cb.Language && ca.Code == cb.Code,
            (MarkdownTableBlock ta, MarkdownTableBlock tb) =>
                ta.Headers.SequenceEqual(tb.Headers) &&
                ta.Rows.Count == tb.Rows.Count &&
                ta.Rows.Zip(tb.Rows).All(pair => pair.First.SequenceEqual(pair.Second)),
            _ => false,
        };
    }
}
