using Microsoft.AspNetCore.Mvc.RazorPages;
using OsuLocalServer.OsuApiV2;
using OsuLocalServer.Settings;

namespace OsuLocalServer.Pages;

public class IndexModel : PageModel
{
    private readonly SettingService _settings;
    private readonly OsuApiV2AuthService _authService;
    private readonly ManiaLabService _maniaLab;

    public AppSettings Settings => _settings.Settings;
    public string? Version { get; }
    public bool ApiV2Configured => _authService.IsConfigured;
    public bool ApiV2TokenValid => _authService.HasValidToken;
    public VersionCheckResult? VersionCheck { get; }
    public string LatestReleaseUrl { get; }

    public ManiaLabState ManiaLabState { get; }
    public string ManiaLabUrl { get; }

    public IndexModel(SettingService settings, OsuApiV2AuthService authService, VersionCheckService versionChecker, ManiaLabService maniaLab)
    {
        _settings = settings;
        _authService = authService;
        _maniaLab = maniaLab;
        Version = Utils.AppVersion;
        VersionCheck = versionChecker.Check();
        LatestReleaseUrl = versionChecker.GetLatestUrl();

        ManiaLabState = _maniaLab.GetState();
        ManiaLabUrl = _settings.Settings.Urls.TrimEnd('/') + "/mania-lab";

        // 自动检查并下载更新（后台 fire-and-forget）
        _ = AutoUpdateAsync();
    }

    private async Task AutoUpdateAsync()
    {
        try
        {
            var result = await _maniaLab.CheckForUpdateAsync();
            if (result.LatestVersion is not null && result.Error is null)
            {
                var installed = ManiaLabState.InstalledVersion;
                if (installed != result.LatestVersion)
                {
                    await _maniaLab.DownloadLatestAsync();
                }
            }
        }
        catch
        {
            // 静默失败，不影响首页加载
        }
    }
}
