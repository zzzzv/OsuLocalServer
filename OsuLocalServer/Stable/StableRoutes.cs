using OsuLocalServer.Settings;

namespace OsuLocalServer.Stable;

public sealed record WriteStableStarRatingsRequest(
    Dictionary<string, StarRating> StarRatings
);

public static class StableRoutes
{
    public static void MapStableRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/stable");
        group.AddEndpointFilter(StableAvailabilityFilter);

        group.MapGet("/files/{**relativePath}", HandleFile);
        group.MapPost("/collection/update", HandleCreateCollection);
        group.MapPost("/star-rating/update", HandleWriteManiaStarRatings);
    }

    private static async ValueTask<object?> StableAvailabilityFilter(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var svc = ctx.HttpContext.RequestServices.GetRequiredService<SettingService>();
        if (!svc.Settings.Stable.IsAvailable)
            return Results.Problem("osu!stable 目录不可用", statusCode: 503);
        return await next(ctx);
    }

    private static IResult HandleFile(string? relativePath, SettingService svc)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return Results.BadRequest("需要提供相对文件路径");

        var root = svc.Settings.Stable.OsuRootPath;

        string? filePath;

        if (relativePath.Contains('*'))
        {
            filePath = StablePathResolver.ResolveFilePathWithWildcard(root, relativePath);
        }
        else
        {
            filePath = StablePathResolver.ResolveFilePath(root, relativePath);
            if (filePath is null)
                return Results.BadRequest("无效的文件路径");
        }

        if (filePath is null || !File.Exists(filePath))
            return Results.NotFound();

        return Results.File(filePath, Utils.GetContentType(filePath), Path.GetFileName(filePath));
    }

    private static IResult HandleCreateCollection(CreateCollectionRequest request, SettingService svc)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "需要提供收藏夹名称" });

        var dbPath = Path.Combine(svc.Settings.Stable.OsuRootPath, "collection.db");

        if (svc.Settings.BackupBeforeWrite)
            Utils.BackupFile(dbPath);

        var result = StableDatabase.AddToCollection(svc.Settings.Stable.OsuRootPath, request.Name, request.BeatmapMd5Hashes);
        return Results.Ok(result);
    }

    private static IResult HandleWriteManiaStarRatings(WriteStableStarRatingsRequest request, SettingService svc)
    {
        if (request.StarRatings.Count == 0)
            return Results.BadRequest(new { error = "需要提供至少一个 StarRating。" });

        var osuDbPath = Path.Combine(svc.Settings.Stable.OsuRootPath, "osu!.db");
        if (!File.Exists(osuDbPath))
            return Results.Problem("未找到 osu!.db。", statusCode: 404);

        try
        {
            var updated = StableDatabase.WriteManiaStarRatings(osuDbPath, request.StarRatings);
            return Results.Ok(new { updated });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: 409);
        }
    }
}
