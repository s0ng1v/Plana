using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VGList.Desktop.Services;

namespace VGList.Desktop.Main.Assistant;

/// <summary>
/// 轻量零依赖流式 Markdown 富文本渲染附加属性（作用于 SelectableTextBlock，保留原生选字与复制体验）：
/// 1) 支持标题（# / ## / ###）、粗体（**text**）、行内代码（`code`）与无序列表项（- / * ），流式未闭合标记平滑降级不闪烁；
/// 2) 当 IsStreaming=true 且未开启「减少动效」时，为每次 SSE 新抵达的增量片段提供 220ms CubicEaseOut 透明渐入效果，
///    文字字距与换行零抖动，渐入完成后自动停止计时器零常驻开销。
/// </summary>
public static class ChatMarkdown
{
    private const double FadeDurationMs = 220.0;
    private static readonly FontFamily CodeFont = new("Anthropic Mono, Cascadia Mono, Consolas, Courier New, monospace");
    private static readonly ConditionalWeakTable<SelectableTextBlock, StreamFadeState> States = [];

    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<SelectableTextBlock, string?>("Source", typeof(ChatMarkdown));

    public static readonly AttachedProperty<bool> IsStreamingProperty =
        AvaloniaProperty.RegisterAttached<SelectableTextBlock, bool>("IsStreaming", typeof(ChatMarkdown));

    static ChatMarkdown()
    {
        SourceProperty.Changed.AddClassHandler<SelectableTextBlock>((tb, e) =>
            OnSourceChanged(tb, e.NewValue as string));
        IsStreamingProperty.Changed.AddClassHandler<SelectableTextBlock>((tb, _) =>
            OnStreamingStateChanged(tb));
    }

    public static string? GetSource(SelectableTextBlock element) => element.GetValue(SourceProperty);
    public static void SetSource(SelectableTextBlock element, string? value) => element.SetValue(SourceProperty, value);

    public static bool GetIsStreaming(SelectableTextBlock element) => element.GetValue(IsStreamingProperty);
    public static void SetIsStreaming(SelectableTextBlock element, bool value) => element.SetValue(IsStreamingProperty, value);

