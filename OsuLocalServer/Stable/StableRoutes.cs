using OsuLocalServer.Settings;

namespace OsuLocalServer.Stable;

public static class StableRoutes
{
    public static void MapStableRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/stable");

        group.MapGet("/files/{**relativePath}", HandleFile);
    }

    private static IResult HandleFile(string? relativePath, SettingService svc)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return Results.BadRequest("Relative file path is required.");

        var root = svc.Settings.Stable.OsuRootPath;
        if (!OsuPathResolver.IsValidOsuRoot(root))
        {
            root = OsuPathResolver.TryFindOsuRootPath();
            if (root is null)
                return Results.Problem("osu!stable directory not available.", statusCode: 503);
        }

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


}
