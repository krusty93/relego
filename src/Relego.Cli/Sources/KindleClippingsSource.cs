using Microsoft.Extensions.Logging;
using Relego.Cli.Infrastructure;
using Relego.Cli.Parsing;

namespace Relego.Cli.Sources;

/// <summary>
/// <see cref="IHighlightSource"/> adapter over the existing static
/// <see cref="ClippingsParser"/>. Adds no parsing logic (FR-011); exists so the
/// resolver can treat every source uniformly. Owns the <c>My Clippings.txt</c>
/// detection rules and the <see cref="KindleDetector"/> device probe.
/// </summary>
public sealed class KindleClippingsSource : IHighlightSource
{
    private const string ClippingsFileName = "My Clippings.txt";

    /// <inheritdoc />
    public SourceDescriptor Descriptor { get; } = new("kindle", "Kindle");

    /// <inheritdoc />
    public SourceProbe Locate(string? userPath)
    {
        if (string.IsNullOrWhiteSpace(userPath))
        {
            var detected = KindleDetector.DetectClippingsPath();
            var suggested = KindleDetector.GetSuggestedClippingsPath() ?? ClippingsFileName;
            return new SourceProbe(detected, [suggested]);
        }

        userPath = userPath.Trim();

        // Explicit file named "My Clippings.txt".
        if (IsClippingsFileName(userPath))
        {
            return new SourceProbe(File.Exists(userPath) ? userPath : null, [userPath]);
        }

        // Directory (a mounted device root): try documents/ then the directory itself.
        if (Directory.Exists(userPath))
        {
            var probed = new List<string>();

            var inDocuments = Path.Combine(userPath, "documents", ClippingsFileName);
            probed.Add(inDocuments);
            if (File.Exists(inDocuments))
            {
                return new SourceProbe(inDocuments, probed);
            }

            var direct = Path.Combine(userPath, ClippingsFileName);
            probed.Add(direct);
            if (File.Exists(direct))
            {
                return new SourceProbe(direct, probed);
            }

            return new SourceProbe(null, probed);
        }

        // An explicit path that is neither a "My Clippings.txt" file nor a directory.
        return new SourceProbe(null, [userPath]);
    }

    /// <inheritdoc />
    public Task<ParseResult> ReadAsync(
        string path,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
        => ClippingsParser.ParseAsync(path, logger);

    private static bool IsClippingsFileName(string path)
        => string.Equals(Path.GetFileName(path), ClippingsFileName, StringComparison.OrdinalIgnoreCase);
}