    private static void OnSourceChanged(SelectableTextBlock tb, string? markdown)
    {
        var state = GetOrCreateState(tb);
        var text = (markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        var now = Environment.TickCount64;

        var animate = GetIsStreaming(tb) && !MotionHelper.ReduceMotion;
        if (animate && text.Length > state.LastLength && text.StartsWith(state.LastText, StringComparison.Ordinal))
        {
            state.FadeSpans.Add(new FadeSpan(state.LastLength, text.Length, now));
            state.EnsureTimerRunning();
        }
        else if (!animate || text.Length < state.LastLength || !text.StartsWith(state.LastText, StringComparison.Ordinal))
        {
            state.FadeSpans.Clear();
            state.StopTimer();
        }

        state.LastText = text;
        state.LastLength = text.Length;
        Render(tb, text, state, now);
    }

    private static void OnStreamingStateChanged(SelectableTextBlock tb)
    {
        if (!States.TryGetValue(tb, out var state))
        {
            return;
        }

        // 流式结束时让已存在的尾部渐入自然跑完；若是减少动效则立即全显
        if (MotionHelper.ReduceMotion && state.FadeSpans.Count > 0)
        {
            state.FadeSpans.Clear();
            state.StopTimer();
            Render(tb, state.LastText, state, Environment.TickCount64);
        }
    }

    private static StreamFadeState GetOrCreateState(SelectableTextBlock tb)
    {
        if (States.TryGetValue(tb, out var existing))
        {
            return existing;
        }

        var state = new StreamFadeState(tb);
        States.Add(tb, state);
        tb.DetachedFromVisualTree += (_, _) => state.StopTimer();
        return state;
    }

    internal static void Render(SelectableTextBlock tb, string markdown, StreamFadeState state, long nowMs)
    {
        var inlines = tb.Inlines;
        if (inlines is null)
        {
            return;
        }

        // 清理已完全渐入的片段
        for (var i = state.FadeSpans.Count - 1; i >= 0; i--)
        {
            if (nowMs - state.FadeSpans[i].CreatedMs >= FadeDurationMs)
            {
                state.FadeSpans.RemoveAt(i);
            }
        }

        if (state.FadeSpans.Count == 0)
        {
            state.StopTimer();
        }

        inlines.Clear();
        if (string.IsNullOrEmpty(markdown))
        {
            return;
        }

        var baseColor = ResolveColor(tb, "Main.Text.Primary", Color.FromRgb(41, 31, 36));
        var accentBrush = ResolveBrush(tb, "Main.Accent");
        var accentColor = (accentBrush as ISolidColorBrush)?.Color ?? Color.FromRgb(228, 107, 146);
        var codeBgBrush = ResolveBrush(tb, "Main.Accent.Soft");
        var codeBgColor = (codeBgBrush as ISolidColorBrush)?.Color ?? Color.FromRgb(253, 236, 242);

        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var offset = 0;
        var lineIndex = 0;

        while (offset <= normalized.Length)
        {
            if (lineIndex > 0)
            {
                inlines.Add(new LineBreak());
            }

            var nl = normalized.IndexOf('\n', offset);
            var lineEnd = nl < 0 ? normalized.Length : nl;
            var lineLen = lineEnd - offset;
            if (lineLen > 0)
            {
                var line = normalized.Substring(offset, lineLen);
                RenderLine(
                    inlines,
                    line,
                    offset,
                    baseColor,
                    accentBrush,
                    accentColor,
                    codeBgBrush,
                    codeBgColor,
                    state.FadeSpans,
                    nowMs);
            }

            if (nl < 0)
            {
                break;
            }

            offset = nl + 1;
            lineIndex++;
        }
    }

    private static void RenderLine(
        InlineCollection inlines,
        string line,
        int lineOffset,
        Color baseColor,
        IBrush? accentBrush,
        Color accentColor,
        IBrush? codeBgBrush,
        Color codeBgColor,
        List<FadeSpan> fadeSpans,
        long nowMs)
    {
        if (line.StartsWith("### ", StringComparison.Ordinal))
        {
            AddInlineRuns(inlines, line[4..], lineOffset + 4, baseColor, accentBrush, accentColor, codeBgBrush, codeBgColor, FontWeight.SemiBold, 14.5, fadeSpans, nowMs);
            return;
        }

        if (line.StartsWith("## ", StringComparison.Ordinal))
        {
            AddInlineRuns(inlines, line[3..], lineOffset + 3, baseColor, accentBrush, accentColor, codeBgBrush, codeBgColor, FontWeight.SemiBold, 15.5, fadeSpans, nowMs);
            return;
        }

        if (line.StartsWith("# ", StringComparison.Ordinal))
        {
            AddInlineRuns(inlines, line[2..], lineOffset + 2, baseColor, accentBrush, accentColor, codeBgBrush, codeBgColor, FontWeight.Bold, 16.5, fadeSpans, nowMs);
            return;
        }

        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
        {
            var alpha = ComputeAlpha(lineOffset, fadeSpans, nowMs);
            var bulletBrush = alpha < 0.999 ? WithAlpha(accentColor, alpha) : accentBrush;
            inlines.Add(new Run("•  ") { Foreground = bulletBrush, FontWeight = FontWeight.Bold });
            AddInlineRuns(inlines, line[2..], lineOffset + 2, baseColor, accentBrush, accentColor, codeBgBrush, codeBgColor, FontWeight.Regular, null, fadeSpans, nowMs);
            return;
        }

        AddInlineRuns(inlines, line, lineOffset, baseColor, accentBrush, accentColor, codeBgBrush, codeBgColor, FontWeight.Regular, null, fadeSpans, nowMs);
    }

    private static void AddInlineRuns(
        InlineCollection inlines,
        string line,
        int baseOffset,
        Color baseColor,
        IBrush? accentBrush,
        Color accentColor,
        IBrush? codeBgBrush,
        Color codeBgColor,
        FontWeight baseWeight,
        double? fontSize,
        List<FadeSpan> fadeSpans,
        long nowMs)
    {
        var idx = 0;
        while (idx < line.Length)
        {
            var nextBold = line.IndexOf("**", idx, StringComparison.Ordinal);
            var nextCode = line.IndexOf('`', idx);

            var nextToken = -1;
            var isBold = false;
            if (nextBold >= 0 && (nextCode < 0 || nextBold < nextCode))
            {
                nextToken = nextBold;
                isBold = true;
            }
            else if (nextCode >= 0)
            {
                nextToken = nextCode;
            }

            if (nextToken < 0)
            {
                EmitFadeAwareRuns(inlines, line[idx..], baseOffset + idx, baseWeight, fontSize, baseColor, null, null, null, null, fadeSpans, nowMs);
                break;
            }

            if (nextToken > idx)
            {
                EmitFadeAwareRuns(inlines, line[idx..nextToken], baseOffset + idx, baseWeight, fontSize, baseColor, null, null, null, null, fadeSpans, nowMs);
            }

            if (isBold)
            {
                var close = line.IndexOf("**", nextToken + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    var rest = line[(nextToken + 2)..];
                    if (rest.Length > 0)
                    {
                        EmitFadeAwareRuns(inlines, rest, baseOffset + nextToken + 2, FontWeight.SemiBold, fontSize, baseColor, null, null, null, null, fadeSpans, nowMs);
                    }

                    break;
                }

                EmitFadeAwareRuns(inlines, line[(nextToken + 2)..close], baseOffset + nextToken + 2, FontWeight.SemiBold, fontSize, baseColor, null, null, null, null, fadeSpans, nowMs);
                idx = close + 2;
            }
            else
            {
                var close = line.IndexOf('`', nextToken + 1);
                if (close < 0)
                {
                    EmitFadeAwareRuns(inlines, line[nextToken..], baseOffset + nextToken, baseWeight, fontSize, baseColor, null, null, null, null, fadeSpans, nowMs);
                    break;
                }

                var code = line[(nextToken + 1)..close];
                EmitFadeAwareRuns(inlines, $" {code} ", baseOffset + nextToken + 1, FontWeight.Medium, (fontSize ?? 14) - 1, accentColor, accentBrush, codeBgColor, codeBgBrush, CodeFont, fadeSpans, nowMs);
                idx = close + 1;
            }
        }
    }

    private static void EmitFadeAwareRuns(
        InlineCollection inlines,
        string text,
        int docStart,
        FontWeight weight,
        double? fontSize,
        Color fgColor,
        IBrush? steadyFg,
        Color? bgColor,
        IBrush? steadyBg,
        FontFamily? fontFamily,
        List<FadeSpan> fadeSpans,
        long nowMs)
    {
        if (text.Length == 0)
        {
            return;
        }

        var docEnd = docStart + text.Length;
        if (fadeSpans.Count == 0 || docEnd <= fadeSpans[0].Start)
        {
            AddRun(inlines, text, weight, fontSize, steadyFg, steadyBg, fontFamily);
            return;
        }

        // 将落在渐入区间的段落按 chunk 边界切分，赋予平滑递增的透明度
        var cursor = docStart;
        foreach (var span in fadeSpans)
        {
            if (span.End <= cursor || span.Start >= docEnd)
            {
                continue;
            }

            if (span.Start > cursor)
            {
                var beforeLen = span.Start - cursor;
                AddRun(inlines, text.Substring(cursor - docStart, beforeLen), weight, fontSize, steadyFg, steadyBg, fontFamily);
                cursor = span.Start;
            }

            var segEnd = Math.Min(span.End, docEnd);
            var segLen = segEnd - cursor;
            if (segLen > 0)
            {
                var alpha = EaseFade(nowMs - span.CreatedMs);
                var fg = alpha < 0.999 ? WithAlpha(fgColor, alpha) : steadyFg;
                var bg = bgColor.HasValue ? (alpha < 0.999 ? WithAlpha(bgColor.Value, alpha) : steadyBg) : null;
                AddRun(inlines, text.Substring(cursor - docStart, segLen), weight, fontSize, fg, bg, fontFamily);
                cursor = segEnd;
            }
        }

        if (cursor < docEnd)
        {
            AddRun(inlines, text[(cursor - docStart)..], weight, fontSize, steadyFg, steadyBg, fontFamily);
        }
    }

    private static double ComputeAlpha(int docIndex, List<FadeSpan> fadeSpans, long nowMs)
    {
        foreach (var span in fadeSpans)
        {
            if (docIndex >= span.Start && docIndex < span.End)
            {
                return EaseFade(nowMs - span.CreatedMs);
            }
        }

        return 1.0;
    }

    private static double EaseFade(long elapsedMs)
    {
        if (elapsedMs <= 0)
        {
            return 0.08;
        }

        if (elapsedMs >= FadeDurationMs)
        {
            return 1.0;
        }

        var t = elapsedMs / FadeDurationMs;
        // CubicEaseOut：开头轻快显现、末尾丝滑落稳
        var eased = 1.0 - Math.Pow(1.0 - t, 3.0);
        return 0.08 + 0.92 * eased;
    }

    private static SolidColorBrush WithAlpha(Color color, double factor)
    {
        var a = (byte)Math.Clamp(Math.Round(color.A * factor), 0, 255);
        return new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
    }

    private static void AddRun(
        InlineCollection inlines,
        string text,
        FontWeight weight,
        double? fontSize,
        IBrush? foreground,
        IBrush? background,
        FontFamily? fontFamily)
    {
        if (text.Length == 0)
        {
            return;
        }

        var run = new Run(text) { FontWeight = weight };
        if (fontSize.HasValue)
        {
            run.FontSize = fontSize.Value;
        }

        if (foreground is not null)
        {
            run.Foreground = foreground;
        }

        if (background is not null)
        {
            run.Background = background;
        }

        if (fontFamily is not null)
        {
            run.FontFamily = fontFamily;
        }

        inlines.Add(run);
    }

    private static IBrush? ResolveBrush(Control target, string key) =>
        target.TryFindResource(key, target.ActualThemeVariant, out var value) ? value as IBrush : null;

    private static Color ResolveColor(Control target, string key, Color fallback) =>
        ResolveBrush(target, key) is ISolidColorBrush solid ? solid.Color : fallback;

    internal sealed record FadeSpan(int Start, int End, long CreatedMs);

    internal sealed class StreamFadeState(SelectableTextBlock target)
    {
        private readonly SelectableTextBlock _target = target;
        private DispatcherTimer? _timer;

        public string LastText { get; set; } = string.Empty;
        public int LastLength { get; set; }
        public List<FadeSpan> FadeSpans { get; } = [];

        public void EnsureTimerRunning()
        {
            if (_timer is null)
            {
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnTick);
            }

            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }

        public void StopTimer() => _timer?.Stop();

        private void OnTick(object? sender, EventArgs e)
        {
            if (FadeSpans.Count == 0 || !_target.IsAttachedToVisualTree())
            {
                StopTimer();
                return;
            }

            Render(_target, LastText, this, Environment.TickCount64);
        }
    }
}
