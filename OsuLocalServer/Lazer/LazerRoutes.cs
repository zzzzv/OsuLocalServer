using OsuLocalServer.Settings;

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

        group.MapGet("/scores", HandleScores);
        group.MapGet("/beatmaps", HandleBeatmaps);
        group.MapGet("/beatmapsets", HandleBeatmapSets);
        group.MapGet("/collections", HandleCollections);
        group.MapGet("/files/{hash}", HandleFile);
    }

    private static IResult HandleScores(string rql, int? depth, LazerRealmQueryService queryService) =>
        RunQuery(() => queryService.QueryScores(rql, depth ?? 0));

    private static IResult HandleBeatmaps(string rql, int? depth, LazerRealmQueryService queryService) =>
        RunQuery(() => queryService.QueryBeatmaps(rql, depth ?? 0));

    private static IResult HandleBeatmapSets(string rql, int? depth, LazerRealmQueryService queryService) =>
        RunQuery(() => queryService.QueryBeatmapSets(rql, depth ?? 0));

    private static IResult HandleCollections(string rql, int? depth, LazerRealmQueryService queryService) =>
        RunQuery(() => queryService.QueryCollections(rql, depth ?? 0));

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
