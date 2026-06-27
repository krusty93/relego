namespace Relego.Tests.Sources.Support;

/// <summary>
/// Resolves committed test fixtures by walking up from the test assembly location
/// until the repository root (the folder containing <c>docs/examples/</c>) is found.
/// </summary>
internal static class TestFixtures
{
    public static string KoboFixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "examples", "kobo-highlights.sqlite");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate docs/examples/kobo-highlights.sqlite by walking up from " + AppContext.BaseDirectory);
    }
}
