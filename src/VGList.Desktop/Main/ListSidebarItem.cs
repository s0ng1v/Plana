using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VGList.Desktop.Main;

/// <summary>侧栏自建清单项：名称、未完成计数、选中与就地重命名状态。</summary>
public sealed partial class ListSidebarItem : ObservableObject
{
    public ListSidebarItem(string id, string name, string color, int count)
    {
        Id = id;
        _name = name;
        _editName = name;
        _count = count;
        DotBrush = new SolidColorBrush(Color.Parse(color));
    }

    public string Id { get; }

    public IBrush DotBrush { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName;

    public void BeginEdit()
    {
        EditName = Name;
        IsEditing = true;
    }

    public void EndEdit() => IsEditing = false;
}
