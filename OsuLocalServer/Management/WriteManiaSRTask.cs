using OsuLocalServer.Lazer;
using OsuLocalServer.Settings;
using OsuLocalServer.Stable;

namespace OsuLocalServer.Management;

public enum ManiaSRTarget { Stable, Lazer }

public class WriteManiaSRTask
{
    public ManiaSRTarget Target { get; set; } = ManiaSRTarget.Stable;
    public ManiaSRAlgorithm Algorithm { get; set; } = ManiaSRAlgorithm.XXY;

    public TaskHandler Create() => (sp, log, ct) =>
    {
        var settings = sp.GetRequiredService<SettingService>();
        var packPath = settings.Settings.Management.ManiaSRPackPath;

        if (!File.Exists(packPath))
        {
            log.Warn($"msgpack 文件不存在: {packPath}");
            return Task.CompletedTask;
        }

        var data = ManiaSRPackStore.Load(packPath).Data;
        if (data.Count == 0)
        {
            log.Warn("msgpack 数据为空");
            return Task.CompletedTask;
        }

        // 只写有值的槽位：未计算 / 失败 / 不适用的谱面保持库中原值，不回退到其它算法
        var ratings = new Dictionary<string, StarRating>();
        int pending = 0, failed = 0, unsupported = 0;
        foreach (var (md5, entry) in data)
        {
            switch (entry.StateOf(Algorithm))
            {
                case SRState.Ok:
                    if (entry.RatingOf(Algorithm) is { IsValid: true } rating)
                        ratings[md5] = rating;
                    else
                        failed++;
                    break;
                case SRState.Pending:
                    pending++;
                    break;
                case SRState.Failed:
                    failed++;
                    break;
                default:
                    unsupported++;
                    break;
            }
        }

        log.Info($"共 {data.Count} 条：可写 {ratings.Count}，未计算 {pending}，失败 {failed}，不适用 {unsupported}");

        if (Algorithm == ManiaSRAlgorithm.SPM)
            log.Warn("SPM 的 4K / 6K 输出是模型自身的等级值，不是星级（只有 7K 是星级量纲），写入后这些键数的谱面在游戏内显示的不是星数");
        if (ratings.Count == 0)
        {
            log.Warn("没有可写入的数据");
            return Task.CompletedTask;
        }

        int updated = Target == ManiaSRTarget.Stable
            ? WriteToStable(settings.Settings.Stable, ratings, log)
            : WriteToLazer(settings.Settings.Lazer, ratings, log);

        if (updated > 0 && Target == ManiaSRTarget.Stable)
        {
            var management = settings.Settings.Management;
            management.LastWrittenAlgorithm = Algorithm.ToString();
            management.LastWrittenAt = DateTimeOffset.Now;
            management.LastWrittenCount = updated;
            settings.Settings.Save();
        }

        log.Info($"{Target} 写入完成：更新 {updated} 个谱面（算法 {Algorithm}，空槽位的谱面保持原值）");
        return Task.CompletedTask;
    };

    private static int WriteToStable(StableSettings stable, Dictionary<string, StarRating> ratings, TaskLogger log)
    {
        if (!stable.IsAvailable)
        {
            log.Warn("Stable 不可用，跳过");
            return 0;
        }

        var osuDbPath = Path.Combine(stable.OsuRootPath, "osu!.db");
        return StableDatabase.WriteManiaStarRatings(osuDbPath, ratings, stable.BackupBeforeWrite);
    }

    private static int WriteToLazer(LazerSettings lazer, Dictionary<string, StarRating> ratings, TaskLogger log)
    {
        if (!lazer.IsAvailable)
        {
            log.Warn("Lazer 不可用，跳过");
            return 0;
        }

        // Lazer 数据库只有单个 SR 字段，只写 NM
        var nmOnly = ratings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.NM);
        return LazerRealm.WriteStarRatings(lazer.ClientRealmPath, nmOnly);
    }
}
