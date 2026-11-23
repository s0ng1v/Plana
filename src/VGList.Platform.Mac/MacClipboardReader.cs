using VGList.Platform;

namespace VGList.Platform.Mac;

/// <summary>剪贴板文件/图片读取暂未接入 NSPasteboard；文本粘贴仍由 Avalonia 负责。</summary>
public sealed class MacClipboardReader : IClipboardReader
{
    public bool TryGetFilePaths(out string[] paths)
    {
        paths = [];
        return false;
    }

    public bool TryGetImageBytes(out byte[] bytes, out bool isPng)
    {
        bytes = [];
        isPng = false;
        return false;
    }
}
