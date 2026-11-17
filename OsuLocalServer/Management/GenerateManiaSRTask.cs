using System.Collections.Concurrent;
using OsuLazer.Interop;
using OsuLocalServer.Settings;
using OsuLocalServer.Stable;
using OsuParsers.Database.Objects;
using OsuParsers.Decoders;
using OsuParsers.Enums;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Mods;
using Realms;
using StarRatingRebirth;
using Spm = SPMRating;

namespace OsuLocalServer.Management;

/// <summary>
/// 生成 Mania SR 数据包。
/// <para>PPY 只能从 osu!.db / Lazer ruleset 得到，且只在「首次导入」时读库；其余算法一律本地计算。</para>
/// <para>每个算法槽位带状态：Pending 会补算，Failed/Unsupported 是终态，不再重试。</para>
/// </summary>
public class GenerateManiaSRTask
{
    public int Parallelism { get; set; } = Math.Max(1, Environment.ProcessorCount - 2);
    public bool LogXxyErrors { get; set; } = true;
    public bool SaveCheckpoint { get; set; } = true;
    public bool ComputeXxy { get; set; } = true;
    public bool ComputeSpm { get; set; } = true;
    /// <summary>清空已有 PPY，从库中重新导入。</summary>
    public bool ReimportPpy { get; set; }
    /// <summary>把 Failed 槽位重置为 Pending，本次重试。</summary>
    public bool ResetFailed { get; set; }

    private readonly object _saveLock = new();

    private sealed record WorkItem(string MD5Hash, string BeatmapPath);

    public TaskHandler Create() => async (sp, log, ct) =>
    {
        var settings = sp.GetRequiredService<SettingService>();
        var packPath = settings.Settings.Management.ManiaSRPackPath;

        var loaded = ManiaSRPackStore.Load(packPath);
        var data = loaded.Data;

        if (loaded.MigratedFromV1)
        {
            ManiaSRPackStore.Backup(packPath);
            log.Info($"旧格式数据包已迁移为紧凑格式：{loaded.MigratedCount} 条（旧文件已备份为 .bakN）");
            SaveData(data, packPath, log, quiet: true);
        }

        log.Info($"已加载 {data.Count} 条现有数据，并行数：{Parallelism}，计算：{DescribeAlgorithms()}");

        if (ReimportPpy)
        {
            foreach (var key in data.Keys)
                data[key] = data[key].Pending(ManiaSRAlgorithm.PPY);
            log.Info("已清空 PPY，本次从库中重新导入");
        }

        if (ResetFailed)
            log.Info($"已把 {ResetFailedSlots(data)} 个失败槽位重置为未计算，本次会重试");

        var work = new List<WorkItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int ppyImported = 0;

        var stable = settings.Settings.Stable;
        var lazer = settings.Settings.Lazer;
        bool lazerAvailable = lazer.IsAvailable;

        if (stable.IsAvailable)
            ppyImported = CollectStable(stable, data, work, seen, log, lazerAvailable);
        else
            log.Warn("Stable 不可用，跳过（PPY 无法从 osu!.db 导入）");

        if (lazerAvailable)
            CollectLazer(lazer, data, work, seen, log);
        else
            log.Warn("Lazer 不可用，跳过（PPY 未计算的条目不进入待计算）");

        if (ppyImported > 0)
            SaveData(data, packPath, log, quiet: true);

        // 只有两个来源都扫过，"没出现过" 才能确定是孤儿，否则会误删另一个库里的谱面
        if (stable.IsAvailable && lazerAvailable)
            PruneOrphans(data, seen, packPath, log);
        else
            log.Warn("Stable / Lazer 未全部可用，跳过孤儿条目清理");

        Process(data, work, packPath, log, lazerAvailable, ct);

        SaveData(data, packPath, log);
    };

