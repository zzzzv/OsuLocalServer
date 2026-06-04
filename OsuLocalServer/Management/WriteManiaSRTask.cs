using MessagePack;
using OsuLocalServer.Lazer;
using OsuLocalServer.Settings;
using OsuLocalServer.Stable;

namespace OsuLocalServer.Management;

public enum ManiaSRTarget { Stable, Lazer }
public enum ManiaSRAlgorithm { PPY, XXY }

public class WriteManiaSRTask
{
    public ManiaSRTarget Target { get; set; } = ManiaSRTarget.Stable;
    public ManiaSRAlgorithm Algorithm { get; set; } = ManiaSRAlgorithm.XXY;

    public TaskHandler Create() => (sp, log, ct) =>
    {
        var settings = sp.GetRequiredService<SettingService>();
        var msgpackPath = settings.Settings.Management.ManiaSRPackPath;

        if (!File.Exists(msgpackPath))
        {
            log.Warn($"msgpack 文件不存在: {msgpackPath}");
            return Task.CompletedTask;
        }

        var bytes = File.ReadAllBytes(msgpackPath);
        var data = MessagePackSerializer.Deserialize<Dictionary<string, ManiaSRData>>(bytes);
        if (data is null || data.Count == 0)
        {
            log.Warn("msgpack 数据为空");
            return Task.CompletedTask;
        }

        if (Target == ManiaSRTarget.Stable)
            WriteToStable(settings.Settings.Stable, data, log);
        else
            WriteToLazer(settings.Settings.Lazer, data, log);

        return Task.CompletedTask;
    };

    private void WriteToStable(StableSettings stable, Dictionary<string, ManiaSRData> data, TaskLogger log)
    {
        if (!stable.IsAvailable)
        {
            log.Warn("Stable 不可用，跳过");
            return;
        }

        var osuDbPath = Path.Combine(stable.OsuRootPath, "osu!.db");
        var ratings = data
            .Where(kvp => kvp.Value is not null)
            .ToDictionary(kvp => kvp.Key, kvp => Algorithm == ManiaSRAlgorithm.XXY ? kvp.Value.XXY : kvp.Value.PPY);
        var updated = StableDatabase.WriteManiaStarRatings(osuDbPath, ratings);
        log.Info($"Stable 写入完成，共更新 {updated} 个谱面（使用 {Algorithm}）");
    }

    private void WriteToLazer(LazerSettings lazer, Dictionary<string, ManiaSRData> data, TaskLogger log)
    {
        if (!lazer.IsAvailable)
        {
            log.Warn("Lazer 不可用，跳过");
            return;
        }

        var ratings = data
            .Where(kvp => kvp.Value is not null)
            .ToDictionary(kvp => kvp.Key, kvp => (Algorithm == ManiaSRAlgorithm.XXY ? kvp.Value.XXY : kvp.Value.PPY).NM);
        var updated = LazerRealm.WriteStarRatings(lazer.ClientRealmPath, ratings);
        log.Info($"Lazer 写入完成，共更新 {updated} 个谱面（使用 {Algorithm} NM）");
    }
}
