using System.Net.Http.Headers;
using System.Text.Json;

using OsuLocalServer.Settings;

namespace OsuLocalServer.OsuApiV2;

public sealed class OsuApiV2AuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SettingService _settings;
    private readonly ILogger<OsuApiV2AuthService> _logger;

    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt;

    private static readonly Uri TokenEndpoint = new("https://osu.ppy.sh/oauth/token");

    public OsuApiV2AuthService(IHttpClientFactory httpClientFactory, SettingService settings, ILogger<OsuApiV2AuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;

        // 从持久化恢复 token
        var cred = settings.Settings.ApiV2;
        if (!string.IsNullOrWhiteSpace(cred.AccessToken) && DateTimeOffset.UtcNow < cred.TokenExpiresAt)
        {
            _cachedToken = cred.AccessToken;
            _tokenExpiresAt = cred.TokenExpiresAt;
        }
    }

    public bool HasValidToken =>
        _cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt;

    public bool IsConfigured =>
        _settings?.Settings?.ApiV2 is not null &&
        !string.IsNullOrWhiteSpace(_settings.Settings.ApiV2.ClientId) &&
        !string.IsNullOrWhiteSpace(_settings.Settings.ApiV2.ClientSecret);

    public string? GetClientId() => _settings?.Settings?.ApiV2?.ClientId is { Length: > 0 } cid
        ? cid : null;

    public void ClearToken()
    {
        _cachedToken = null;
        _tokenExpiresAt = DateTimeOffset.MinValue;
        _settings.Settings.ApiV2.AccessToken = null;
        _settings.Settings.ApiV2.TokenExpiresAt = DateTimeOffset.MinValue;
        _settings.Settings.Save();
    }

    public async Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (HasValidToken)
            return _cachedToken!;

        var clientId = _settings.Settings.ApiV2.ClientId;
        var clientSecret = _settings.Settings.ApiV2.ClientSecret;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("Osu API v2 credentials not configured.");

        var client = _httpClientFactory.CreateClient("OsuApiV2");

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "client_credentials",
                ["scope"] = "public",
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
        var root = json!.RootElement;

        _cachedToken = root.GetProperty("access_token").GetString();
        var expiresIn = root.GetProperty("expires_in").GetInt64();
        _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60); // 60s buffer

        _settings.Settings.ApiV2.AccessToken = _cachedToken;
        _settings.Settings.ApiV2.TokenExpiresAt = _tokenExpiresAt;
        _settings.Settings.Save();

        _logger.LogInformation("osu! API v2 access token acquired, expires at {ExpiresAt:O}", _tokenExpiresAt);

        return _cachedToken!;
    }
}
