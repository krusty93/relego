namespace Relego.Core.Sources;

/// <summary>
/// The result of resolving an input (an explicit path or a device probe) into the
/// set of highlight sources to import from.
/// </summary>
/// <param name="Found">True when at least one source resolved.</param>
/// <param name="Sources">
/// Every detected source, in DI/registration order. When more than one device is
/// connected this carries them all — the import workflow imports each with
/// per-source failure isolation (FR-017, FR-018).
/// </param>
/// <param name="ProbedLocations">
/// The union of every location each source searched. Populated for actionable
/// not-found errors (FR-009).
/// </param>
public sealed record SourceResolution(
    bool Found,
    IReadOnlyList<ResolvedSource> Sources,
    IReadOnlyList<string> ProbedLocations);
