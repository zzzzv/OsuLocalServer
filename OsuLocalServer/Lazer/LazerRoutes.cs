using System.Text.Json;
using System.Diagnostics;
using osu.Game.Online.API;
using OsuLocalServer.Settings;
using Realms;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Scoring;

namespace OsuLocalServer.Lazer;

public sealed record WriteStarRatingsRequest(
    Dictionary<string, double> StarRatings
);

public static class LazerRoutes
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    
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
            logger.LogInformation("未检测到 Lazer — /api/lazer 端点不可用");
        }

        var group = app.MapGroup("/api/lazer");
        group.AddEndpointFilter(LazerAvailabilityFilter);

        group.MapGet("/scores", HandleScores);
        group.MapGet("/beatmaps", HandleBeatmaps);
        group.MapGet("/beatmapsets", HandleBeatmapSets);
        group.MapGet("/collections", HandleCollections);
        group.MapGet("/files/{hash}", HandleFile);
        group.MapPost("/collection/update", HandleCreateCollection);
        group.MapPost("/star-rating/calculate", (Delegate)HandleCalcSR);
        group.MapPost("/star-rating/update", HandleWriteStarRatings);
    }

    private static async ValueTask<object?> LazerAvailabilityFilter(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var svc = ctx.HttpContext.RequestServices.GetRequiredService<SettingService>();
        if (!svc.Settings.Lazer.IsAvailable)
            return Results.Problem("Lazer 不可用。", statusCode: 503);
        return await next(ctx);
    }

    private static IResult HandleScores(string rql, int? depth, SettingService svc, ILoggerFactory loggerFactory, string[]? noExpand = null) =>
        RunQuery(() => LazerRealm.Query(svc.Settings.Lazer.ClientRealmPath, rql, depth ?? 0, realm => realm.All<ScoreInfo>().Filter(rql), noExpand?.ToHashSet()), loggerFactory.CreateLogger("LazerRoutes"));

    private static IResult HandleBeatmaps(string rql, int? depth, SettingService svc, ILoggerFactory loggerFactory, string[]? noExpand = null) =>
        RunQuery(() => LazerRealm.Query(svc.Settings.Lazer.ClientRealmPath, rql, depth ?? 0, realm => realm.All<BeatmapInfo>().Filter(rql), noExpand?.ToHashSet()), loggerFactory.CreateLogger("LazerRoutes"));

    private static IResult HandleBeatmapSets(string rql, int? depth, SettingService svc, ILoggerFactory loggerFactory, string[]? noExpand = null) =>
        RunQuery(() => LazerRealm.Query(svc.Settings.Lazer.ClientRealmPath, rql, depth ?? 0, realm => realm.All<BeatmapSetInfo>().Filter(rql), noExpand?.ToHashSet()), loggerFactory.CreateLogger("LazerRoutes"));

    private static IResult HandleCollections(string rql, int? depth, SettingService svc, ILoggerFactory loggerFactory, string[]? noExpand = null) =>
        RunQuery(() => LazerRealm.Query(svc.Settings.Lazer.ClientRealmPath, rql, depth ?? 0, realm => realm.All<BeatmapCollection>().Filter(rql), noExpand?.ToHashSet()), loggerFactory.CreateLogger("LazerRoutes"));

    private static IResult HandleFile(string hash, SettingService svc)
    {
        hash = hash.Trim('\"');

        if (string.IsNullOrWhiteSpace(hash) || hash.Length < 3)
            return Results.BadRequest("无效的文件哈希。");

        var dataDir = Path.GetDirectoryName(svc.Settings.Lazer.ClientRealmPath) ?? LazerPaths.GetDefaultDataDirectory();
        var filePath = Path.Combine(dataDir, "files", hash[..1], hash[..2], hash);
        if (!File.Exists(filePath))
            return Results.NotFound($"文件未找到: {filePath}");

        return Results.File(filePath, Utils.GetContentType(filePath), Path.GetFileName(filePath));
    }

    private static IResult HandleCreateCollection(CreateCollectionRequest request, SettingService svc)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "需要提供收藏夹名称。" });

        var path = svc.Settings.Lazer.ClientRealmPath;
        if (!File.Exists(path))
            return Results.Problem("未找到 client.realm。", statusCode: 404);

        var result = LazerRealm.AddToCollection(path, request.Name, request.BeatmapMd5Hashes);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleCalcSR(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var beatmapContent = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(beatmapContent))
            return Results.BadRequest(new { error = "body 需要提供 .osu 文件内容。" });

        var modsQuery = context.Request.Query["mods"].FirstOrDefault();
        var mods = !string.IsNullOrWhiteSpace(modsQuery)
            ? JsonSerializer.Deserialize<APIMod[]>(modsQuery, JsonOptions) ?? []
            : [];

        try
        {
            var sr = LazerRulesets.CalcSRFromContent(beatmapContent, mods);
            return Results.Ok(new { starRating = sr });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: FormatExceptionDetail(ex),
                title: $"Star Rating 计算失败: {ex.GetType().Name}",
                statusCode: 400);
        }
    }

    private static IResult HandleWriteStarRatings(WriteStarRatingsRequest request, SettingService svc)
    {
        if (request.StarRatings.Count == 0)
            return Results.BadRequest(new { error = "需要提供至少一个 StarRating。" });

        var path = svc.Settings.Lazer.ClientRealmPath;
        if (!File.Exists(path))
            return Results.Problem("未找到 client.realm。", statusCode: 404);

        var updated = LazerRealm.WriteStarRatings(path, request.StarRatings);
        return Results.Ok(new { updated });
    }

    private static string FormatExceptionDetail(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(ex.ToString());

        if (ex.InnerException is not null)
        {
            sb.AppendLine("--- Inner Exception ---");
            sb.AppendLine(ex.InnerException.ToString());
        }

        return sb.ToString().TrimEnd();
    }

    private static IResult RunQuery(Func<List<object>> queryFunc, ILogger logger)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var items = queryFunc();
            sw.Stop();
            var queryTime = sw.ElapsedMilliseconds;

            sw.Restart();
            var response = new { count = items.Count, items };
            var _ = JsonSerializer.Serialize(response, JsonOptions);
            sw.Stop();
            var serializeTime = sw.ElapsedMilliseconds;

            logger.LogInformation("RunQuery: count={Count}, queryTime={QueryTime}ms, serializeTime={SerializeTime}ms",
                items.Count, queryTime, serializeTime);

            return Results.Ok(response);
        }
        catch (FileNotFoundException ex)
        {
            return Results.Problem(
                detail: FormatExceptionDetail(ex),
                title: "Lazer 数据库文件未找到",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: FormatExceptionDetail(ex),
                title: $"查询执行失败: {ex.GetType().Name}",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
