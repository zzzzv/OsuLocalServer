using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.StaticFiles;

namespace OsuLocalServer;

public static class Utils
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new()
    {
        Mappings =
        {
            [".osu"] = "text/plain",
        },
    };

    public static string GetContentType(string filePath)
    {
        return ContentTypeProvider.TryGetContentType(filePath, out var contentType)
            ? contentType
            : "application/octet-stream";
    }

    public static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    private const byte VK_ALT = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x02;

    public static void OpenFolder(string path)
    {
        try
        {
            // 释放前景锁定，让新窗口可以置前
            keybd_event(VK_ALT, 0, 0, 0);
            keybd_event(VK_ALT, 0, KEYEVENTF_KEYUP, 0);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }

    public static void BackupFile(string path)
    {
        for (int i = 1; ; i++)
        {
            var backupPath = $"{path}.bak{i}";
            if (!File.Exists(backupPath))
            {
                File.Copy(path, backupPath);
                return;
            }
        }
    }

    /// <summary>
    /// 检查指定根目录下是否有 osu!.exe 进程在运行。
    /// </summary>
    /// <param name="rootPath">osu! 安装目录（如 stable 根目录或 lazer current 目录）</param>
    public static bool IsOsuProcessRunning(string rootPath)
    {
        var targetDir = Path.GetFullPath(rootPath).TrimEnd('\\') + '\\';
        foreach (var p in Process.GetProcessesByName("osu!"))
        {
            try
            {
                if (p.MainModule?.FileName is string fn &&
                    Path.GetFullPath(fn).StartsWith(targetDir, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
            finally { p.Dispose(); }
        }
        return false;
    }
}