    /// <summary>
    /// 从 osu!.db 枚举 Mania 谱面：建条目 + 首次导入 PPY + 收集待计算的谱面路径。
    /// 只有确实有 Pending 槽位的谱面才会进入 <paramref name="work"/>。
    /// </summary>
    private int CollectStable(
        StableSettings stable,
        ConcurrentDictionary<string, ManiaSRData> data,
        List<WorkItem> work,
        HashSet<string> seen,
        TaskLogger log,
        bool lazerAvailable)
    {
        var osuDbPath = Path.Combine(stable.OsuRootPath, "osu!.db");
        var osuDb = DatabaseDecoder.DecodeOsu(osuDbPath);
        var beatmaps = osuDb.Beatmaps
            .Where(b => b.Ruleset == Ruleset.Mania && !string.IsNullOrEmpty(b.MD5Hash))
            .ToList();

        log.Info($"Stable：共 {beatmaps.Count} 个 Mania 谱面");

        int imported = 0, missing = 0, queued = 0;
        foreach (var bm in beatmaps)
        {
            var md5 = bm.MD5Hash!;
            var entry = data.GetOrAdd(md5, ManiaSRData.Empty);
            seen.Add(md5);

            if (entry.StateOf(ManiaSRAlgorithm.PPY) == SRState.Pending)
            {
                var ppy = ReadPpyFromStable(bm);
                if (ppy.IsValid)
                {
                    entry = entry.With(ManiaSRAlgorithm.PPY, SRState.Ok, ppy);
                    data[md5] = entry;
                    imported++;
                }
                else
                {
                    missing++;
                }
            }

            if (NeedsWork(entry, lazerAvailable))
            {
                work.Add(new WorkItem(md5, bm.GetBeatmapPath(stable.OsuRootPath)));
                queued++;
            }
        }

        log.Info($"PPY 导入：新增 {imported} 条，库中无可用 PPY {missing} 条（保持未计算）；待计算 {queued} 条");
        return imported;
    }

    /// <summary>
    /// PPY 只能从 osu!.db 读。本工具只写 None/HalfTime/DoubleTime 三槽，
    /// 因此 None == Easy 时说明三槽就是 ppy 原值，否则 EZ 三槽才是 ppy 原值。
    /// </summary>
    private static StarRating ReadPpyFromStable(DbBeatmap bm)
    {
        try
        {
            double none = bm.ManiaStarRating[Mods.None];
            double easy = bm.ManiaStarRating[Mods.Easy];
            if (none == easy)
            {
                return new StarRating(
                    none,
                    bm.ManiaStarRating[Mods.HalfTime],
                    bm.ManiaStarRating[Mods.DoubleTime]);
            }

            return new StarRating(
                easy,
                bm.ManiaStarRating[Mods.HalfTime | Mods.Easy],
                bm.ManiaStarRating[Mods.DoubleTime | Mods.Easy]);
        }
        catch (Exception)
        {
            // 老版本 osu!.db 可能没有 EZ 组合槽位
            return StarRating.Zero;
        }
    }

    /// <summary>
    /// 从 client.realm 枚举 Mania 谱面。模式过滤交给 Realm（RQL），并且只对
    /// 确实需要计算的谱面做文件存在性检查——全量 File.Exists 是这一阶段的耗时大头。
    /// </summary>
    private void CollectLazer(
        LazerSettings lazer,
        ConcurrentDictionary<string, ManiaSRData> data,
        List<WorkItem> work,
        HashSet<string> seen,
        TaskLogger log)
    {
        var database = new LazerDatabase(lazer.ClientRealmPath);
        var fileStore = new LazerFileStore(database.DataDirectory);
        using var realm = database.Open();

        var entries = new List<(string MD5Hash, string Hash)>();
        foreach (var bm in realm.All<BeatmapInfo>().Filter("Ruleset.ShortName == \"mania\""))
        {
            if (!string.IsNullOrEmpty(bm.MD5Hash) && !string.IsNullOrEmpty(bm.Hash))
                entries.Add((bm.MD5Hash, bm.Hash));
        }

        log.Info($"Lazer：共 {entries.Count} 个 Mania 谱面");

        int added = 0, missingFile = 0, queued = 0, fromStable = 0;
        foreach (var entry in entries)
        {
            // Stable 已经收录过（路径与待算状态都已确定），不重复检查文件
            if (!seen.Add(entry.MD5Hash))
            {
                fromStable++;
                continue;
            }

            if (data.TryGetValue(entry.MD5Hash, out var existing) && !NeedsWork(existing, lazerAvailable: true))
                continue;

            if (!fileStore.TryGetFilePath(entry.Hash, out var osuPath))
            {
                missingFile++;
                continue;
            }

            if (data.TryAdd(entry.MD5Hash, ManiaSRData.Empty))
                added++;

            work.Add(new WorkItem(entry.MD5Hash, osuPath));
            queued++;
        }

        log.Info($"Lazer：新增 {added} 条，待计算 {queued} 条，文件缺失 {missingFile} 条，已在 Stable 收录 {fromStable} 条");
    }

