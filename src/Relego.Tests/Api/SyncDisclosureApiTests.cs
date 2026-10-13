using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Relego.Core.Contracts;
using Relego.Server.Sync;

namespace Relego.Tests.Api;

/// <summary>
/// Covers the coverage disclosure and prerequisites served with the sync status. The invariant under
/// test is that both are visible in every connection state, including before anything is connected:
/// the user has to be able to read what the feature does and does not reach before granting it, so
/// hiding them behind a connection would invert the point of showing them. The disclosure version the
/// server served is also recorded on the connection, so a later change can be told apart from the
/// text the user actually agreed to. The local import path is covered by
/// <see cref="ImportEndpointTests"/> and is expected to stay green: it is the fallback the disclosure
/// points at.
/// </summary>
public sealed class SyncDisclosureApiTests
{
    private const string Provider = KindleCloudProvider.ProviderId;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetStatus_NotConnected_StillServesTheDisclosureAndPrerequisites()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (status, body) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(body);
        Assert.Equal("not_connected", body!.Status);
        AssertDisclosureIsComplete(body);
    }

    [Fact]
    public async Task GetStatus_AwaitingPairing_KeepsServingThem()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (created, _) = await SendAsync<CreateSyncConnectionResponse>(client, HttpMethod.Post, $"/sync/{Provider}/connection");
        Assert.Equal(HttpStatusCode.Created, created);

        var (_, body) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.NotNull(body);
        Assert.Equal("awaiting_pairing", body!.Status);
        AssertDisclosureIsComplete(body);
    }

    [Fact]
    public async Task GetStatus_ActiveConnection_KeepsServingThemUnchanged()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();
        var token = await ConnectAsync(client);

        var (paired, _) = await SendAsync<SyncPairResponse>(
            client,
            HttpMethod.Post,
            $"/sync/{Provider}/pair",
            new SyncPairRequest { ProfileVersion = KindleCloudProvider.ProfileVersion, RegionId = "us" },
            token);
        Assert.Equal(HttpStatusCode.OK, paired);

        var (_, body) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.NotNull(body);
        // The stored state is "active"; the user-visible status derived from it is "connected".
        Assert.Equal("active", body!.Connection.State);
        Assert.Equal("connected", body.Status);
        AssertDisclosureIsComplete(body);
    }

    [Fact]
    public async Task PostConnection_RecordsTheDisclosureVersionTheUserWasShown()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        await ConnectAsync(client);

        var (_, body) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");
        Assert.NotNull(body);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var stored = await db.QuerySingleAsync<string>(
            "SELECT disclosure_version FROM sync_connections WHERE provider_id = @ProviderId",
            new { ProviderId = Provider });

        // The recorded version is the one served, not a constant the endpoint happens to hold: a
        // later disclosure change has to be able to tell the two apart.
        Assert.Equal(body!.Disclosure.Version, stored);
    }

    [Fact]
    public async Task GetStatus_EveryDisclosureItemCarriesAKindTitleAndBody()
    {
        using var factory = new RelegoTestApplicationFactory();
        using var client = factory.CreateClient();

        var (_, body) = await SendAsync<SyncStatusResponse>(client, HttpMethod.Get, $"/sync/{Provider}");

        Assert.NotNull(body);
        Assert.NotEmpty(body!.Disclosure.Items);

        foreach (var item in body.Disclosure.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Kind));
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Body));
        }

        // The disclosure exists to name the limits, so those have to be among the items.
        Assert.Contains(body.Disclosure.Items, item => item.Kind == "Prerequisite");
        Assert.Contains(body.Disclosure.Items, item => item.Kind == "Privacy");
    }

    private static void AssertDisclosureIsComplete(SyncStatusResponse body)
    {
        var provider = new KindleCloudProvider();

        Assert.Equal(provider.Disclosure.Version, body.Disclosure.Version);
        Assert.Equal(
            provider.Disclosure.Items.Select(item => item.Kind),
            body.Disclosure.Items.Select(item => item.Kind));
        Assert.Equal(provider.Prerequisites, body.Prerequisites);
        Assert.NotEmpty(body.Prerequisites);
    }

    private static async Task<string> ConnectAsync(HttpClient client)
    {
        var (status, body) = await SendAsync<CreateSyncConnectionResponse>(client, HttpMethod.Post, $"/sync/{Provider}/connection");
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.NotNull(body);

        return body!.Token;
    }

    private static async Task<(HttpStatusCode Status, T? Body)> SendAsync<T>(
        HttpClient client,
        HttpMethod method,
        string url,
        object? payload = null,
        string? token = null)
    {
        using var request = new HttpRequestMessage(method, url);

        if (payload is not null)
            request.Content = JsonContent.Create(payload, options: Json);

        if (token is not null)
            request.Headers.Add("Authorization", $"Bearer {token}");

        using var response = await client.SendAsync(request);

        if (typeof(T) == typeof(object) || response.Content.Headers.ContentLength is null or 0)
            return (response.StatusCode, default);

        var body = await response.Content.ReadFromJsonAsync<T>(Json);
        return (response.StatusCode, body);
    }
}
