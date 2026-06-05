using OsuLocalServer.Lazer;
using osu.Game.Online.API;
using osu.Game.Rulesets.Mania.Mods;
using Xunit;

namespace OsuLocalServer.Tests.lazer;

public sealed class LazerRulesetsTests
{
    private static readonly string SampleBeatmapPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "pupa.osu");
    private static readonly string LazerCurrentDirectory = LazerPaths.GetDefaultLazerCurrentDirectory();
    private static readonly string SampleBeatmapContent = File.ReadAllText(SampleBeatmapPath);
    private static readonly StarRating ExpectedManiaSR = new(
        NM: 5.390204871167372,
        HT: 4.395036522598568,
        DT: 7.186750686166602);

    static LazerRulesetsTests()
    {
        OsuLazerAssemblyResolver.Register(LazerCurrentDirectory);
    }

    [Fact]
    public void CalcSR()
    {
        var nm = LazerRulesets.CalcSR(SampleBeatmapPath, []);
        var ht = LazerRulesets.CalcSR(SampleBeatmapPath, [new ManiaModHalfTime()]);
        var dt = LazerRulesets.CalcSR(SampleBeatmapPath, [new ManiaModDoubleTime()]);

        Assert.Equal(ExpectedManiaSR.NM, nm, 12);
        Assert.Equal(ExpectedManiaSR.HT, ht, 12);
        Assert.Equal(ExpectedManiaSR.DT, dt, 12);
    }

    [Fact]
    public void CalcSRFromContent()
    {
        var nm = LazerRulesets.CalcSRFromContent(SampleBeatmapContent, []);
        var ht = LazerRulesets.CalcSRFromContent(SampleBeatmapContent, [new APIMod { Acronym = "HT", Settings = new Dictionary<string, object> { ["speed_change"] = 0.75 } }]);
        var dt = LazerRulesets.CalcSRFromContent(SampleBeatmapContent, [new APIMod { Acronym = "DT", Settings = new Dictionary<string, object> { ["speed_change"] = 1.5 } }]);

        Assert.Equal(ExpectedManiaSR.NM, nm, 12);
        Assert.Equal(ExpectedManiaSR.HT, ht, 12);
        Assert.Equal(ExpectedManiaSR.DT, dt, 12);
    }

    [Fact]
    public void CalcManiaSR()
    {
        var result = LazerRulesets.CalcManiaSR(SampleBeatmapPath);

        Assert.Equal(ExpectedManiaSR.NM, result.NM, 12);
        Assert.Equal(ExpectedManiaSR.HT, result.HT, 12);
        Assert.Equal(ExpectedManiaSR.DT, result.DT, 12);
    }
}