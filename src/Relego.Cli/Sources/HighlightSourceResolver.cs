namespace Relego.Cli.Sources;

/// <summary>
/// Resolves an input into the set of highlight sources to import from. Built from the
/// <em>injected</em> collection of <see cref="IHighlightSource"/>, it calls each source's
/// <see cref="IHighlightSource.Locate"/> and returns <em>every</em> source that resolves.
/// </summary>
/// <remarks>
/// Contains no per-source <c>switch</c>/<c>if</c> and no precedence: detection is owned by
/// each source, so a new source is picked up by the same loop once registered in DI
/// (FR-011, SC-010). DI registration order defines processing order.
/// </remarks>
public sealed class HighlightSourceResolver(IEnumerable<IHighlightSource> sources)
{
    private readonly IReadOnlyList<IHighlightSource> _sources = [.. sources];

    /// <summary>
    /// Resolves <paramref name="userPath"/> (or, when <see langword="null"/>, probes connected
    /// devices) into every detected source.
    /// </summary>
    public SourceResolution Resolve(string? userPath)
    {
        var found = new List<ResolvedSource>();
        var probed = new List<string>();

        foreach (var source in _sources)
        {
            var probe = source.Locate(userPath);
            probed.AddRange(probe.ProbedLocations);

            if (probe.FoundPath is not null)
            {
                found.Add(new ResolvedSource(source, probe.FoundPath));
            }
        }

        return new SourceResolution(found.Count > 0, found, probed);
    }
}
