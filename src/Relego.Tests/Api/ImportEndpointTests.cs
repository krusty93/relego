using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Relego.Core.Contracts;
using Relego.Tests.Sources.Support;

namespace Relego.Tests.Api;

public sealed class ImportEndpointTests : IDisposable
{
    private readonly RelegoTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public ImportEndpointTests()
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
    public async Task PostImports_KindleClippings_ParsesAndStores()
    {
        var response = await UploadAsync(
            File.ReadAllBytes(TestFixtures.KindleFixturePath()),
            "My Clippings.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ImportResponse>();
        Assert.NotNull(result);
        Assert.Equal("kindle", result.Source);
        Assert.Equal("My Clippings.txt", result.FileName);
        Assert.True(result.BooksParsed > 0);
        Assert.True(result.HighlightsParsed > 0);
        Assert.Equal(result.HighlightsParsed, result.NewHighlights);
        Assert.Equal(result.BooksParsed, result.NewBooks);
    }

    [Fact]
    public async Task PostImports_KoboDatabase_IsDetectedByContentNotFileName()
    {
        // The file name is deliberately wrong: format detection reads the SQLite header.
        var response = await UploadAsync(
            File.ReadAllBytes(TestFixtures.KoboFixturePath()),
            "renamed-export.dat");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ImportResponse>();
        Assert.NotNull(result);
        Assert.Equal("kobo", result.Source);
        Assert.True(result.HighlightsParsed > 0);
    }

    [Fact]
    public async Task PostImports_SameFileTwice_ReportsDuplicatesOnSecondRun()
    {
        var bytes = File.ReadAllBytes(TestFixtures.KindleFixturePath());

        var first = await (await UploadAsync(bytes, "My Clippings.txt")).Content.ReadFromJsonAsync<ImportResponse>();
        var second = await (await UploadAsync(bytes, "My Clippings.txt")).Content.ReadFromJsonAsync<ImportResponse>();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(0, second.NewHighlights);
        Assert.Equal(first.NewHighlights, second.DuplicateHighlights);
        Assert.Equal(0, second.NewBooks);
    }

    [Fact]
    public async Task PostImports_BinaryFileThatIsNotSqlite_Returns422()
    {
        var response = await UploadAsync([0x00, 0x01, 0x02, 0x03, 0x00], "photo.png");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("My Clippings.txt", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostImports_EmptyFile_Returns422()
    {
        var response = await UploadAsync([], "My Clippings.txt");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostImports_TextWithNoHighlights_Returns200WithZeroCounts()
    {
        var response = await UploadAsync(
            Encoding.UTF8.GetBytes("this file has no clipping separators at all"),
            "notes.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ImportResponse>();
        Assert.NotNull(result);
        Assert.Equal(0, result.BooksParsed);
        Assert.Equal(0, result.NewHighlights);
    }

    [Fact]
    public async Task PostImports_NoFileAttached_Returns422()
    {
        using var content = new MultipartFormDataContent();
        var response = await _client.PostAsync("/imports", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private async Task<HttpResponseMessage> UploadAsync(byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", fileName);

        return await _client.PostAsync("/imports", content);
    }
}

