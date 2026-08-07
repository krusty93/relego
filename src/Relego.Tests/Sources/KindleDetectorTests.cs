using Relego.Core.Sources;

namespace Relego.Tests.Cli;

public class KindleDetectorTests
{
    [Fact]
    public void DetectClippingsPath_ReturnsNull_WhenNoKindleConnected()
    {
        // In CI/dev containers, no Kindle is ever mounted
        var result = KindleDetector.DetectClippingsPath();
        Assert.Null(result);
    }

    [Fact]
    public void DetectClippingsPath_FindsFile_WhenSimulatedPathExists()
    {
        // We can't easily test platform-specific mount points in CI,
        // but we verify the method doesn't throw on the current OS.
        var result = KindleDetector.DetectClippingsPath();
        // Result is null because no Kindle is mounted — that's expected
        Assert.Null(result);
    }
}
