using Microsoft.Extensions.Logging;
using Relego.Cli.Parsing;

namespace Relego.Cli.Sources;

/// <summary>
/// <see cref="IHighlightSource"/> adapter over the existing static
/// <see cref="ClippingsParser"/>. Adds no parsing logic (FR-011); exists so the
/// resolver can treat every source uniformly. Its detection member is added in a
/// later phase.
/// </summary>
public sealed class KindleClippingsSource : IHighlightSource
{
    /// <inheritdoc />
    public SourceDescriptor Descriptor { get; } = new("kindle", "Kindle");

    /// <inheritdoc />
    public Task<ParseResult> ReadAsync(
        string path,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
        => ClippingsParser.ParseAsync(path, logger);
}
