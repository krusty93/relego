using System.Net.Http.Json;
using Relego.Core.Contracts;

namespace Relego.Cli.Infrastructure;

/// <summary>
/// Typed HTTP client for all Relego server API calls.
/// Uses source-generated JSON context for trimming compatibility.
/// </summary>
public sealed class RelegoHttpClient(HttpClient http)
{
    public async Task<HighlightsResponse> GetHighlightsAsync(int page, int pageSize, string? query, CancellationToken ct = default)
    {
        var requestUri = $"/highlights?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(query))
        {
            requestUri += $"&q={Uri.EscapeDataString(query)}";
        }

        return (await http.GetFromJsonAsync(requestUri, RelegoJsonContext.Default.HighlightsResponse, ct).ConfigureAwait(false))!;
    }

    public async Task<SyncResponse> PostSyncAsync(SyncRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/highlights/import", request, RelegoJsonContext.Default.SyncRequest, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(RelegoJsonContext.Default.SyncResponse, ct).ConfigureAwait(false))!;
    }

    public async Task<SettingsResponse> GetSettingsAsync(CancellationToken ct = default)
        => (await http.GetFromJsonAsync("/settings", RelegoJsonContext.Default.SettingsResponse, ct).ConfigureAwait(false))!;

    public async Task<SettingsResponse> PatchSettingsAsync(UpdateSettingsRequest request, CancellationToken ct = default)
    {
        var response = await http.PatchAsJsonAsync("/settings", request, RelegoJsonContext.Default.UpdateSettingsRequest, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(RelegoJsonContext.Default.SettingsResponse, ct).ConfigureAwait(false))!;
    }

    public async Task<StatusResponse> GetStatusAsync(CancellationToken ct = default)
        => (await http.GetFromJsonAsync("/status", RelegoJsonContext.Default.StatusResponse, ct).ConfigureAwait(false))!;

    public async Task<HttpResponseMessage> PostExcludeAsync(string type, int id, CancellationToken ct = default)
    {
        var response = await http.PostAsync($"/{type}s/{id}/exclusions", null, ct).ConfigureAwait(false);
        return response;
    }

    public async Task<HttpResponseMessage> DeleteExcludeAsync(string type, int id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"/{type}s/{id}/exclusions", ct).ConfigureAwait(false);
        return response;
    }

    public async Task<ExclusionsResponse> GetExclusionsAsync(CancellationToken ct = default)
        => (await http.GetFromJsonAsync("/exclusions", RelegoJsonContext.Default.ExclusionsResponse, ct).ConfigureAwait(false))!;

    public Task<HttpResponseMessage> PostTestKindleEmailAsync(CancellationToken ct = default)
        => http.PostAsync("/settings/test-kindle-email", null, ct);

    public Task<HttpResponseMessage> PostTestRecapEmailAsync(CancellationToken ct = default)
        => http.PostAsync("/settings/test-recap-email", null, ct);

    public async Task<RecapTriggerResponse> TriggerRecapAsync(CancellationToken ct = default)
    {
        var response = await http.PostAsync("/recaps", null, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync(RelegoJsonContext.Default.RecapTriggerResponse, ct).ConfigureAwait(false))!;
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("/healthz/live", ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<HttpResponseMessage> PutWeightAsync(int highlightId, SetWeightRequest request, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/highlights/{highlightId}/weight", request, RelegoJsonContext.Default.SetWeightRequest, ct).ConfigureAwait(false);
        return response;
    }

    public Task<HttpResponseMessage> DeleteHighlightAsync(int id, CancellationToken ct = default)
        => http.DeleteAsync($"/highlights/{id}", ct);

    public Task<HttpResponseMessage> RenameBookAsync(int bookId, RenameBookRequest request, CancellationToken ct = default)
        => http.PutAsJsonAsync($"/books/{bookId}/title", request, RelegoJsonContext.Default.RenameBookRequest, ct);

    public async Task<List<WeightedHighlightDto>> GetWeightsAsync(CancellationToken ct = default)
        => (await http.GetFromJsonAsync("/highlights/weights", RelegoJsonContext.Default.ListWeightedHighlightDto, ct).ConfigureAwait(false))!;
}
