using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Relego.Tests.Api;

public sealed class WebUiHostingTests : IDisposable
{
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), $"relego-web-{Guid.NewGuid():N}");
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public WebUiHostingTests()
    {
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>Relego</title><div id=\"root\"></div>");
        File.WriteAllText(Path.Combine(_webRoot, "asset.js"), "console.log('relego');");

        _factory = new RelegoTestApplicationFactory(builder => builder.UseWebRoot(_webRoot));
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetRoot_ServesWebUiIndex()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("id=\"root\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetClientRoute_FallsBackToWebUiIndex()
    {
        var response = await _client.GetAsync("/app/books/fiction");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("id=\"root\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetStatus_RemainsApiResponse()
    {
        var response = await _client.GetAsync("/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }
}
