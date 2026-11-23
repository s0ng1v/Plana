namespace VGList.Platform;

/// <summary>剪贴板文件/图片读取。macOS 暂未实现时返回 false，不影响文本粘贴。</summary>
public interface IClipboardReader
{
    bool TryGetFilePaths(out string[] paths);

    bool TryGetImageBytes(out byte[] bytes, out bool isPng);
}
