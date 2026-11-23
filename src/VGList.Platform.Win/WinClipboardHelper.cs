using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace VGList.Platform.Win;

/// <summary>
/// Windows 原生剪贴板媒体/文件读取辅助（零额外依赖）：
/// 支持直接读取 Win+Shift+S / 微信截图的图像（PNG / CF_DIB）以及资源管理器复制的文件列表（CF_HDROP）。
/// </summary>
public static class WinClipboardHelper
{
    private const uint CfDib = 8;
    private const uint CfHDrop = 15;
    private const uint CfDibV5 = 17;
    private const uint BiBitfields = 3;

    private static readonly uint CfPng = RegisterClipboardFormatW("PNG");

    /// <summary>读取剪贴板中的本地文件路径列表（如在资源管理器按 Ctrl+C 复制的文件）。</summary>
    public static bool TryGetFilePaths(out string[] paths)
    {
        paths = [];
        if (!IsClipboardFormatAvailable(CfHDrop))
        {
            return false;
        }

        if (!TryOpenClipboard())
        {
            return false;
        }

        try
        {
            var handle = GetClipboardData(CfHDrop);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            var count = DragQueryFileW(handle, 0xFFFFFFFF, null, 0);
            if (count == 0)
            {
                return false;
            }

            var list = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var len = DragQueryFileW(handle, i, null, 0);
                if (len == 0)
                {
                    continue;
                }

                var sb = new StringBuilder((int)len + 1);
                if (DragQueryFileW(handle, i, sb, (uint)sb.Capacity) > 0)
                {
                    list.Add(sb.ToString());
                }
            }

            paths = list.ToArray();
            return paths.Length > 0;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// 读取剪贴板中的图像字节（优先原生 PNG，其次将 CF_DIB / CF_DIBV5 封装为标准 BMP 字节供 Avalonia Bitmap 解码）。
    /// </summary>
    public static bool TryGetImageBytes(out byte[] bytes, out bool isPng)
    {
        bytes = [];
        isPng = false;

        var hasPng = CfPng != 0 && IsClipboardFormatAvailable(CfPng);
        var hasDib = IsClipboardFormatAvailable(CfDib);
        var hasDibV5 = !hasDib && IsClipboardFormatAvailable(CfDibV5);
        if (!hasPng && !hasDib && !hasDibV5)
        {
            return false;
        }

        if (!TryOpenClipboard())
        {
            return false;
        }

        try
        {
            if (hasPng && TryReadGlobalBytes(CfPng, out var pngBytes) && pngBytes.Length > 8)
            {
                bytes = pngBytes;
                isPng = true;
                return true;
            }

            var format = hasDib ? CfDib : CfDibV5;
            if (TryReadGlobalBytes(format, out var dibBytes) && dibBytes.Length >= 40)
            {
                bytes = WrapDibAsBmp(dibBytes);
                isPng = false;
                return bytes.Length > 54;
            }

            return false;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>将 Windows CF_DIB 内存块封装为带 14 字节 BITMAPFILEHEADER 的完整 BMP 文件。</summary>
    internal static byte[] WrapDibAsBmp(byte[] dibBytes)
    {
        if (dibBytes.Length < 40)
        {
            return [];
        }

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(dibBytes.AsSpan(0, 4));
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dibBytes.AsSpan(14, 2));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dibBytes.AsSpan(16, 4));
        var clrUsed = BinaryPrimitives.ReadUInt32LittleEndian(dibBytes.AsSpan(32, 4));

        if (headerSize < 40 || headerSize > dibBytes.Length)
        {
            return [];
        }

        uint colorTableBytes = 0;
        if (bitCount <= 8)
        {
            var entries = clrUsed != 0 ? clrUsed : (1u << bitCount);
            colorTableBytes = entries * 4u;
        }
        else if (compression == BiBitfields && headerSize == 40)
        {
            // BITMAPINFOHEADER + BI_BITFIELDS 包含 3 个 DWORD 通道掩码
            colorTableBytes = 12u;
        }

        const int fileHeaderSize = 14;
        var pixelOffset = (uint)(fileHeaderSize + headerSize + colorTableBytes);
        var totalSize = (uint)(fileHeaderSize + dibBytes.Length);

        var bmp = new byte[totalSize];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2, 4), totalSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10, 4), pixelOffset);
        Buffer.BlockCopy(dibBytes, 0, bmp, fileHeaderSize, dibBytes.Length);
        return bmp;
    }

    private static bool TryOpenClipboard()
    {
        // 剪贴板可能被其它进程短暂占用，轻量重试最多 3 次
        for (var i = 0; i < 3; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }

    private static bool TryReadGlobalBytes(uint format, out byte[] data)
    {
        data = [];
        var hGlobal = GetClipboardData(format);
        if (hGlobal == IntPtr.Zero)
        {
            return false;
        }

        var size = GlobalSize(hGlobal);
        if (size == UIntPtr.Zero || size.ToUInt64() > 50UL * 1024 * 1024)
        {
            return false;
        }

        var ptr = GlobalLock(hGlobal);
        if (ptr == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            data = new byte[(int)size.ToUInt64()];
            Marshal.Copy(ptr, data, 0, data.Length);
            return true;
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string lpszFormat);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);
}
