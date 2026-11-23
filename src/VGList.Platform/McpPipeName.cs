using System.Security.Cryptography;
using System.Text;

namespace VGList.Platform;

/// <summary>主进程与 VGList.Mcp 共用的当前用户管道名，跨平台保持一致。</summary>
public static class McpPipeName
{
    public static string ForCurrentUser()
    {
        var user = Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)))[..16]
            .ToLowerInvariant();
        return $"plana-mcp-{hash}";
    }
}
