using System.Text;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;

namespace OsuLocalServer.Lazer;

public static class LazerRulesets
{
    private static readonly Lazy<AssemblyRulesetStore> Store = new(() =>
        new AssemblyRulesetStore(LazerPaths.GetDefaultLazerCurrentDirectory()));

    public static double CalcSR(string beatmapPath, Mod[] mods)
    {
        var working = new FlatWorkingBeatmap(beatmapPath);
        var rulesetInfo = working.BeatmapInfo.Ruleset ?? throw new InvalidOperationException("谱面无 ruleset 信息");
        var ruleset = rulesetInfo.CreateInstance() ?? throw new InvalidOperationException($"无法创建 ruleset 实例: {rulesetInfo.Name}");
        var calculator = ruleset.CreateDifficultyCalculator(working);
        return calculator.Calculate(mods).StarRating;
    }

    public static double CalcSRFromContent(string beatmapContent, APIMod[] apiMods)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(beatmapContent));
        using var reader = new LineBufferedReader(stream);
        var beatmap = osu.Game.Beatmaps.Formats.Decoder.GetDecoder<Beatmap>(reader).Decode(reader);

        var working = new FlatWorkingBeatmap(beatmap);
        var rulesetInfo = working.BeatmapInfo.Ruleset ?? throw new InvalidOperationException("谱面无 ruleset 信息");
        var ruleset = rulesetInfo.CreateInstance() ?? throw new InvalidOperationException($"无法创建 ruleset 实例: {rulesetInfo.Name}");
        var mods = apiMods.Select(m => m.ToMod(ruleset)).ToArray();
        var calculator = ruleset.CreateDifficultyCalculator(working);
        return calculator.Calculate(mods).StarRating;
    }

    public static StarRating CalcManiaSR(string beatmapPath)
    {
        var info = Store.Value.GetRuleset("mania") ?? throw new InvalidOperationException("找不到 Mania ruleset");
        var mania = info.CreateInstance() ?? throw new InvalidOperationException("无法创建 Mania ruleset 实例");

        var working = new FlatWorkingBeatmap(beatmapPath);
        var calculator = mania.CreateDifficultyCalculator(working);

        return new StarRating(
            NM: calculator.Calculate([]).StarRating,
            HT: calculator.Calculate([new ManiaModHalfTime()]).StarRating,
            DT: calculator.Calculate([new ManiaModDoubleTime()]).StarRating
        );
    }
}
