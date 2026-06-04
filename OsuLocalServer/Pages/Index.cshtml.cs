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

    public IndexModel(SettingService settings, OsuApiV2AuthService authService)
    {
        _settings = settings;
        _authService = authService;
        Version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
    }
}
