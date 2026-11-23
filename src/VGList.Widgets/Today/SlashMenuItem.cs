namespace VGList.Widgets.Today;

/// <summary>内容区「/」快捷插入菜单的一项。</summary>
public sealed record SlashMenuItem(string Glyph, string Label, string Snippet, bool IsBlock = true)
{
    public static IReadOnlyList<SlashMenuItem> All { get; } =
    [
        new("H1", "一级标题", "# "),
        new("H2", "二级标题", "## "),
        new("H3", "三级标题", "### "),
        new("•", "无序列表", "- "),
        new("1.", "有序列表", "1. "),
        new("✓", "检查项", "- [ ] "),
        new("“", "引用", "> "),
        new("—", "水平分割线", "\n---\n"),
        new("⊕", "附件", "![](链接)", IsBlock: false),
        new("↳", "子任务", "- [ ] "),
        new("#", "标签", "#标签 ", IsBlock: false),
        new("@", "关联任务/笔记", "[[链接]]", IsBlock: false),
    ];
}
