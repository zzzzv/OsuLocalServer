using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
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
