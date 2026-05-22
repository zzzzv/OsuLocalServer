using System.Text.Json;
using System.Text.Json.Nodes;

try
{
    var builder = WebApplication.CreateBuilder(args);

    var configFileName = "OsuLocalServer.Lazer.json";
    var configPath = Path.Combine(builder.Environment.ContentRootPath, configFileName);
    if (!File.Exists(configPath))
    {
        var defaults = new JsonObject
        {
            ["Urls"] = "http://localhost:5048",
            ["LazerPaths"] = new JsonObject
            {
                ["LazerCurrentDirectory"] = null,
                ["DataDirectory"] = null,
                ["TempDirectory"] = null,
            },
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

    builder.Services.AddSingleton<LazerScoreQueryService>();

    var app = builder.Build();

    app.UseCors();

    // Realm assemblies are only needed at query time, by when config is ready
    var lazerDirectory = ServerConfig.GetLazerCurrentDirectory(app.Configuration);
    OsuLazerAssemblyResolver.Register(lazerDirectory);

    var appVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    logger.LogInformation("OsuLocalServer.Lazer v{Version}", appVersion);
    logger.LogInformation("Registered lazer assembly directory: {LazerDirectory}", lazerDirectory);

    var dataDirectory = ServerConfig.GetDataDirectory(app.Configuration);

    app.MapGet("/", () => "OsuLazerServer");

    app.MapGet("/api/status", () => Results.Ok(new { available = true, dataDirectory }));

    app.MapGet("/api/scores", (string rql, int? depth, LazerScoreQueryService queryService) =>
        RunQuery(rql, () => queryService.QueryScores(rql, depth ?? 0)));

    app.MapGet("/api/beatmaps", (string rql, int? depth, LazerScoreQueryService queryService) =>
        RunQuery(rql, () => queryService.QueryBeatmaps(rql, depth ?? 0)));

    app.MapGet("/api/beatmapsets", (string rql, int? depth, LazerScoreQueryService queryService) =>
        RunQuery(rql, () => queryService.QueryBeatmapSets(rql, depth ?? 0)));

    app.MapGet("/api/collections", (string rql, int? depth, LazerScoreQueryService queryService) =>
        RunQuery(rql, () => queryService.QueryCollections(rql, depth ?? 0)));

    app.MapGet("/files/{hash}", (string hash) =>
    {
        hash = hash.Trim('"');

        if (string.IsNullOrWhiteSpace(hash) || hash.Length < 3)
            return Results.BadRequest("Invalid file hash.");

        var filePath = Path.Combine(dataDirectory, "files", hash[..1], hash[..2], hash);
        if (!File.Exists(filePath))
            return Results.NotFound($"File not found: {filePath}");

        return Results.File(filePath, contentType: "application/octet-stream");
    });

    app.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Console.Error.WriteLine("Press any key to exit...");
    Console.ReadKey(true);
}

static IResult RunQuery(string rql, Func<List<object>> queryFunc)
{
    try
    {
        var items = queryFunc();
        return Results.Ok(new { count = items.Count, items });
    }
    catch (FileNotFoundException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
}
