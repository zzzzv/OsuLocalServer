using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OsuLocalServer.OsuApiV2;
using OsuLocalServer.Settings;

namespace OsuLocalServer.Pages;

public class SettingsModel : PageModel
{
    private readonly SettingService _settings;
    private readonly OsuApiV2AuthService _authService;
    private readonly VersionCheckService _versionChecker;

    public SettingsModel(SettingService settings, OsuApiV2AuthService authService, VersionCheckService versionChecker)
    {
        _settings = settings;
        _authService = authService;
        _versionChecker = versionChecker;
    }

    public string? Message { get; set; }
    public string MessageType { get; set; } = "";

    // Display-only status
    public bool IsConfigured => _authService.IsConfigured;
    public bool TokenValid => _authService.HasValidToken;
    public string Urls => _settings.Settings.Urls;
    public bool OpenBrowserOnStartup => _settings.Settings.OpenBrowserOnStartup;
    public bool BackupBeforeWrite => _settings.Settings.BackupBeforeWrite;
    public string ClientRealmPath => _settings.Settings.Lazer.ClientRealmPath;
    public string OsuRootPath => _settings.Settings.Stable.OsuRootPath;
    public string? ClientId => _authService.GetClientId();
    public UpdateSource UpdateSource => _settings.Settings.UpdateSource;

    public void OnGet() { }

    public IActionResult OnPost(
        string urls,
        bool openBrowserOnStartup,
        bool backupBeforeWrite,
        string clientRealmPath,
        string osuRootPath,
        string clientId,
        string clientSecret,
        string updateSource)
    {
        var s = _settings.Settings;

        if (!string.IsNullOrWhiteSpace(urls))
            s.Urls = urls;

        s.OpenBrowserOnStartup = openBrowserOnStartup;
        s.BackupBeforeWrite = backupBeforeWrite;

        if (!string.IsNullOrWhiteSpace(clientRealmPath))
            s.Lazer.ClientRealmPath = clientRealmPath;

        if (!string.IsNullOrWhiteSpace(osuRootPath))
            s.Stable.OsuRootPath = osuRootPath;

        if (Enum.TryParse<UpdateSource>(updateSource, ignoreCase: true, out var parsed) && parsed != s.UpdateSource)
        {
            s.UpdateSource = parsed;
            _versionChecker.ClearCache();
        }

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            s.ApiV2 = new ApiV2Credentials
            {
                ClientId = clientId ?? "",
                ClientSecret = clientSecret,
            };
            _authService.ClearToken();
        }

        _settings.Settings.Save();
        Message = "Settings saved";
        MessageType = "success";
        return Page();
    }

    public IActionResult OnPostOpenFolder()
    {
        Utils.OpenFolder(AppSettings.StorageDir);
        return RedirectToPage();
    }
}
