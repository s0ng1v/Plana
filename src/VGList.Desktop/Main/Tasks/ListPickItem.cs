using Avalonia.Media;

namespace VGList.Desktop.Main.Tasks;

/// <summary>任务详情清单菜单的一项。</summary>
public sealed class ListPickItem
{
    public ListPickItem(string id, string name, string color, bool isSelected)
    {
        Id = id;
        Name = name;
        IsSelected = isSelected;
        DotBrush = new SolidColorBrush(Color.Parse(color));
    }

    public string Id { get; }

    public string Name { get; }

    public bool IsSelected { get; }

    public IBrush DotBrush { get; }
}
