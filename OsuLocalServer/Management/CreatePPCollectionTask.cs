using MessagePack;
using OsuLocalServer.Lazer;
using OsuLocalServer.Settings;
using OsuLocalServer.Stable;
using OsuParsers.Decoders;
using OsuParsers.Enums;
using OsuParsers.Enums.Database;
using osu.Game.Beatmaps;

namespace OsuLocalServer.Management;

public enum PPCollectionSource { Stable, Lazer }

public class CreatePPCollectionTask
{
    public PPCollectionSource Source { get; set; } = PPCollectionSource.Stable;
    public double Threshold { get; set; } = 0.2;
    public double MinPPY { get; set; } = 5;

    public TaskHandler Create() => (sp, log, ct) =>
    {
        var settings = sp.GetRequiredService<SettingService>();
        var msgpackPath = settings.Settings.Management.ManiaSRPackPath;

        if (!File.Exists(msgpackPath))
        {
            log.Warn($"msgpack 文件不存在: {msgpackPath}");
            log.Info("请先运行「生成 Mania SR」任务");
            return Task.CompletedTask;
        }

        var bytes = File.ReadAllBytes(msgpackPath);
        var data = MessagePackSerializer.Deserialize<Dictionary<string, ManiaSRData>>(bytes);
        if (data is null || data.Count == 0)
        {
            log.Warn("msgpack 数据为空");
            log.Info("请先运行「生成 Mania SR」任务");
            return Task.CompletedTask;
        }

        log.Info($"已加载 {data.Count} 条 Mania SR 数据, XXY-PPY < {Threshold}, PPY > {MinPPY}");

        if (Source == PPCollectionSource.Stable)
            ProcessStable(settings.Settings.Stable, data, log);
        else
            ProcessLazer(settings.Settings.Lazer, data, log);

        return Task.CompletedTask;
    };

    private void ProcessStable(StableSettings stable, Dictionary<string, ManiaSRData> data, TaskLogger log)
    {
        if (!stable.IsAvailable)
        {
            log.Warn("Stable 不可用，跳过");
            return;
        }

        var osuDbPath = Path.Combine(stable.OsuRootPath, "osu!.db");
        if (!File.Exists(osuDbPath))
        {
            log.Warn($"osu!.db 未找到: {osuDbPath}");
            return;
        }

        var osuDb = DatabaseDecoder.DecodeOsu(osuDbPath);

        // 只取 Mania 且 Ranked 状态的谱面
        var rankedBeatmaps = osuDb.Beatmaps
            .Where(b => b.Ruleset == Ruleset.Mania && b.RankedStatus == RankedStatus.Ranked)
            .ToList();

        log.Info($"Stable: 共 {rankedBeatmaps.Count} 个 Ranked Mania 谱面");

        CollectAndCreateCollections(rankedBeatmaps.Select(b => b.MD5Hash),
            data, stable.OsuRootPath, log, isStable: true, stable.BackupBeforeWrite);
    }

    private void ProcessLazer(LazerSettings lazer, Dictionary<string, ManiaSRData> data, TaskLogger log)
    {
        if (!lazer.IsAvailable)
        {
            log.Warn("Lazer 不可用，跳过");
            return;
        }

        List<string> md5Hashes;
        using (var realm = LazerRealm.OpenRealm(lazer.ClientRealmPath))
        {
            // 在 C# 中过滤 Mania + Ranked (Realm 表达式树不支持 ?. 和某些类型转换)
            var rankedBeatmaps = realm.All<BeatmapInfo>()
                .ToList()
                .Where(b => b.Ruleset?.ShortName == "mania"
                         && !string.IsNullOrEmpty(b.MD5Hash)
                         && b.Status == BeatmapOnlineStatus.Ranked)
                .ToList();

            md5Hashes = rankedBeatmaps.Select(b => b.MD5Hash).ToList();
            log.Info($"Lazer: 共 {md5Hashes.Count} 个 Ranked Mania 谱面");
        }

        // realm 已关闭，再以写入模式打开创建收藏夹
        CollectAndCreateCollections(md5Hashes,
            data, lazer.ClientRealmPath, log, isStable: false, backup: false);
    }

    private void CollectAndCreateCollections(
        IEnumerable<string> md5Hashes,
        Dictionary<string, ManiaSRData> data,
        string targetPath,
        TaskLogger log,
        bool isStable,
        bool backup)
    {
        var nmHashes = new List<string>();
        var htHashes = new List<string>();
        var dtHashes = new List<string>();

        int checkedCount = 0, skippedNoData = 0, skippedZeroXxy = 0;

        foreach (var md5 in md5Hashes)
        {
            checkedCount++;

            if (!data.TryGetValue(md5, out var sr) || sr is null)
            {
                skippedNoData++;
                continue;
            }

            if (sr.XXY is null || sr.PPY is null)
            {
                skippedZeroXxy++;
                continue;
            }

            // XXY - PPY < 阈值，且 XXY/PPY 有效
            if (sr.XXY.NM > 0 && sr.PPY.NM > MinPPY && sr.XXY.NM - sr.PPY.NM < Threshold)
                nmHashes.Add(md5);
            if (sr.XXY.HT > 0 && sr.PPY.HT > MinPPY && sr.XXY.HT - sr.PPY.HT < Threshold)
                htHashes.Add(md5);
            if (sr.XXY.DT > 0 && sr.PPY.DT > MinPPY && sr.XXY.DT - sr.PPY.DT < Threshold)
                dtHashes.Add(md5);
        }

        log.Info($"检查完成: 共 {checkedCount} 个谱面, 无 SR 数据: {skippedNoData}, XXY 无效: {skippedZeroXxy}");
        log.Info($"筛选结果: PP(NM)={nmHashes.Count}, PP HT={htHashes.Count}, PP DT={dtHashes.Count}");

        // 创建收藏夹
        if (isStable)
        {
            CreateStableCollection(targetPath, "PP", nmHashes, backup, log);
            CreateStableCollection(targetPath, "PP HT", htHashes, backup, log);
            CreateStableCollection(targetPath, "PP DT", dtHashes, backup, log);
        }
        else
        {
            CreateLazerCollection(targetPath, "PP", nmHashes, log);
            CreateLazerCollection(targetPath, "PP HT", htHashes, log);
            CreateLazerCollection(targetPath, "PP DT", dtHashes, log);
        }
    }

    private void CreateStableCollection(string osuRoot, string name, List<string> hashes, bool backup, TaskLogger log)
    {
        if (hashes.Count == 0)
        {
            log.Info($"收藏夹「{name}」: 无满足条件的谱面，跳过");
            return;
        }

        try
        {
            var result = StableDatabase.AddToCollection(osuRoot, name, hashes.ToArray(), overwrite: true, backup: backup);
            log.Info($"收藏夹「{name}」创建/更新完成: {result.BeatmapCount} 个谱面");
        }
        catch (InvalidOperationException ex)
        {
            log.Error($"收藏夹「{name}」创建失败: {ex.Message}");
        }
    }

    private void CreateLazerCollection(string clientRealmPath, string name, List<string> hashes, TaskLogger log)
    {
        if (hashes.Count == 0)
        {
            log.Info($"收藏夹「{name}」: 无满足条件的谱面，跳过");
            return;
        }

        try
        {
            var result = LazerRealm.AddToCollection(clientRealmPath, name, hashes.ToArray(), overwrite: true);
            log.Info($"收藏夹「{name}」创建/更新完成: {result.BeatmapCount} 个谱面");
        }
        catch (Exception ex)
        {
            log.Error($"收藏夹「{name}」创建失败: {ex.Message}");
        }
    }
}
