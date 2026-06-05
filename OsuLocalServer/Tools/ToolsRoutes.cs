using StarRatingRebirth;

namespace OsuLocalServer.Tools;

public static class ToolsRoutes
{
    public static void MapToolsRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/tools");

        group.MapPost("/xxy-calculate", (Delegate)HandleXxyCalculate);
    }

    private static async Task<IResult> HandleXxyCalculate(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var beatmapContent = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(beatmapContent))
            return Results.BadRequest(new { error = "body 需要提供 .osu 文件内容。" });

        var speedRateStr = context.Request.Query["speedRate"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(speedRateStr) || !double.TryParse(speedRateStr, out var speedRate) || speedRate <= 0)
            return Results.BadRequest(new { error = "speedRate 必须大于 0。" });

        try
        {
            var lines = beatmapContent.Split('\n', StringSplitOptions.None);
            var data = ManiaData.FromLines(lines);

            var rateData = speedRate != 1.0
                ? data.ChangeRate(1.0 / speedRate)  // 加速则压缩时间，减速则拉伸时间
                : data;

            var sr = SRCalculator.Calculate(rateData);
            return Results.Ok(new { starRating = sr });
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: 400);
        }
    }
}
