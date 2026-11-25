using Relego.Server.Telemetry;

namespace Relego.Tests.Telemetry;

/// <summary>
/// Covers the identifier that makes "how many installs" countable without tracking people: it is
/// random, it is created once, and it lives on the data volume so deleting the volume resets it.
/// </summary>
public sealed class InstallationIdStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"relego-telemetry-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreatesAnIdentifierOnFirstUse()
    {
        var id = await new InstallationIdStore(_directory).GetOrCreateAsync();

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.True(File.Exists(Path.Combine(_directory, "install-id")));
    }

    [Fact]
    public async Task KeepsTheSameIdentifierAcrossCallsAndRestarts()
    {
        var first = await new InstallationIdStore(_directory).GetOrCreateAsync();
        var second = await new InstallationIdStore(_directory).GetOrCreateAsync();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task DifferentVolumesGetDifferentIdentifiers()
    {
        var other = Path.Combine(_directory, "other");

        var first = await new InstallationIdStore(_directory).GetOrCreateAsync();
        var second = await new InstallationIdStore(other).GetOrCreateAsync();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task AnEmptyFileIsTreatedAsMissing()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "install-id"), "   ");

        var id = await new InstallationIdStore(_directory).GetOrCreateAsync();

        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    /// <summary>Cleans up the temporary volume the tests pretend to be.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
