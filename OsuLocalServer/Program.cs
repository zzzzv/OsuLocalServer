using OsuLocalServer;
using OsuLocalServer.Settings;
using OsuLocalServer.OsuApiV2;
using OsuLocalServer.Lazer;
using OsuLocalServer.Stable;

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
    builder.Services.AddSingleton<SettingService>();

    builder.Services.AddSingleton<OsuApiV2AuthService>();

    OsuApiV2Proxy.AddServices(builder.Services);

    var app = builder.Build();
    app.UseCors();

    var settingService = app.Services.GetRequiredService<SettingService>();
    settingService.Settings = appSettings;

    app.MapRazorPages();

    app.MapLazerRoutes();
    app.MapStableRoutes();

    var apiV2Auth = app.Services.GetRequiredService<OsuApiV2AuthService>();
    app.MapSettingsRoutes(settingService, apiV2Auth);

    app.MapReverseProxy();

    var appVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    startupLogger.LogInformation("OsuLocalServer v{Version}", appVersion);

    app.MapGet("/", () => "OsuLocalServer");

    if (appSettings.OpenSettingsOnStartup)
    {
        var settingsUrl = $"{appSettings.Urls.TrimEnd('/')}/settings";
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
