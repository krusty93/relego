using Relego.Cli.Infrastructure;

namespace Relego.Tests.Cli;

public sealed class ServerUrlValidationTests
{
    [Fact]
    public void GetConfiguredOrDefault_MissingValue_ReturnsLocalhostDefault()
    {
        var value = ServerUrlValidator.GetConfiguredOrDefault(null);

        Assert.Equal(ServerUrlValidator.DefaultServerUrl, value);
    }

    [Fact]
    public void GetConfiguredOrDefault_EnvironmentValue_ReturnsEnvironmentOverride()
    {
        var value = ServerUrlValidator.GetConfiguredOrDefault("http://localhost:8080", "  https://relego.example.com/api  ");

        Assert.Equal("https://relego.example.com/api", value);
    }

    [Fact]
    public void Resolve_MissingValue_ReturnsValidDefaultUri()
    {
        var result = ServerUrlValidator.Resolve(null, out var uri, out var resolvedValue);

        Assert.Equal(ServerUrlValidator.ValidationResult.Valid, result);
        Assert.NotNull(uri);
        Assert.Equal(ServerUrlValidator.DefaultServerUrl, resolvedValue);
        Assert.Equal(ServerUrlValidator.DefaultServerUrl, uri.ToString().TrimEnd('/'));
    }

    [Fact]
    public void Resolve_EnvironmentOverride_WinsOverConfiguredValue()
    {
        var result = ServerUrlValidator.Resolve("http://localhost:8080", "  https://relego.example.com/base/  ", out var uri, out var resolvedValue);

        Assert.Equal(ServerUrlValidator.ValidationResult.Valid, result);
        Assert.NotNull(uri);
        Assert.Equal("https://relego.example.com/base/", resolvedValue);
        Assert.Equal("https://relego.example.com/base", uri.ToString().TrimEnd('/'));
    }

    [Fact]
    public void Resolve_ConfiguredValue_TrimsAndPreservesUrl()
    {
        var result = ServerUrlValidator.Resolve("  https://relego.example.com/base/  ", out var uri, out var resolvedValue);

        Assert.Equal(ServerUrlValidator.ValidationResult.Valid, result);
        Assert.NotNull(uri);
        Assert.Equal("https://relego.example.com/base/", resolvedValue);
        Assert.Equal("https://relego.example.com/base", uri.ToString().TrimEnd('/'));
    }

    [Fact]
    public void Validate_NullValue_ReturnsMissing()
    {
        var result = ServerUrlValidator.Validate(null, out var uri);

        Assert.Equal(ServerUrlValidator.ValidationResult.Missing, result);
        Assert.Null(uri);
    }

    [Fact]
    public void Validate_EmptyString_ReturnsMissing()
    {
        var result = ServerUrlValidator.Validate(string.Empty, out var uri);

        Assert.Equal(ServerUrlValidator.ValidationResult.Missing, result);
        Assert.Null(uri);
    }

    [Fact]
    public void Validate_WhitespaceOnly_ReturnsMissing()
    {
        var result = ServerUrlValidator.Validate("   ", out var uri);

        Assert.Equal(ServerUrlValidator.ValidationResult.Missing, result);
        Assert.Null(uri);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("//missing-scheme")]
    [InlineData("file:///local/path")]
    public void Validate_MalformedOrNonHttpUrl_ReturnsMalformed(string value)
    {
        var result = ServerUrlValidator.Validate(value, out var uri);

        Assert.Equal(ServerUrlValidator.ValidationResult.Malformed, result);
        Assert.Null(uri);
    }

    [Theory]
    [InlineData("http://192.168.1.10:8080")]
    [InlineData("http://localhost:5000")]
    [InlineData("https://relego.example.com")]
    [InlineData("http://relego.example.com/prefix")]
    public void Validate_ValidHttpUrl_ReturnsValidWithUri(string value)
    {
        var result = ServerUrlValidator.Validate(value, out var uri);

        Assert.Equal(ServerUrlValidator.ValidationResult.Valid, result);
        Assert.NotNull(uri);
        Assert.Equal(value, uri.ToString().TrimEnd('/'));
    }
}
