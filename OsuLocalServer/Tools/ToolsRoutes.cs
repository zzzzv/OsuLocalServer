using StarRatingRebirth;

namespace OsuLocalServer.Tools;

public sealed record XxyCalcRequest(
    string BeatmapContent,
    double SpeedRate
);

public static class ToolsRoutes
{
    public static void MapToolsRoutes(this WebApplication app)
    {
        var group = app.MapGroup("/api/tools");

        group.MapPost("/xxy-calculate", HandleXxyCalculate);
    }

    private static IResult HandleXxyCalculate(XxyCalcRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BeatmapContent))
            return Results.BadRequest(new { error = "需要提供 beatmapContent。" });
        if (request.SpeedRate <= 0)
            return Results.BadRequest(new { error = "speedRate 必须大于 0。" });

        try
        {
            var lines = request.BeatmapContent.Split('\n', StringSplitOptions.None);
            var data = ManiaData.FromLines(lines);

            var rateData = request.SpeedRate != 1.0
                ? data.ChangeRate(1.0 / request.SpeedRate)  // 加速则压缩时间，减速则拉伸时间
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
