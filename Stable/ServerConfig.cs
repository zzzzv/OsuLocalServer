public static class ServerConfig
{
    public const string DefaultUrls = "http://localhost:5167";

    public static string GetUrls(IConfiguration configuration)
    {
        var value = configuration["Urls"];
        return !string.IsNullOrEmpty(value) ? value : DefaultUrls;
    }

    public static string ResolveOsuRootPath(IConfiguration configuration)
    {
        var configuredPath = configuration["AppSettings:OsuRootPath"];
        if (!string.IsNullOrWhiteSpace(configuredPath) && Utils.IsValidOsuRoot(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var detected = Utils.TryFindOsuRootPath();
        if (detected is not null)
        {
            return detected;
        }

        throw new InvalidOperationException("Could not find osu! installation directory. Set AppSettings.OsuRootPath in appsettings.json.");
    }
}
