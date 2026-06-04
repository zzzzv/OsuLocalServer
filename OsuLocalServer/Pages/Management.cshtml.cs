using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OsuLocalServer.Management;

namespace OsuLocalServer.Pages;

public class ManagementModel : PageModel
{
    private readonly TaskManager _tm;

    public ManagementModel(TaskManager tm) => _tm = tm;

    public TaskState State => _tm.State;
    public bool IsRunning => _tm.State == TaskState.Running;
    public int LogCount => _tm.LogCount;

    public GenerateManiaSRTask GenerateManiaSR { get; } = new();
    public WriteManiaSRTask WriteManiaSR { get; } = new();

    public void OnGet()
    {
        if (Request.Cookies["gen_parallelism"] is string p && int.TryParse(p, out var pc))
            GenerateManiaSR.Parallelism = pc;
        if (Request.Cookies["gen_logErrors"] is string e)
            GenerateManiaSR.LogXxyErrors = e == "true";
        if (Request.Cookies["gen_saveCheckpoint"] is string s)
            GenerateManiaSR.SaveCheckpoint = s == "true";

        if (Request.Cookies["write_target"] is string t && Enum.TryParse<ManiaSRTarget>(t, out var target))
            WriteManiaSR.Target = target;
        if (Request.Cookies["write_algorithm"] is string a && Enum.TryParse<ManiaSRAlgorithm>(a, out var alg))
            WriteManiaSR.Algorithm = alg;
    }

    public IActionResult OnPostGenerate(int parallelism, bool logXxyErrors, bool saveCheckpoint)
    {
        GenerateManiaSR.Parallelism = Math.Max(1, parallelism);
        GenerateManiaSR.LogXxyErrors = logXxyErrors;
        GenerateManiaSR.SaveCheckpoint = saveCheckpoint;

        Response.Cookies.Append("gen_parallelism", GenerateManiaSR.Parallelism.ToString(), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });
        Response.Cookies.Append("gen_logErrors", GenerateManiaSR.LogXxyErrors.ToString().ToLower(), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });
        Response.Cookies.Append("gen_saveCheckpoint", GenerateManiaSR.SaveCheckpoint.ToString().ToLower(), new CookieOptions { MaxAge = TimeSpan.FromDays(365) });

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

    public IActionResult OnPostCancel()
    {
        _tm.Cancel();
        return RedirectToPage();
    }

    public string[] GetAllLogs() => _tm.GetAllLogs();
    public TaskManager.LogEntry[] GetLogEntries() => _tm.GetLogEntries();
}
