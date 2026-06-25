using OsuLocalServer;
using OsuLocalServer.Settings;
using OsuLocalServer.OsuApiV2;
using OsuLocalServer.Lazer;
using OsuLocalServer.Management;
using OsuLocalServer.Stable;
using OsuLocalServer.Tools;

try
{
    var appSettings = AppSettings.Load();

    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Warning);
    builder.WebHost.UseUrls(appSettings.Urls);

    var allowedIPs = new[] { "localhost", "127.0.0.1", "::1" };

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.SetIsOriginAllowed(origin =>
            {
                if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    return allowedIPs.Contains(uri.Host);
                return false;
            })
            .AllowAnyHeader()
            .AllowAnyMethod();
        });
    });

    builder.Services.AddRazorPages();
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<SettingService>();

    builder.Services.AddSingleton<OsuApiV2AuthService>();
    builder.Services.AddSingleton<TaskManager>();
    builder.Services.AddSingleton<VersionCheckService>();
    builder.Services.AddSingleton<ManiaLabService>();

    OsuApiV2Proxy.AddServices(builder.Services);

    var app = builder.Build();
    app.UseCors();

    // CLI 参数：强制禁用模块，用于模拟无 stable/lazer 的环境
    if (args.Contains("--no-lazer")) appSettings.Lazer.Disable = true;
    if (args.Contains("--no-stable")) appSettings.Stable.Disable = true;

    var settingService = app.Services.GetRequiredService<SettingService>();
    settingService.Settings = appSettings;

    app.MapRazorPages();
    app.MapHub<ServerHub>(ServerHub.Path);

    app.MapLazerRoutes();
    app.MapStableRoutes();
    app.MapToolsRoutes();

    var apiV2Auth = app.Services.GetRequiredService<OsuApiV2AuthService>();
    app.MapSettingsRoutes(settingService, apiV2Auth);

    app.MapReverseProxy();
    app.MapManagementRoutes();

    // mania-lab 静态文件服务（终端中间件，不传递到后续管道）
    app.Map("/mania-lab", maniaLabApp =>
    {
        maniaLabApp.Run(async ctx =>
        {
            var ss = ctx.RequestServices.GetRequiredService<SettingService>();
            var maniaLab = ss.Settings.ManiaLab;
            if (!maniaLab.IsAvailable || maniaLab.CurrentDir is null)
            {
                ctx.Response.StatusCode = 404;
                return;
            }

            var subPath = ctx.Request.Path.Value ?? "/";
            if (subPath.StartsWith("/mania-lab"))
                subPath = subPath["/mania-lab".Length..];
            if (string.IsNullOrEmpty(subPath))
                subPath = "/";

            var filePath = Path.Combine(maniaLab.CurrentDir, subPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(filePath))
            {
                var contentType = Utils.GetContentType(filePath);
                ctx.Response.ContentType = contentType;
                await ctx.Response.SendFileAsync(filePath);
            }
            else
            {
                // SPA fallback
                var indexPath = Path.Combine(maniaLab.CurrentDir, "index.html");
                if (File.Exists(indexPath))
                {
                    ctx.Response.ContentType = "text/html";
                    await ctx.Response.SendFileAsync(indexPath);
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                }
            }
        });
    });

    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    startupLogger.LogInformation("OsuLocalServer v{Version}", Utils.AppVersion);

    if (appSettings.OpenBrowserOnStartup)
    {
        var settingsUrl = appSettings.Urls.TrimEnd('/');
        Utils.OpenBrowser(settingsUrl);
    }

    app.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
#if !DEBUG
    Console.Error.WriteLine("Press Enter to exit...");
    Console.ReadLine();
#endif
}
