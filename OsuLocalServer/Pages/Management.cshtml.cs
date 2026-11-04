using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OsuLocalServer.Management;
using OsuLocalServer.Settings;

namespace OsuLocalServer.Pages;

public class ManagementModel : PageModel
{
    private readonly TaskManager _tm;
    private readonly SettingService _settings;

    public ManagementModel(TaskManager tm, SettingService settings)
    {
        _tm = tm;
        _settings = settings;
    }

    public TaskState State => _tm.State;
    public bool IsRunning => _tm.State == TaskState.Running;
    public int LogCount => _tm.LogCount;

    public GenerateManiaSRTask GenerateManiaSR { get; } = new();
    public WriteManiaSRTask WriteManiaSR { get; } = new();
    public CreatePPCollectionTask CreatePPCollection { get; } = new();

    /// <summary>Stable 库里当前存的 SR 算法（上次写入记录）。</summary>
    public string LastWrittenDescription => _settings.Settings.Management.LastWrittenDescription;

    public void OnGet()
    {
        if (Request.Cookies["gen_parallelism"] is string p && int.TryParse(p, out var pc))
            GenerateManiaSR.Parallelism = pc;
        if (Request.Cookies["gen_logErrors"] is string e)
            GenerateManiaSR.LogXxyErrors = e == "true";
        if (Request.Cookies["gen_saveCheckpoint"] is string s)
            GenerateManiaSR.SaveCheckpoint = s == "true";
        if (Request.Cookies["gen_computeXxy"] is string cx)
            GenerateManiaSR.ComputeXxy = cx == "true";
        if (Request.Cookies["gen_computeSpm"] is string cs)
            GenerateManiaSR.ComputeSpm = cs == "true";

        if (Request.Cookies["write_target"] is string t && Enum.TryParse<ManiaSRTarget>(t, out var target))
            WriteManiaSR.Target = target;
        if (Request.Cookies["write_algorithm"] is string a && Enum.TryParse<ManiaSRAlgorithm>(a, out var alg))
            WriteManiaSR.Algorithm = alg;

        if (Request.Cookies["pp_source"] is string src && Enum.TryParse<PPCollectionSource>(src, out var source))
            CreatePPCollection.Source = source;
        if (Request.Cookies["pp_threshold"] is string th && double.TryParse(th, out var threshold))
            CreatePPCollection.Threshold = threshold;
        if (Request.Cookies["pp_minPpy"] is string mp && double.TryParse(mp, out var minPpy))
            CreatePPCollection.MinPPY = minPpy;
    }

    public IActionResult OnPostGenerate(
        int parallelism,
        bool logXxyErrors,
        bool saveCheckpoint,
        bool computeXxy,
        bool computeSpm,
        bool reimportPpy,
        bool resetFailed)
    {
        GenerateManiaSR.Parallelism = Math.Max(1, parallelism);
        GenerateManiaSR.LogXxyErrors = logXxyErrors;
        GenerateManiaSR.SaveCheckpoint = saveCheckpoint;
        GenerateManiaSR.ComputeXxy = computeXxy;
        GenerateManiaSR.ComputeSpm = computeSpm;
        GenerateManiaSR.ReimportPpy = reimportPpy;
        GenerateManiaSR.ResetFailed = resetFailed;

        Response.Cookies.Append("gen_parallelism", GenerateManiaSR.Parallelism.ToString(), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });
        Response.Cookies.Append("gen_logErrors", GenerateManiaSR.LogXxyErrors.ToString().ToLower(), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });
        Response.Cookies.Append("gen_saveCheckpoint", GenerateManiaSR.SaveCheckpoint.ToString().ToLower(), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });
        Response.Cookies.Append("gen_computeXxy", GenerateManiaSR.ComputeXxy.ToString().ToLower(), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });
        Response.Cookies.Append("gen_computeSpm", GenerateManiaSR.ComputeSpm.ToString().ToLower(), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });

        _tm.Start(GenerateManiaSR.Create());
        return RedirectToPage();
    }

    public IActionResult OnPostWriteManiaSR(ManiaSRTarget target, ManiaSRAlgorithm algorithm)
    {
        WriteManiaSR.Target = target;
        WriteManiaSR.Algorithm = algorithm;

        Response.Cookies.Append("write_target", WriteManiaSR.Target.ToString(), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });
        Response.Cookies.Append("write_algorithm", WriteManiaSR.Algorithm.ToString(), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });

        _tm.Start(WriteManiaSR.Create());
        return RedirectToPage();
    }

    public IActionResult OnPostCreatePPCollection(PPCollectionSource source, double threshold, double minPpy)
    {
        CreatePPCollection.Source = source;
        CreatePPCollection.Threshold = Math.Max(0, threshold);
        CreatePPCollection.MinPPY = Math.Max(0, minPpy);

        Response.Cookies.Append("pp_source", CreatePPCollection.Source.ToString(), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });
        Response.Cookies.Append("pp_threshold", CreatePPCollection.Threshold.ToString("0.0"), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });
        Response.Cookies.Append("pp_minPpy", CreatePPCollection.MinPPY.ToString("0.0"), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });

        _tm.Start(CreatePPCollection.Create());
        return RedirectToPage();
    }

    public IActionResult OnPostCancel()
    {
        _tm.Cancel();
        return RedirectToPage();
    }

    public string[] GetAllLogs() => _tm.GetAllLogs();
    public TaskManager.LogEntry[] GetLogEntries() => _tm.GetLogEntries();
}
