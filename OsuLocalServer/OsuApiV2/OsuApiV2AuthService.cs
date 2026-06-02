using System.Net.Http.Headers;
using System.Text.Json;

using OsuLocalServer.Settings;

namespace OsuLocalServer.OsuApiV2;

public sealed class OsuApiV2AuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SettingService _settings;

    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt;

    private static readonly Uri TokenEndpoint = new("https://osu.ppy.sh/oauth/token");

    public OsuApiV2AuthService(IHttpClientFactory httpClientFactory, SettingService settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
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

        return _cachedToken!;
    }
}
