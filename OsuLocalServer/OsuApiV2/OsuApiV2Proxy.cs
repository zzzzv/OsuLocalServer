using System.Text.Json;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace OsuLocalServer.OsuApiV2;

public static class OsuApiV2Proxy
{
    private const string RouteId = "apiv2";
    private const string LocalPrefix = "/api/osuapi";
    private const string ClusterId = "osuApiCluster";
    private const string DestinationAddress = "https://osu.ppy.sh/api/";

    private static (RouteConfig[] Routes, ClusterConfig[] Clusters) GetYarpConfig()
    {
        var routes = new[]
        {
            new RouteConfig
            {
                RouteId = RouteId,
                Match = new RouteMatch { Path = LocalPrefix + "/v2/{**catchAll}" },
                ClusterId = ClusterId,
            }.WithTransformPathRemovePrefix(LocalPrefix),
        };

        var clusters = new[]
        {
            new ClusterConfig
            {
                ClusterId = ClusterId,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["osuApi"] = new() { Address = DestinationAddress },
                },
            },
        };

        return (routes, clusters);
    }

    public static IServiceCollection AddServices(IServiceCollection services)
    {
        var (routes, clusters) = GetYarpConfig();
        services.AddReverseProxy()
            .LoadFromMemory(routes, clusters)
            .AddTransforms<BearerTokenTransformProvider>();
        services.AddHttpClient("OsuApiV2").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxConnectionsPerServer = 10,
        });
        return services;
    }

    /// <summary>
    /// YARP transform provider that injects the Bearer token into proxied requests.
    /// </summary>
    public sealed class BearerTokenTransformProvider : ITransformProvider
    {
        private readonly OsuApiV2AuthService _authService;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public BearerTokenTransformProvider(OsuApiV2AuthService authService)
        {
            _authService = authService;
        }

        public void Apply(TransformBuilderContext context)
        {
            context.AddRequestTransform(async transformContext =>
            {
                try
                {
                    var token = await _authService.GetValidAccessTokenAsync(
                        transformContext.HttpContext.RequestAborted);

                    transformContext.ProxyRequest.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
                catch (InvalidOperationException) when (!_authService.IsConfigured)
                {
                    transformContext.HttpContext.Response.StatusCode = 400;
                    await transformContext.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        error = "osu! API v2 credentials not configured. Visit /settings to set up.",
                    }, JsonOptions);
                }
            });
        }

        public void ValidateCluster(TransformClusterValidationContext context) { }
        public void ValidateRoute(TransformRouteValidationContext context) { }
    }
}
