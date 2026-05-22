using System.Text.Json;
using System.Text.Json.Nodes;

try
{
    var builder = WebApplication.CreateBuilder(args);

    var configFileName = "OsuLocalServer.Stable.json";
    var configPath = Path.Combine(builder.Environment.ContentRootPath, configFileName);
    if (!File.Exists(configPath))
    {
        var defaults = new JsonObject
        {
            ["Urls"] = "http://localhost:5167",
            ["AppSettings"] = new JsonObject { ["OsuRootPath"] = null },
        };
        File.WriteAllText(configPath, defaults.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Created default config: {configPath}");
    }

    builder.Configuration.AddJsonFile(configFileName, optional: true, reloadOnChange: true);
    builder.WebHost.UseUrls(ServerConfig.GetUrls(builder.Configuration));

    var allowedIPs = new[] { "localhost", "127.0.0.1", "::1" };

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.SetIsOriginAllowed(origin =>
            {
                if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return allowedIPs.Contains(uri.Host);
                }
                return false;
            })
            .AllowAnyHeader()
            .AllowAnyMethod();
        });
    });

    var app = builder.Build();

    // Resolve osu! path at startup; exit if not found
    var osuRootPath = ServerConfig.ResolveOsuRootPath(builder.Configuration);

    var appVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogInformation("OsuLocalServer.Stable v{Version}", appVersion);
    logger.LogInformation("Loaded osu! directory: {OsuRootPath}", osuRootPath);

    app.MapGet("/", () => "OsuStableServer");

    app.MapGet("/api/status", () => Results.Ok(new { available = true, osuRootPath }));

    app.MapGet("/files/{**relativePath}", (string? relativePath) =>
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return Results.BadRequest("Relative file path is required.");

        var filePath = Utils.ResolveFilePath(osuRootPath, relativePath);
        if (filePath is null)
            return Results.BadRequest("Invalid file path.");

        if (!File.Exists(filePath))
            return Results.NotFound();

        return Results.File(filePath, Utils.GetContentType(filePath), enableRangeProcessing: true);
    });

    app.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Console.Error.WriteLine("Press any key to exit...");
    Console.ReadKey(true);
}

