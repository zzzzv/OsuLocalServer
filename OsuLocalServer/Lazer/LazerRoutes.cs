using OsuLocalServer.Settings;
using Realms;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Scoring;

namespace OsuLocalServer.Lazer;

public static class LazerRoutes
{
    public static void MapLazerRoutes(this WebApplication app)
    {
        try
        {
            var lazerDirectory = LazerPaths.ResolveLazerCurrentDirectory();
            OsuLazerAssemblyResolver.Register(lazerDirectory);
        }
        catch
        {
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Lazer");
            logger.LogInformation("Lazer not detected — /api/lazer endpoints are unavailable");
        }

        var group = app.MapGroup("/api/lazer");
        group.AddEndpointFilter(LazerAvailabilityFilter);

        group.MapGet("/scores", HandleScores);
        group.MapGet("/beatmaps", HandleBeatmaps);
        group.MapGet("/beatmapsets", HandleBeatmapSets);
        group.MapGet("/collections", HandleCollections);
        group.MapGet("/files/{hash}", HandleFile);
        group.MapPost("/collections", HandleCreateCollection);
    }

    private static async ValueTask<object?> LazerAvailabilityFilter(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var svc = ctx.HttpContext.RequestServices.GetRequiredService<SettingService>();
        if (!svc.Settings.Lazer.IsAvailable)
            return Results.Problem("Lazer not available.", statusCode: 503);
        return await next(ctx);
    }

    private static IResult HandleScores(string rql, int? depth, SettingService svc) =>
        RunQuery(() => LazerRealm.Query(svc.Settings.Lazer.ClientRealmPath, rql, depth ?? 0, realm => realm.All<ScoreInfo>().Filter(rql)));

    private static IResult HandleBeatmaps(string rql, int? depth, SettingService svc) =>
        RunQuery(() => LazerRealm.Query(svc.Settings.Lazer.ClientRealmPath, rql, depth ?? 0, realm => realm.All<BeatmapInfo>().Filter(rql)));

    private static IResult HandleBeatmapSets(string rql, int? depth, SettingService svc) =>
        RunQuery(() => LazerRealm.Query(svc.Settings.Lazer.ClientRealmPath, rql, depth ?? 0, realm => realm.All<BeatmapSetInfo>().Filter(rql)));

    private static IResult HandleCollections(string rql, int? depth, SettingService svc) =>
        RunQuery(() => LazerRealm.Query(svc.Settings.Lazer.ClientRealmPath, rql, depth ?? 0, realm => realm.All<BeatmapCollection>().Filter(rql)));

    private static IResult HandleFile(string hash, SettingService svc)
    {
        hash = hash.Trim('\"');

        if (string.IsNullOrWhiteSpace(hash) || hash.Length < 3)
            return Results.BadRequest("Invalid file hash.");

        var dataDir = Path.GetDirectoryName(svc.Settings.Lazer.ClientRealmPath) ?? LazerPaths.GetDefaultDataDirectory();
        var filePath = Path.Combine(dataDir, "files", hash[..1], hash[..2], hash);
        if (!File.Exists(filePath))
            return Results.NotFound($"File not found: {filePath}");

        return Results.File(filePath, contentType: "application/octet-stream");
    }

    private static IResult HandleCreateCollection(CreateCollectionRequest request, SettingService svc)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Collection name is required." });

        var path = svc.Settings.Lazer.ClientRealmPath;
        if (!File.Exists(path))
            return Results.Problem("client.realm not found.", statusCode: 404);

        var result = LazerRealm.AddToCollection(path, request.Name, request.BeatmapMd5Hashes);
        return Results.Ok(result);
    }

    private static IResult RunQuery(Func<List<object>> queryFunc)
    {
        try
        {
            var items = queryFunc();
            return Results.Ok(new { count = items.Count, items });
        }
        catch (FileNotFoundException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
