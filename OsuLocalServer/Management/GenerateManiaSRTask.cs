using System.Collections.Concurrent;
using MessagePack;
using OsuLocalServer.Lazer;
using OsuLocalServer.Settings;
using OsuLocalServer.Stable;
using OsuParsers.Decoders;
using OsuParsers.Enums;
using osu.Game.Beatmaps;
using StarRatingRebirth;

namespace OsuLocalServer.Management;

public class GenerateManiaSRTask
{
    public int Parallelism { get; set; } = Math.Max(1, Environment.ProcessorCount - 2);
    public bool LogXxyErrors { get; set; } = true;
    public bool SaveCheckpoint { get; set; } = true;

    public TaskHandler Create() => async (sp, log, ct) =>
    {
        var settings = sp.GetRequiredService<SettingService>();
        var msgpackPath = settings.Settings.Management.ManiaSRPackPath;

        ConcurrentDictionary<string, ManiaSRData> data = [];
        if (File.Exists(msgpackPath))
        {
            var bytes = await File.ReadAllBytesAsync(msgpackPath, ct);
            var existing = MessagePackSerializer.Deserialize<Dictionary<string, ManiaSRData>>(bytes);
            if (existing is not null)
                data = new ConcurrentDictionary<string, ManiaSRData>(existing);
            log.Info($"已加载 {data.Count} 条现有数据, 并行数: {Parallelism}, 记录 XXY 错误: {LogXxyErrors}");
        }

        var stable = settings.Settings.Stable;
        if (stable.IsAvailable)
            ProcessStable(stable, data, msgpackPath, log, ct);
        else
            log.Warn("Stable 不可用，跳过");

        var lazer = settings.Settings.Lazer;
        if (lazer.IsAvailable)
            ProcessLazer(lazer, data, msgpackPath, log, ct);
        else
            log.Warn("Lazer 不可用，跳过");

        SaveData(data, msgpackPath, log);
    };

    private readonly object _saveLock = new();

    private void SaveData(ConcurrentDictionary<string, ManiaSRData> data, string path, TaskLogger log)
    {
        lock (_saveLock)
        {
            Directory.CreateDirectory(AppSettings.StorageDir);
            var outputBytes = MessagePackSerializer.Serialize(data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
            File.WriteAllBytes(path, outputBytes);
            log.Info($"已保存 {data.Count} 条数据到 {path}");
        }
    }

    private void ProcessStable(
        StableSettings stable,
        ConcurrentDictionary<string, ManiaSRData> data,
        string msgpackPath,
        TaskLogger log,
        CancellationToken ct)
    {
        var osuDbPath = Path.Combine(stable.OsuRootPath, "osu!.db");
        var osuDb = DatabaseDecoder.DecodeOsu(osuDbPath);
        var maniaBeatmaps = osuDb.Beatmaps.Where(b => b.Ruleset == Ruleset.Mania).ToList();
        var total = maniaBeatmaps.Count;
        log.Info($"Stable: 共 {total} 个 Mania 谱面");

        int processed = 0, xxyCount = 0, errorCount = 0;
        var parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct };

        Parallel.ForEach(maniaBeatmaps, parallelOpts, bm =>
        {
            if (!data.TryAdd(bm.MD5Hash, default!)) return;

            var p = Interlocked.Increment(ref processed);
            if (p % 500 == 0)
                log.Info($"进度: {p}/{total}");

            StarRating ppy;
            StarRating xxy;

            if (bm.ManiaStarRating[Mods.None] == bm.ManiaStarRating[Mods.Easy])
            {
                ppy = new StarRating(
                    bm.ManiaStarRating[Mods.None],
                    bm.ManiaStarRating[Mods.HalfTime],
                    bm.ManiaStarRating[Mods.DoubleTime]
                );
                Interlocked.Increment(ref xxyCount);
                try
                {
                    xxy = CalculateXXY(bm.GetBeatmapPath(stable.OsuRootPath));
                    data[bm.MD5Hash] = new ManiaSRData(ppy, xxy);
                    if (SaveCheckpoint && (xxyCount - errorCount) % 5000 == 0)
                        SaveData(data, msgpackPath, log);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref errorCount);
                    if (LogXxyErrors)
                        log.Warn($"XXY 计算 {bm.Artist} - {bm.Title} [{bm.Difficulty}] 出错: {ex.Message}");
                    xxy = new StarRating(0, 0, 0);
                    data[bm.MD5Hash] = new ManiaSRData(ppy, xxy);
                }
            }
            else
            {
                ppy = new StarRating(
                    bm.ManiaStarRating[Mods.Easy],
                    bm.ManiaStarRating[Mods.HalfTime | Mods.Easy],
                    bm.ManiaStarRating[Mods.DoubleTime | Mods.Easy]
                );
                xxy = new StarRating(
                    bm.ManiaStarRating[Mods.None],
                    bm.ManiaStarRating[Mods.HalfTime],
                    bm.ManiaStarRating[Mods.DoubleTime]
                );
                data[bm.MD5Hash] = new ManiaSRData(ppy, xxy);
            }
        });

        log.Info($"Stable 处理完成：{processed} 个谱面, XXY 计算 {xxyCount} 次，出错 {errorCount} 次");
    }

    private void ProcessLazer(
        LazerSettings lazer,
        ConcurrentDictionary<string, ManiaSRData> data,
        string msgpackPath,
        TaskLogger log,
        CancellationToken ct)
    {
        var dataDir = Path.GetDirectoryName(lazer.ClientRealmPath)!;
        using var realm = LazerRealm.OpenRealm(lazer.ClientRealmPath);

        var entries = new List<(string MD5Hash, string Hash)>();
        foreach (var bm in realm.All<BeatmapInfo>())
        {
            if (bm.Ruleset?.ShortName == "mania" && !string.IsNullOrEmpty(bm.MD5Hash) && !string.IsNullOrEmpty(bm.Hash))
                entries.Add((bm.MD5Hash, bm.Hash));
        }

        var total = entries.Count;
        log.Info($"Lazer: 共 {total} 个 Mania 谱面");

        int processed = 0, batchCount = 0;
        var parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct };

        Parallel.ForEach(entries, parallelOpts, entry =>
        {
            var osuPath = Path.Combine(dataDir, "files", entry.Hash[..1], entry.Hash[..2], entry.Hash);
            if (!File.Exists(osuPath)) return;

            if (!data.TryAdd(entry.MD5Hash, default!)) return;

            var p = Interlocked.Increment(ref processed);
            if (p % 500 == 0)
                log.Info($"Lazer 进度: {p}/{total}");

            try
            {
                var ppy = LazerRulesets.CalcManiaSR(osuPath);
                var xxy = CalculateXXY(osuPath);
                data[entry.MD5Hash] = new ManiaSRData(ppy, xxy);

                var b = Interlocked.Increment(ref batchCount);
                if (SaveCheckpoint && b % 5000 == 0)
                    SaveData(data, msgpackPath, log);
            }
            catch (Exception ex)
            {
                if (LogXxyErrors)
                    log.Warn($"Lazer 计算 {entry.MD5Hash} 出错: {ex.Message}");
            }
        });

        log.Info($"Lazer 处理完成：{processed} 个谱面");
    }

    private StarRating CalculateXXY(string beatmapPath)
    {
        var data = ManiaData.FromFile(beatmapPath);
        return new StarRating(
            NM: SRCalculator.Calculate(data),
            HT: SRCalculator.Calculate(data.HT()),
            DT: SRCalculator.Calculate(data.DT())
        );
    }
}
