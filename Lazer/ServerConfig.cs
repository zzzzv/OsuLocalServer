public static class ServerConfig
{
    public const string DefaultUrls = "http://localhost:5048";

    public static string GetUrls(IConfiguration configuration)
    {
        var value = configuration["Urls"];
        return !string.IsNullOrEmpty(value) ? value : DefaultUrls;
    }

    public static string GetLazerCurrentDirectory(IConfiguration configuration)
    {
        var value = configuration["LazerPaths:LazerCurrentDirectory"];
        var path = !string.IsNullOrEmpty(value)
            ? value
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "osulazer",
                "current");

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Lazer directory not found: {path}");

        var missing = new[] { "osu.Game.dll", "Realm.dll" }
            .FirstOrDefault(dll => !File.Exists(Path.Combine(path, dll)));
        if (missing is not null)
            throw new FileNotFoundException($"Missing {missing} in lazer directory", Path.Combine(path, missing));

        return path;
    }

    public static string GetDataDirectory(IConfiguration configuration)
    {
        var value = configuration["LazerPaths:DataDirectory"];
        return !string.IsNullOrEmpty(value)
            ? value
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "osu");
    }

    public static string GetTempDirectory(IConfiguration configuration)
    {
        var value = configuration["LazerPaths:TempDirectory"];
        return !string.IsNullOrEmpty(value)
            ? value
            : Path.Combine(Path.GetTempPath(), "lazer");
    }
}
