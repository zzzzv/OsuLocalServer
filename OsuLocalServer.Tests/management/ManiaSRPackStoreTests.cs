using MessagePack;
using OsuLocalServer;
using OsuLocalServer.Management;
using Xunit;

namespace OsuLocalServer.Tests.Management;

public class ManiaSRPackStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"mania_sr_{Guid.NewGuid():N}.msgpack");

    [Fact]
    public void CompactFormat_RoundTrips_StatesAndValues()
    {
        var path = TempPath();
        try
        {
            var data = new Dictionary<string, ManiaSRData>
            {
                ["aaa"] = ManiaSRData.Empty
                    .With(ManiaSRAlgorithm.PPY, SRState.Ok, new StarRating(3.5, 4.5, 6.5))
                    .With(ManiaSRAlgorithm.XXY, SRState.Ok, new StarRating(4.1, 5.2, 7.3))
                    .With(ManiaSRAlgorithm.SPM, SRState.Unsupported),
                ["bbb"] = ManiaSRData.Empty
                    .With(ManiaSRAlgorithm.PPY, SRState.Ok, new StarRating(1, 2, 3))
                    .With(ManiaSRAlgorithm.XXY, SRState.Failed),
                ["ccc"] = ManiaSRData.Empty,
            };

            ManiaSRPackStore.Save(data, path);
            var loaded = ManiaSRPackStore.Load(path);

            Assert.False(loaded.MigratedFromV1);
            Assert.Equal(3, loaded.Data.Count);

            Assert.Equal(SRState.Ok, loaded.Data["aaa"].StateOf(ManiaSRAlgorithm.PPY));
            Assert.Equal(4.5, loaded.Data["aaa"].PPY!.HT);
            Assert.Equal(7.3, loaded.Data["aaa"].XXY!.DT);
            Assert.Equal(SRState.Unsupported, loaded.Data["aaa"].StateOf(ManiaSRAlgorithm.SPM));
            Assert.Null(loaded.Data["aaa"].SPM);

            Assert.Equal(SRState.Failed, loaded.Data["bbb"].StateOf(ManiaSRAlgorithm.XXY));
            Assert.Null(loaded.Data["bbb"].XXY);

            Assert.Equal(SRState.Pending, loaded.Data["ccc"].StateOf(ManiaSRAlgorithm.PPY));
            Assert.Equal(SRState.Pending, loaded.Data["ccc"].StateOf(ManiaSRAlgorithm.SPM));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyFormat_IsMigrated()
    {
        var path = TempPath();
        try
        {
            var legacy = new Dictionary<string, ManiaSRDataV1?>
            {
                ["ok"] = new(new StarRatingV1(3.5, 4.5, 6.5), new StarRatingV1(4.1, 5.2, 7.3)),
                // v1 里 XXY 全 0 表示计算失败
                ["zero"] = new(new StarRatingV1(2, 3, 4), new StarRatingV1(0, 0, 0)),
                // v1 里 XXY 为 null 表示根本没算出来
                ["null"] = new(new StarRatingV1(1, 1, 1), null),
                // v1 里整条是 null（老代码 default! 占位）
                ["empty"] = null,
            };
            File.WriteAllBytes(path, MessagePackSerializer.Serialize(legacy));

            var loaded = ManiaSRPackStore.Load(path);

            Assert.True(loaded.MigratedFromV1);
            Assert.Equal(4, loaded.Data.Count);
            Assert.Equal(SRState.Ok, loaded.Data["ok"].StateOf(ManiaSRAlgorithm.XXY));
            Assert.Equal(5.2, loaded.Data["ok"].XXY!.HT);

            Assert.Equal(SRState.Failed, loaded.Data["zero"].StateOf(ManiaSRAlgorithm.XXY));
            Assert.Equal(SRState.Pending, loaded.Data["null"].StateOf(ManiaSRAlgorithm.XXY));

            Assert.Equal(SRState.Pending, loaded.Data["empty"].StateOf(ManiaSRAlgorithm.PPY));
            Assert.Equal(SRState.Pending, loaded.Data["empty"].StateOf(ManiaSRAlgorithm.XXY));

            foreach (var md5 in new[] { "ok", "zero", "null" })
                Assert.Equal(SRState.Ok, loaded.Data[md5].StateOf(ManiaSRAlgorithm.PPY));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StateBitfield_IsIndependentPerAlgorithm()
    {
        var data = ManiaSRData.Empty
            .With(ManiaSRAlgorithm.PPY, SRState.Ok, new StarRating(1, 2, 3))
            .With(ManiaSRAlgorithm.XXY, SRState.Unsupported)
            .With(ManiaSRAlgorithm.SPM, SRState.Failed);

        Assert.Equal(SRState.Ok, data.StateOf(ManiaSRAlgorithm.PPY));
        Assert.Equal(SRState.Unsupported, data.StateOf(ManiaSRAlgorithm.XXY));
        Assert.Equal(SRState.Failed, data.StateOf(ManiaSRAlgorithm.SPM));

        var reimported = data.Pending(ManiaSRAlgorithm.PPY);
        Assert.Equal(SRState.Pending, reimported.StateOf(ManiaSRAlgorithm.PPY));
        Assert.Null(reimported.PPY);
        Assert.Equal(SRState.Unsupported, reimported.StateOf(ManiaSRAlgorithm.XXY));
    }
}
