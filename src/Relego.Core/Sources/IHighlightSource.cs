using Microsoft.Extensions.Logging;
using Relego.Core.Parsing;

namespace Relego.Core.Sources;

/// <summary>
/// The common, self-describing abstraction through which every highlight source
/// (Kindle clippings, Kobo database, …) feeds the import pipeline. Each source
/// owns its identity via <see cref="Descriptor"/> and returns the existing
/// <see cref="ParseResult"/> so downstream components stay source-agnostic.
/// </summary>
/// <remarks>
/// A new source is added by implementing this interface and registering it once
/// in DI — no edits to the resolver, workflow, command, or any enum (FR-011).
/// </remarks>
public interface IHighlightSource
{
    /// <summary>
    /// This source's identity — used only as a label for reporting/logging,
    /// never branched on.
    /// </summary>
    SourceDescriptor Descriptor { get; }

    /// <summary>
    /// Detection owned by the source: encapsulates this source's filename / directory /
    /// device rules so the resolver needs no per-source branching.
    /// </summary>
    /// <param name="userPath">
    /// An explicit path to resolve, or <see langword="null"/> to probe connected devices.
    /// </param>
    /// <returns>
    /// A <see cref="SourceProbe"/> whose <see cref="SourceProbe.FoundPath"/> is the concrete
    /// file this source can read (or <see langword="null"/>), and whose
    /// <see cref="SourceProbe.ProbedLocations"/> lists everywhere it looked.
    /// </returns>
    SourceProbe Locate(string? userPath);

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
