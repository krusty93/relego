using System.Net;

namespace Relego.Tests.Api;

public sealed class ProbeEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProbeEndpointTests()
    {
        _factory = new RelegoTestApplicationFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task GetLiveness_WhenDatabaseAccessible_Returns204()
    {
        var response = await _client.GetAsync("/healthz/live");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task GetStartup_WhenDatabaseAccessible_Returns204()
    {
        var response = await _client.GetAsync("/healthz/startup");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

}
