using Microsoft.Win32;

namespace OsuLocalServer.Stable;

public static class StablePathResolver
{
    private static readonly string[] RegistryPaths =
    [
        @"HKEY_CLASSES_ROOT\osustable.File.osz\Shell\Open\Command",
        @"HKEY_CURRENT_USER\Software\Classes\osustable.File.osz\Shell\Open\Command"
    ];

    public static string? TryFindOsuRootPath()
    {
        foreach (var registryPath in RegistryPaths)
        {
            if (Registry.GetValue(registryPath, null, null) is not string command || string.IsNullOrWhiteSpace(command))
                continue;

            var executablePath = ExtractExecutablePath(command);
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                continue;

            var directoryPath = Path.GetDirectoryName(executablePath);
            if (IsValidOsuRoot(directoryPath))
                return Path.GetFullPath(directoryPath!);
        }

        return null;
    }

    public static bool IsValidOsuRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            return File.Exists(Path.Combine(fullPath, "osu!.exe"));
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractExecutablePath(string command)
    {
        var trimmed = command.Trim();

        if (trimmed.StartsWith('"'))
        {
            var endQuoteIndex = trimmed.IndexOf('"', 1);
            return endQuoteIndex > 1 ? trimmed[1..endQuoteIndex] : null;
        }

        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIndex >= 0 ? trimmed[..(exeIndex + 4)] : null;
    }

    public static string? ResolveFilePath(string osuRootPath, string relativePath)
    {
        var normalizedRelativePath = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(normalizedRelativePath))
            return null;

        var fullRootPath = Path.GetFullPath(osuRootPath)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRootPath, normalizedRelativePath));

        return fullPath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    /// <summary>
    /// 使用 <c>*</c> 通配符遍历文件系统树。
    /// <c>*</c> 匹配任意字符（路径分隔符除外）。
    /// 返回第一个匹配项，或 null。
    /// </summary>
    public static string? ResolveFilePathWithWildcard(string osuRootPath, string relativePath)
    {
        var normalized = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return WalkPathWithWildcard(osuRootPath, segments, 0);
    }

    private static string? WalkPathWithWildcard(string currentDir, string[] segments, int index)
    {
        if (index >= segments.Length || !Directory.Exists(currentDir))
            return null;

        var segment = segments[index];
        var isLast = index == segments.Length - 1;

        if (segment.Contains('*'))
        {
            try
            {
                if (isLast)
                {
                    foreach (var file in Directory.EnumerateFiles(currentDir, segment))
                        return Path.GetFullPath(file);
                }
                else
                {
                    foreach (var dir in Directory.EnumerateDirectories(currentDir, segment))
                    {
                        var result = WalkPathWithWildcard(dir, segments, index + 1);
                        if (result != null)
                            return result;
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
        }
        else
        {
            var nextPath = Path.Combine(currentDir, segment);
            if (isLast)
            {
                if (File.Exists(nextPath))
                    return Path.GetFullPath(nextPath);
            }
            else if (Directory.Exists(nextPath))
            {
                return WalkPathWithWildcard(nextPath, segments, index + 1);
            }
        }

        return null;
    }
}
