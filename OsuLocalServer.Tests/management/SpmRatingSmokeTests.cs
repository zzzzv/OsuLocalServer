using Xunit;

namespace OsuLocalServer.Tests.Management;

public class SpmRatingSmokeTests
{
    [Fact]
    public void Fixture_IsRatedAtThreeRates()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "pupa.osu");
        var data = SPMRating.ManiaData.FromFile(path);

        Assert.Equal(7, data.CS);

        foreach (double rate in new[] { 1.0, 0.75, 1.5 })
        {
            bool ok = SPMRating.SPMCalculator.TryCalculateDetailed(
                data.WithSpeedRate(rate), out var result, out var error);

            Assert.True(ok, error);
            Assert.Equal(7, result.KeyCount);
            Assert.True(double.IsFinite(result.ReferenceScore), $"rate {rate} 的 ReferenceScore 非有限值");
            Assert.True(result.ReferenceScore > 0, $"rate {rate} 的 ReferenceScore 应大于 0");
        }
    }
}
