using OsuLocalServer.Settings;

namespace OsuLocalServer.Management;

public static class ManagementRoutes
{
    public static void MapManagementRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/management");

        group.MapGet("/mania-sr/msgpack", HandleManiaSRPack);
    }

    private static IResult HandleManiaSRPack(SettingService svc)
    {
        var path = svc.Settings.Management.ManiaSRPackPath;
        if (!File.Exists(path))
            return Results.NotFound();

        return Results.File(path, "application/octet-stream", "mania_sr.msgpack");
    }
}
