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

    OsuApiV2Proxy.AddServices(builder.Services);

    var app = builder.Build();
    app.UseCors();

    // CLI 参数：强制禁用模块，用于模拟无 stable/lazer 的环境
    if (args.Contains("--no-lazer")) appSettings.Lazer.Disable = true;
    if (args.Contains("--no-stable")) appSettings.Stable.Disable = true;

    var settingService = app.Services.GetRequiredService<SettingService>();
    settingService.Settings = appSettings;

    app.MapRazorPages();
    app.MapHub<ManagementHub>("/ws/management");

    app.MapLazerRoutes();
    app.MapStableRoutes();
    app.MapToolsRoutes();

    var apiV2Auth = app.Services.GetRequiredService<OsuApiV2AuthService>();
    app.MapSettingsRoutes(settingService, apiV2Auth);

    app.MapReverseProxy();
    app.MapManagementRoutes();

    var appVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    startupLogger.LogInformation("OsuLocalServer v{Version}", appVersion);

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
