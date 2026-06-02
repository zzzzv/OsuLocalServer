using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OsuLocalServer.OsuApiV2;
using OsuLocalServer.Settings;

namespace OsuLocalServer.Pages;

public class SettingsModel : PageModel
{
    private readonly SettingService _settings;
    private readonly OsuApiV2AuthService _authService;

    public SettingsModel(SettingService settings, OsuApiV2AuthService authService)
    {
        _settings = settings;
        _authService = authService;
    }

    public string? Message { get; set; }
    public string MessageType { get; set; } = "";

    // Display-only status
    public bool IsConfigured => _authService.IsConfigured;
    public bool TokenValid => _authService.HasValidToken;
    public string Urls => _settings.Settings.Urls;
    public bool OpenSettingsOnStartup => _settings.Settings.OpenSettingsOnStartup;
    public bool BackupBeforeWrite => _settings.Settings.BackupBeforeWrite;
    public string ClientRealmPath => _settings.Settings.Lazer.ClientRealmPath;
    public string OsuRootPath => _settings.Settings.Stable.OsuRootPath;
    public string? ClientId => _authService.GetClientId();

    public void OnGet() { }

    public IActionResult OnPost(
        string urls,
        bool openSettingsOnStartup,
        bool backupBeforeWrite,
        string clientRealmPath,
        string osuRootPath,
        string clientId,
        string clientSecret)
    {
        var s = _settings.Settings;

        if (!string.IsNullOrWhiteSpace(urls))
            s.Urls = urls;

        s.OpenSettingsOnStartup = openSettingsOnStartup;
        s.BackupBeforeWrite = backupBeforeWrite;

        if (!string.IsNullOrWhiteSpace(clientRealmPath))
            s.Lazer.ClientRealmPath = clientRealmPath;

        if (!string.IsNullOrWhiteSpace(osuRootPath))
            s.Stable.OsuRootPath = osuRootPath;

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
}
