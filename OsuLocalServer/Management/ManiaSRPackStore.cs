using System.Collections.Concurrent;
using MessagePack;

namespace OsuLocalServer.Management;

/// <summary>v1 包的星数格式（字段名入包），仅用于读取旧文件。</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record class StarRatingV1(double NM, double HT, double DT);

/// <summary>v1 包的谱面数据，仅用于读取旧文件。</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record class ManiaSRDataV1(StarRatingV1? PPY, StarRatingV1? XXY);

/// <summary>
/// SR 数据包（mania_sr.msgpack）的读写：v2 为紧凑数组格式，读取时自动识别并迁移 v1。
/// </summary>
public static class ManiaSRPackStore
{
    public sealed record LoadResult(
        ConcurrentDictionary<string, ManiaSRData> Data,
        bool MigratedFromV1,
        int MigratedCount);

    public static LoadResult Load(string path)
    {
        if (!File.Exists(path))
            return new LoadResult(new ConcurrentDictionary<string, ManiaSRData>(), false, 0);

        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
            return new LoadResult(new ConcurrentDictionary<string, ManiaSRData>(), false, 0);

        if (IsCompactFormat(bytes))
        {
            var dict = MessagePackSerializer.Deserialize<Dictionary<string, ManiaSRData?>>(bytes) ?? [];
            var compact = new ConcurrentDictionary<string, ManiaSRData>();
            foreach (var (md5, entry) in dict)
                compact[md5] = entry ?? ManiaSRData.Empty;
            return new LoadResult(compact, false, 0);
        }

        // v1 里失败/未算的条目可能整条是 null（老代码用 default! 占位）
        var legacy = MessagePackSerializer.Deserialize<Dictionary<string, ManiaSRDataV1?>>(bytes) ?? [];
        var data = new ConcurrentDictionary<string, ManiaSRData>();
        foreach (var (md5, entry) in legacy)
            data[md5] = Migrate(entry);
        return new LoadResult(data, true, data.Count);
    }

    public static void Save(IReadOnlyDictionary<string, ManiaSRData> data, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var bytes = MessagePackSerializer.Serialize(data);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    public static void Backup(string path)
    {
        if (File.Exists(path))
            Utils.BackupFile(path);
    }

    /// <summary>
    /// v2 每条记录是数组，v1 是带字段名的 map。只看第一个值的数据类型即可判定。
    /// </summary>
    private static bool IsCompactFormat(byte[] bytes)
    {
        if (bytes.Length == 0)
            return true;

        var reader = new MessagePackReader(bytes);
        if (reader.NextMessagePackType == MessagePackType.Nil)
            return true;

        int count = reader.ReadMapHeader();
        if (count <= 0)
            return true;

        _ = reader.ReadString();
        return reader.NextMessagePackType == MessagePackType.Array;
    }

    /// <summary>
    /// v1 → v2。v1 的 XXY 全 0 表示当时计算出错，按新规则记为终态不再重试；
    /// null 表示当时根本没算出来（Lazer 异常残留），留 Pending 让它重算。
    /// </summary>
    private static ManiaSRData Migrate(ManiaSRDataV1? v1)
    {
        var data = ManiaSRData.Empty;
        if (v1 is null)
            return data;

        if (v1.PPY is { } ppy)
        {
            var rating = new StarRating(ppy.NM, ppy.HT, ppy.DT);
            data = rating.IsValid
                ? data.With(ManiaSRAlgorithm.PPY, SRState.Ok, rating)
                : data.Pending(ManiaSRAlgorithm.PPY);
        }

        if (v1.XXY is { } xxy)
        {
            var rating = new StarRating(xxy.NM, xxy.HT, xxy.DT);
            data = rating.IsValid
                ? data.With(ManiaSRAlgorithm.XXY, SRState.Ok, rating)
                : data.With(ManiaSRAlgorithm.XXY, SRState.Failed);
        }

        return data;
    }
}