    /// <summary>
    /// 该条目是否还有本次要算的槽位。PPY 需要 Lazer 可用，XXY / SPM 取决于开关。
    /// 已停用的算法、Lazer 不可用时的 PPY 都不算，否则任务每轮都会空跑一遍全部谱面。
    /// </summary>
    private bool NeedsWork(ManiaSRData entry, bool lazerAvailable) =>
        (lazerAvailable && entry.StateOf(ManiaSRAlgorithm.PPY) == SRState.Pending)
        || (ComputeXxy && entry.StateOf(ManiaSRAlgorithm.XXY) == SRState.Pending)
        || (ComputeSpm && entry.StateOf(ManiaSRAlgorithm.SPM) == SRState.Pending);

    /// <summary>
    /// 清掉 Stable / Lazer 里都已不存在的条目：它们永远算不出来，只会让数据包越滚越大。
    /// 清理前先备份整包，避免误删后要重算。
    /// </summary>
    private static void PruneOrphans(
        ConcurrentDictionary<string, ManiaSRData> data,
        HashSet<string> seen,
        string packPath,
        TaskLogger log)
    {
        var orphans = new List<string>();
        foreach (var key in data.Keys)
        {
            if (!seen.Contains(key))
                orphans.Add(key);
        }

        if (orphans.Count == 0)
            return;

        ManiaSRPackStore.Backup(packPath);

        int removed = 0;
        foreach (var key in orphans)
        {
            if (data.TryRemove(key, out _))
                removed++;
        }

        log.Info($"已清理 {removed} 条孤儿条目（Stable / Lazer 中都已不存在），清理前已备份为 .bakN");
    }

    private void Process(
        ConcurrentDictionary<string, ManiaSRData> data,
        List<WorkItem> work,
        string packPath,
        TaskLogger log,
        bool lazerAvailable,
        CancellationToken ct)
    {
        // work 里只放确实有 Pending 槽位的谱面，所以进度分母就是待计算数，
        // 已算完的谱面不会让循环再空跑一遍。
        // Stable 与 Lazer 可能有同一张谱，先出现（Stable 优先）的路径胜出
        var items = work
            .GroupBy(w => w.MD5Hash, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        if (items.Count == 0)
        {
            log.Info("没有待计算的谱面");
            return;
        }

        log.Info($"待计算 {items.Count} 个谱面");

        int done = 0, written = 0;
        int ppyOk = 0, ppyFailed = 0;
        int xxyOk = 0, xxyFailed = 0;
        int spmOk = 0, spmFailed = 0, spmUnsupported = 0;

        var parallelOpts = new ParallelOptions
        {
            MaxDegreeOfParallelism = Parallelism,
            CancellationToken = ct,
        };

        Parallel.ForEach(items, parallelOpts, item =>
        {
            var entry = data[item.MD5Hash];
            bool changed = false;

            if (entry.StateOf(ManiaSRAlgorithm.PPY) == SRState.Pending && lazerAvailable)
            {
                try
                {
                    var ppy = CalcLazerManiaSR(item.BeatmapPath);
                    entry = entry.With(ManiaSRAlgorithm.PPY, SRState.Ok, ppy);
                    Interlocked.Increment(ref ppyOk);
                }
                catch (Exception ex)
                {
                    entry = entry.With(ManiaSRAlgorithm.PPY, SRState.Failed);
                    Interlocked.Increment(ref ppyFailed);
                    if (LogXxyErrors)
                        log.Warn($"PPY 计算 {item.MD5Hash} 出错: {ex.Message}");
                }
                changed = true;
            }

            if (ComputeXxy && entry.StateOf(ManiaSRAlgorithm.XXY) == SRState.Pending)
            {
                try
                {
                    entry = entry.With(ManiaSRAlgorithm.XXY, SRState.Ok, CalculateXXY(item.BeatmapPath));
                    Interlocked.Increment(ref xxyOk);
                }
                catch (Exception ex)
                {
                    entry = entry.With(ManiaSRAlgorithm.XXY, SRState.Failed);
                    Interlocked.Increment(ref xxyFailed);
                    if (LogXxyErrors)
                        log.Warn($"XXY 计算 {item.MD5Hash} 出错: {ex.Message}");
                }
                changed = true;
            }

            if (ComputeSpm && entry.StateOf(ManiaSRAlgorithm.SPM) == SRState.Pending)
            {
                var (state, rating, error) = CalculateSpm(item.BeatmapPath);
                entry = entry.With(ManiaSRAlgorithm.SPM, state, rating);
                switch (state)
                {
                    case SRState.Ok:
                        Interlocked.Increment(ref spmOk);
                        break;
                    case SRState.Unsupported:
                        Interlocked.Increment(ref spmUnsupported);
                        break;
                    default:
                        Interlocked.Increment(ref spmFailed);
                        if (LogXxyErrors)
                            log.Warn($"SPM 计算 {item.MD5Hash} 失败: {error}");
                        break;
                }
                changed = true;
            }

            if (changed)
            {
                data[item.MD5Hash] = entry;
                int w = Interlocked.Increment(ref written);
                if (SaveCheckpoint && w % 5000 == 0)
                    SaveData(data, packPath, log);
            }

            int p = Interlocked.Increment(ref done);
            if (p % 500 == 0)
                log.Info($"进度: {p}/{items.Count}");
        });

        log.Info(
            $"计算完成：PPY 补算 {ppyOk}（失败 {ppyFailed}），" +
            $"XXY 成功 {xxyOk}/失败 {xxyFailed}，" +
            $"SPM 成功 {spmOk}/失败 {spmFailed}/不适用 {spmUnsupported}");
    }

    private int ResetFailedSlots(ConcurrentDictionary<string, ManiaSRData> data)
    {
        int count = 0;
        foreach (var key in data.Keys)
        {
            var entry = data[key];
            foreach (var algorithm in Enum.GetValues<ManiaSRAlgorithm>())
            {
                if (entry.StateOf(algorithm) != SRState.Failed)
                    continue;
                entry = entry.Pending(algorithm);
                count++;
            }
            data[key] = entry;
        }
        return count;
    }

    private string DescribeAlgorithms()
    {
        var parts = new List<string> { "PPY" };
        if (ComputeXxy) parts.Add("XXY");
        if (ComputeSpm) parts.Add("SPM");
        return string.Join(" / ", parts);
    }

    private void SaveData(
        ConcurrentDictionary<string, ManiaSRData> data,
        string path,
        TaskLogger log,
        bool quiet = false)
    {
        lock (_saveLock)
        {
            ManiaSRPackStore.Save(data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value), path);
            if (!quiet)
                log.Info($"已保存 {data.Count} 条数据到 {path}");
        }
    }

