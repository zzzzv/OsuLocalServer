using Microsoft.AspNetCore.Mvc.RazorPages;
using OsuLocalServer.OsuApiV2;
using OsuLocalServer.Settings;

namespace OsuLocalServer.Pages;

public class IndexModel : PageModel
{
    private readonly SettingService _settings;
    private readonly OsuApiV2AuthService _authService;

    public AppSettings Settings => _settings.Settings;
    public string? Version { get; }
    public bool ApiV2Configured => _authService.IsConfigured;
    public bool ApiV2TokenValid => _authService.HasValidToken;
    public VersionCheckResult? VersionCheck { get; }
    public string LatestReleaseUrl { get; }

    public IndexModel(SettingService settings, OsuApiV2AuthService authService, VersionCheckService versionChecker)
    {
        _settings = settings;
        _authService = authService;
        Version = Utils.AppVersion;
        VersionCheck = versionChecker.Check();
        LatestReleaseUrl = versionChecker.GetLatestUrl();
    }
}
