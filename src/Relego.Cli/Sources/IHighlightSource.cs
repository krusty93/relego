using Microsoft.Extensions.Logging;
using Relego.Cli.Parsing;

namespace Relego.Cli.Sources;

/// <summary>
/// The common, self-describing abstraction through which every highlight source
/// (Kindle clippings, Kobo database, …) feeds the import pipeline. Each source
/// owns its identity via <see cref="Descriptor"/> and returns the existing
/// <see cref="ParseResult"/> so downstream components stay source-agnostic.
/// </summary>
/// <remarks>
/// A new source is added by implementing this interface and registering it once
/// in DI — no edits to the resolver, workflow, command, or any enum (FR-011).
/// The detection member (<c>Locate</c>) is introduced in a later phase.
/// </remarks>
public interface IHighlightSource
{
    /// <summary>
    /// This source's identity — used only as a label for reporting/logging,
    /// never branched on.
    /// </summary>
    SourceDescriptor Descriptor { get; }

    /// <summary>
    /// Reads the source at <paramref name="path"/> and returns the normalized result.
    /// </summary>
    /// <param name="path">Concrete file this source can read.</param>
    /// <param name="logger">Optional logger for skip-and-warn diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ParseResult> ReadAsync(
        string path,
        ILogger? logger = null,
        CancellationToken cancellationToken = default);
}
