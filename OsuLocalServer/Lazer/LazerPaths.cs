namespace OsuLocalServer.Lazer;

internal static class LazerPaths
{
    private static readonly string[] RequiredDlls = ["osu.Game.dll", "Realm.dll"];

    public static string GetDefaultDataDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "osu");

    public static string GetDefaultTempDirectory() =>
        Path.Combine(Path.GetTempPath(), "lazer");

    public static string GetDefaultLazerCurrentDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "osulazer",
            "current");

    /// <summary>
    /// 解析 lazer current 目录（始终使用默认路径），并验证 DLL 是否存在。
    /// </summary>
    public static string ResolveLazerCurrentDirectory()
    {
        var path = GetDefaultLazerCurrentDirectory();

        if (!IsAvailable())
        {
            var missing = RequiredDlls.FirstOrDefault(dll => !File.Exists(Path.Combine(path, dll)));

            throw missing is null
                ? new DirectoryNotFoundException($"Lazer directory not found: {path}")
                : new FileNotFoundException($"Missing {missing} in lazer directory", Path.Combine(path, missing));
        }

        return path;
    }

    public static bool IsAvailable()
    {
        var path = GetDefaultLazerCurrentDirectory();
        return Directory.Exists(path) && RequiredDlls.All(dll => File.Exists(Path.Combine(path, dll)));
    }
}
