using OsuLocalServer.OsuApiV2;

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
            version = Utils.AppVersion,
            lazer = s.Lazer,
            stable = s.Stable,
            apiv2 = new
            {
                configured = authService.IsConfigured,
                tokenValid = authService.HasValidToken,
                clientId = authService.GetClientId(),
            },
        });
    }
}
