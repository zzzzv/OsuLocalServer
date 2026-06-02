using System.Diagnostics;
using Microsoft.AspNetCore.StaticFiles;

namespace OsuLocalServer;

public static class Utils
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

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
}
