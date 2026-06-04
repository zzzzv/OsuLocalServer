using MessagePack;
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
            return Results.Ok(new { count = 0, path });

        try
        {
            var bytes = File.ReadAllBytes(path);
            var data = MessagePackSerializer.Deserialize<Dictionary<string, ManiaSRData>>(bytes);
            return Results.Ok(new { count = data?.Count ?? 0, path });
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message);
        }
    }
}
