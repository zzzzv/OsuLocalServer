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
    /// Resolves the lazer current directory (always via default), validates DLLs exist.
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
