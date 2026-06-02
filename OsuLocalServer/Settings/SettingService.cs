namespace OsuLocalServer.Settings;

public sealed class SettingService
{
    private readonly ILogger<SettingService> _logger;
    private AppSettings _settings = new();

    public SettingService(ILogger<SettingService> logger)
    {
        _logger = logger;
    }

    public AppSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            _logger.LogInformation("Settings updated");
            LogSettings();
        }
    }

    private void LogSettings()
    {
        var s = _settings;
        _logger.LogInformation("  Urls: {Urls}", s.Urls);
        _logger.LogInformation("  Lazer.ClientRealmPath: {Path}", s.Lazer.ClientRealmPath);
        _logger.LogInformation("  Stable.OsuRootPath: {Path}", s.Stable.OsuRootPath);
        var apiV2 = s.ApiV2;
        var secret = apiV2 is null || string.IsNullOrWhiteSpace(apiV2.ClientSecret) ? "(empty)" : "***";
        var clientId = apiV2 is null || string.IsNullOrWhiteSpace(apiV2.ClientId) ? "(empty)" : apiV2.ClientId;
        _logger.LogInformation("  ApiV2: clientId={ClientId}, clientSecret={Secret}", clientId, secret);
    }
}
