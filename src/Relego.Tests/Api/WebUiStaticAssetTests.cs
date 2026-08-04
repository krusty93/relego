using System.Net;

namespace Relego.Tests.Api;

public sealed class WebUiStaticAssetTests : IDisposable
{
    private readonly string _webRootPath;
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public WebUiStaticAssetTests()
    {
        _webRootPath = Path.Combine(Path.GetTempPath(), $"relego-web-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_webRootPath, "assets"));
        File.WriteAllText(Path.Combine(_webRootPath, "index.html"), "<!doctype html><title>Relego</title>");
        File.WriteAllText(Path.Combine(_webRootPath, "assets", "app.js"), "window.relego = true;");

        _factory = new RelegoTestApplicationFactory(webRootPath: _webRootPath);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Directory.Delete(_webRootPath, recursive: true);
    }

    [Fact]
    public async Task GetRoot_ServesTheWebUi()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Relego", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetClientRoute_FallsBackToTheWebUi()
    {
        var response = await _client.GetAsync("/import");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Relego", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetStaticAsset_ServesTheRequestedFile()
    {
        var response = await _client.GetAsync("/assets/app.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("window.relego", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetApiRoute_IsHandledByTheApiInsteadOfTheSpaFallback()
    {
        var response = await _client.GetAsync("/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
