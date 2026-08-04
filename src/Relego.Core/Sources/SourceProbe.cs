namespace Relego.Core.Sources;

/// <summary>
/// The outcome of a single source's detection attempt. Each source owns its own
/// detection (filename / directory / device rules), so the resolver never branches
/// per source (ADR-008 §5).
/// </summary>
/// <param name="FoundPath">
/// The concrete file this source can read, or <see langword="null"/> when the source
/// is not present at <c>userPath</c> (or on any probed device).
/// </param>
/// <param name="ProbedLocations">
/// Everywhere this source looked. Aggregated by the resolver to build an actionable
/// not-found message (FR-009).
/// </param>
public sealed record SourceProbe(string? FoundPath, IReadOnlyList<string> ProbedLocations);
