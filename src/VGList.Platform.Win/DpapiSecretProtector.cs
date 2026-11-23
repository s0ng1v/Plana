using System.Runtime.InteropServices;
using System.Text;

namespace VGList.Platform.Win;

/// <summary>
/// 基于 Windows DPAPI（CryptProtectData / CryptUnprotectData，CurrentUser 作用域）的本机凭据加密器。
/// 零额外 NuGet 依赖，密文绑定当前 Windows 用户，不允许界面弹窗。
/// </summary>
public sealed class DpapiSecretProtector
{
    private const int CryptProtectUiForbidden = 0x1;

    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        var inputBytes = Encoding.UTF8.GetBytes(plainText);
        var inBlob = new DataBlob();
        var outBlob = new DataBlob();

        try
        {
            inBlob.cbData = inputBytes.Length;
            inBlob.pbData = Marshal.AllocHGlobal(inputBytes.Length);
            Marshal.Copy(inputBytes, 0, inBlob.pbData, inputBytes.Length);

            if (!CryptProtectData(ref inBlob, null, nint.Zero, nint.Zero, nint.Zero, CryptProtectUiForbidden, out outBlob))
            {
                throw new InvalidOperationException($"DPAPI 加密失败 (Win32={Marshal.GetLastWin32Error()})。");
            }

            var cipher = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, cipher, 0, outBlob.cbData);
            return Convert.ToBase64String(cipher);
        }
        finally
        {
            if (inBlob.pbData != nint.Zero)
            {
                Marshal.FreeHGlobal(inBlob.pbData);
            }

            if (outBlob.pbData != nint.Zero)
            {
                LocalFree(outBlob.pbData);
            }
        }
    }

    public static bool TryUnprotect(string cipherBase64, out string plainText)
    {
        plainText = string.Empty;
        if (string.IsNullOrWhiteSpace(cipherBase64))
        {
            return true;
        }

        byte[] cipherBytes;
        try
        {
            cipherBytes = Convert.FromBase64String(cipherBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        var inBlob = new DataBlob();
        var outBlob = new DataBlob();

        try
        {
            inBlob.cbData = cipherBytes.Length;
            inBlob.pbData = Marshal.AllocHGlobal(cipherBytes.Length);
            Marshal.Copy(cipherBytes, 0, inBlob.pbData, cipherBytes.Length);

            if (!CryptUnprotectData(ref inBlob, nint.Zero, nint.Zero, nint.Zero, nint.Zero, CryptProtectUiForbidden, out outBlob))
            {
                return false;
            }

            var plainBytes = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, plainBytes, 0, outBlob.cbData);
            plainText = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        finally
        {
            if (inBlob.pbData != nint.Zero)
            {
                Marshal.FreeHGlobal(inBlob.pbData);
            }

            if (outBlob.pbData != nint.Zero)
            {
                LocalFree(outBlob.pbData);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public nint pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        nint pOptionalEntropy,
        nint pvReserved,
        nint pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        nint ppszDataDescr,
        nint pOptionalEntropy,
        nint pvReserved,
        nint pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint LocalFree(nint hMem);
}
