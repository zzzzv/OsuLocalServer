using System.Text.Json;
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
        // 手动解析 JSON 而非依赖 ASP.NET 模型绑定，推测是因为：
        // APIMod 来自 osu.Game.dll（启动后由 OsuLazerAssemblyResolver 动态加载），
        // 若框架在启动时反射 handler 的参数类型（含 APIMod）来确定绑定方式，
        // 会因程序集尚未加载而引发错误。
        // 改用 HttpContext + 手动解析后，类型解析延迟到首次请求的 JIT 阶段，
        // 此时 osu.Game.dll 已加载完毕。
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var beatmapContent = root.GetProperty("beatmapContent").GetString();
        if (string.IsNullOrWhiteSpace(beatmapContent))
            return Results.BadRequest(new { error = "需要提供 beatmapContent(.osu 文件内容)。" });

        var mods = root.TryGetProperty("mods", out var modsEl)
            ? modsEl.EnumerateArray().Select(ReadApiMod).ToArray()
            : [];

        try
        {
            var sr = LazerRulesets.CalcSRFromContent(beatmapContent, mods);
            return Results.Ok(new { starRating = sr });
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 400);
        }
    }

    private static APIMod ReadApiMod(JsonElement el)
    {
        var mod = new APIMod { Acronym = el.GetProperty("acronym").GetString() ?? "" };
        if (el.TryGetProperty("settings", out var settings))
        {
            foreach (var prop in settings.EnumerateObject())
                mod.Settings[prop.Name] = JsonValueToObject(prop.Value);
        }
        return mod;
    }

    private static object JsonValueToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString()!,
        JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => el.GetRawText(),
    };

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
