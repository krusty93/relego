namespace Relego.Core.Sources;

/// <summary>
/// Detects the Kobo <c>KoboReader.sqlite</c> database path across macOS, Linux, and Windows.
/// Mirrors <see cref="KindleDetector"/>, probing the same mount roots for the
/// <c>.kobo/KoboReader.sqlite</c> file a connected Kobo device exposes.
/// </summary>
public static class KoboDetector
{
    private const string DatabaseRelativePath = ".kobo/KoboReader.sqlite";

    public static string? DetectDatabasePath()
    {
        if (OperatingSystem.IsMacOS())
            return ProbeMacOS();

        if (OperatingSystem.IsLinux())
            return ProbeLinux();

        if (OperatingSystem.IsWindows())
            return ProbeWindows();

        return null;
    }

    public static string? GetSuggestedDatabasePath()
    {
        var detectedPath = DetectDatabasePath();
        if (!string.IsNullOrWhiteSpace(detectedPath))
        {
            return detectedPath;
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine("/Volumes/KOBOeReader", DatabaseRelativePath);
        }

        if (OperatingSystem.IsLinux())
        {
            return GetLinuxSuggestedPath();
        }

        if (OperatingSystem.IsWindows())
        {
            return GetWindowsSuggestedPath();
        }

        return null;
    }

    private static string? ProbeMacOS()
    {
        var path = Path.Combine("/Volumes/KOBOeReader", DatabaseRelativePath);
        return File.Exists(path) ? path : null;
    }

    private static string? ProbeLinux()
    {
        // Try /media/<user>/KOBOeReader and /run/media/<user>/KOBOeReader
        foreach (var baseDir in new[] { "/media", "/run/media" })
        {
            if (!Directory.Exists(baseDir))
                continue;

            try
            {
                foreach (var userDir in Directory.EnumerateDirectories(baseDir))
                {
                    var path = Path.Combine(userDir, "KOBOeReader", DatabaseRelativePath);
                    if (File.Exists(path))
                        return path;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't read
            }
        }

        return null;
    }

    private static string? ProbeWindows()
    {
        // Check drives D through G
        foreach (var drive in new[] { 'D', 'E', 'F', 'G' })
        {
            var path = Path.Combine($"{drive}:\\", DatabaseRelativePath);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static string GetLinuxSuggestedPath()
    {
        var userName = Environment.UserName;

        foreach (var baseDir in new[] { "/media", "/run/media" })
        {
            var userRoot = Path.Combine(baseDir, userName);
            if (Directory.Exists(userRoot))
            {
                return Path.Combine(userRoot, "KOBOeReader", DatabaseRelativePath);
            }
        }

        return Path.Combine("/media", userName, "KOBOeReader", DatabaseRelativePath);
    }

    private static string GetWindowsSuggestedPath()
    {
        foreach (var drive in new[] { 'D', 'E', 'F', 'G' })
        {
            var driveRoot = $"{drive}:\\";
            if (Directory.Exists(driveRoot))
            {
                return Path.Combine(driveRoot, DatabaseRelativePath);
            }
        }

        return Path.Combine("D:\\", DatabaseRelativePath);
    }
}