    private static StarRating CalculateXXY(string beatmapPath)
    {
        var data = ManiaData.FromFile(beatmapPath);
        return new StarRating(
            NM: SRCalculator.Calculate(data),
            HT: SRCalculator.Calculate(data.HT()),
            DT: SRCalculator.Calculate(data.DT())
        );
    }

    /// <summary>
    /// SPM 只支持 4K/6K/7K，其余键数记 Unsupported（终态）。
    /// 取 ReferenceScore（已按模型校准），不是 Calculate() 的 RawScore。
    /// </summary>
    private static (SRState State, StarRating? Rating, string? Error) CalculateSpm(string beatmapPath)
    {
        Spm.ManiaData data;
        try
        {
            data = Spm.ManiaData.FromFile(beatmapPath);
        }
        catch (Exception ex)
        {
            return (SRState.Failed, null, ex.Message);
        }

        if (data.CS is not (4 or 6 or 7))
            return (SRState.Unsupported, null, $"{data.CS}K 不受支持（仅 4K/6K/7K）");

        double[] rates = [1.0, 0.75, 1.5];
        var values = new double[3];
        try
        {
            for (int i = 0; i < rates.Length; i++)
            {
                if (!Spm.SPMCalculator.TryCalculateDetailed(data.WithSpeedRate(rates[i]), out var result, out var error))
                    return (SRState.Failed, null, $"{error} (rate {rates[i]})");

                if (!double.IsFinite(result.ReferenceScore))
                    return (SRState.Failed, null, $"ReferenceScore 非有限值 (rate {rates[i]})");

                values[i] = result.ReferenceScore;
            }
        }
        catch (Exception ex)
        {
            return (SRState.Failed, null, ex.Message);
        }

        return (SRState.Ok, new StarRating(values[0], values[1], values[2]), null);
    }

    /// <summary>
    /// OsuLazer.Interop 只提供通用的单次 SR 计算，mania 需要的 NM/HT/DT 组合由这里组装。
    /// </summary>
    private static StarRating CalcLazerManiaSR(string beatmapPath)
    {
        var calculator = LazerStarRatingCalculator.Default;

        return new StarRating(
            NM: calculator.CalcSR(beatmapPath, []),
            HT: calculator.CalcSR(beatmapPath, [new ManiaModHalfTime()]),
            DT: calculator.CalcSR(beatmapPath, [new ManiaModDoubleTime()]));
    }
}
