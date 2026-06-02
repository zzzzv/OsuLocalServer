using OsuLocalServer.Settings;

namespace OsuLocalServer.Stable;

public static class StableRoutes
{
    public static void MapStableRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/stable");
        group.AddEndpointFilter(StableAvailabilityFilter);

        group.MapGet("/files/{**relativePath}", HandleFile);
        group.MapPost("/collections", HandleCreateCollection);
    }

    private static async ValueTask<object?> StableAvailabilityFilter(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var svc = ctx.HttpContext.RequestServices.GetRequiredService<SettingService>();
        if (!svc.Settings.Stable.IsAvailable)
            return Results.Problem("osu!stable directory not available.", statusCode: 503);
        return await next(ctx);
    }

    private static IResult HandleFile(string? relativePath, SettingService svc)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return Results.BadRequest("Relative file path is required.");

        var root = svc.Settings.Stable.OsuRootPath;

        string? filePath;

        if (relativePath.Contains('*'))
        {
            filePath = OsuPathResolver.ResolveFilePathWithWildcard(root, relativePath);
        }
        else
        {
            filePath = OsuPathResolver.ResolveFilePath(root, relativePath);
            if (filePath is null)
                return Results.BadRequest("Invalid file path.");
        }

        if (filePath is null || !File.Exists(filePath))
            return Results.NotFound();

        return Results.File(filePath, Utils.GetContentType(filePath), Path.GetFileName(filePath));
    }

    private static IResult HandleCreateCollection(CreateCollectionRequest request, SettingService svc)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Collection name is required." });

        var dbPath = Path.Combine(svc.Settings.Stable.OsuRootPath, "collection.db");
        if (!File.Exists(dbPath))
            return Results.Problem("collection.db not found.", statusCode: 404);

        if (svc.Settings.BackupBeforeWrite)
            Utils.BackupFile(dbPath);

        var result = StableDatabase.AddToCollection(dbPath, request.Name, request.BeatmapMd5Hashes);
        return Results.Ok(result);
    }
}
