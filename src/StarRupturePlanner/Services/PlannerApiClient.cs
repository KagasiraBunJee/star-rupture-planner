using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using StarRupturePlanner.Models;

namespace StarRupturePlanner.Services;

public sealed class PlannerApiClient : IPlannerApiClient
{
    public const int DefaultPort = 8010;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _client;
    private Uri _baseUri;

    public Uri BaseUri => _baseUri;

    public string PlannerLanguage { get; set; } = PlannerLanguages.English;

    public PlannerApiClient(int port = DefaultPort)
    {
        _baseUri = BuildBaseUri(port);
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public void ConfigurePort(int port)
    {
        _baseUri = BuildBaseUri(AppSettings.NormalizeApiPort(port));
    }

    public async Task<bool> IsApiAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.GetAsync(new Uri(BaseUri, "api/meta"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlannerApiClient] API availability check failed: {ex.Message}");
            return false;
        }
    }

    public async Task<PlannerCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        return await GetJsonAsync<PlannerCatalog>($"api/planner/catalog?lang={Uri.EscapeDataString(CurrentLanguage())}", cancellationToken)
            ?? new PlannerCatalog();
    }

    public async Task<SuggestionResponse> GetSuggestionsAsync(
        string direction,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/planner/suggestions?direction={Uri.EscapeDataString(direction)}&item_id={Uri.EscapeDataString(itemId)}&lang={Uri.EscapeDataString(CurrentLanguage())}";
        return await GetJsonAsync<SuggestionResponse>(url, cancellationToken)
            ?? new SuggestionResponse();
    }

    private string CurrentLanguage() => PlannerLanguages.Normalize(PlannerLanguage);

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var stream = await _client.GetStreamAsync(new Uri(BaseUri, url), cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    public string ToAbsoluteAssetUrl(string? assetUrl)
    {
        if (string.IsNullOrWhiteSpace(assetUrl))
        {
            return "";
        }

        if (Uri.TryCreate(assetUrl, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return new Uri(BaseUri, assetUrl.TrimStart('/')).ToString();
    }

    private static Uri BuildBaseUri(int port) => new($"http://127.0.0.1:{AppSettings.NormalizeApiPort(port)}/");
}
