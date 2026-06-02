using OsuLocalServer.OsuApiV2;
using OsuLocalServer.Lazer;
using OsuLocalServer.Stable;

namespace OsuLocalServer.Settings;

public static class SettingsRoutes
{
    public static void MapSettingsRoutes(this WebApplication app, SettingService settings, OsuApiV2AuthService authService)
    {
        app.MapGet("/api/status", () => HandleStatus(settings, authService));
    }

    private static IResult HandleStatus(SettingService settings, OsuApiV2AuthService authService)
    {
        var s = settings.Settings;
        return Results.Ok(new
        {
            lazer = new
            {
                available = LazerPaths.IsAvailable(),
                clientRealmPath = s.Lazer.ClientRealmPath,
            },
            stable = new
            {
                available = OsuPathResolver.IsValidOsuRoot(s.Stable.OsuRootPath),
                osuRootPath = s.Stable.OsuRootPath,
            },
            apiv2 = new
            {
                configured = authService.IsConfigured,
                tokenValid = authService.HasValidToken,
                clientId = authService.GetClientId(),
            },
        });
    }
}
