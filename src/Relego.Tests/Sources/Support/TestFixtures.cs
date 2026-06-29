namespace Relego.Tests.Sources.Support;

/// <summary>
/// Resolves committed test fixtures from the <c>Fixtures/</c> folder inside the test
/// project by walking up from the test assembly location. The folder lives under
/// <c>src/Relego.Tests/</c>, so it is present both locally and in the CI Docker image
/// (which copies the whole <c>src/</c> tree) — no copy-to-output or repo-root layout required.
/// </summary>
internal static class TestFixtures
{
    public static string KoboFixturePath() => Resolve("kobo-highlights.sqlite");

    public static string KindleFixturePath() => Resolve("kindle-highlights.txt");

    private static string Resolve(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Fixtures", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate Fixtures/{fileName} by walking up from {AppContext.BaseDirectory}");
    }
}
