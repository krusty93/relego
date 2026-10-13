using System.Diagnostics.CodeAnalysis;

namespace Relego.Server.Sync;

/// <summary>
/// A validated Amazon Kindle notebook region. The host is always derived from this profile; clients
/// send only the id.
/// </summary>
public sealed record KindleRegion
{
    /// <summary>Stable region id, for example <c>uk</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable region name, for example <c>United Kingdom</c>.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Official Kindle notebook host for the region.</summary>
    public string NotebookHost { get; init; } = string.Empty;

    /// <summary>Hosts that indicate Amazon sign-in for the region.</summary>
    public IReadOnlyList<string> SignInHostPatterns { get; init; } = [];
}

/// <summary>
/// The versioned parse profile for a provider: the exhaustive region allowlist and the lookup used
/// to validate a client-supplied region id.
/// </summary>
public sealed class ParseProfile
{
    /// <summary>
    /// The eight validated Kindle notebook regions. Japan, China, Denmark, Ireland and Poland are
    /// intentionally absent (documented incompatible or no dedicated notebook host validated).
    /// </summary>
    public static readonly IReadOnlyList<KindleRegion> KindleRegions =
    [
        new KindleRegion { Id = "us", DisplayName = "United States", NotebookHost = "read.amazon.com", SignInHostPatterns = ["www.amazon.com"] },
        new KindleRegion { Id = "ca", DisplayName = "Canada", NotebookHost = "read.amazon.ca", SignInHostPatterns = ["www.amazon.ca"] },
        new KindleRegion { Id = "uk", DisplayName = "United Kingdom", NotebookHost = "read.amazon.co.uk", SignInHostPatterns = ["www.amazon.co.uk"] },
        new KindleRegion { Id = "de", DisplayName = "Germany", NotebookHost = "lesen.amazon.de", SignInHostPatterns = ["www.amazon.de"] },
        new KindleRegion { Id = "fr", DisplayName = "France", NotebookHost = "lire.amazon.fr", SignInHostPatterns = ["www.amazon.fr"] },
        new KindleRegion { Id = "it", DisplayName = "Italy", NotebookHost = "leggi.amazon.it", SignInHostPatterns = ["www.amazon.it"] },
        new KindleRegion { Id = "es", DisplayName = "Spain", NotebookHost = "leer.amazon.es", SignInHostPatterns = ["www.amazon.es"] },
        new KindleRegion { Id = "br", DisplayName = "Brazil", NotebookHost = "ler.amazon.com.br", SignInHostPatterns = ["www.amazon.com.br"] },
    ];

    private readonly Dictionary<string, KindleRegion> _byId;

    /// <summary>Creates a profile from a version and its region allowlist.</summary>
    /// <param name="version">Parse-profile version.</param>
    /// <param name="regions">The exhaustive region allowlist.</param>
    public ParseProfile(string version, IReadOnlyList<KindleRegion> regions)
    {
        Version = version;
        Regions = regions;
        _byId = regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
    }

    /// <summary>Parse-profile version.</summary>
    public string Version { get; }

    /// <summary>The exhaustive region allowlist for this profile.</summary>
    public IReadOnlyList<KindleRegion> Regions { get; }

    /// <summary>
    /// Resolves an allowlisted region id. Returns <c>false</c> for unknown, excluded, empty or
    /// host-shaped input; clients never pass hosts.
    /// </summary>
    /// <param name="regionId">The client-supplied region id.</param>
    /// <param name="region">The resolved region when the id is valid.</param>
    /// <returns><c>true</c> when the id resolves to an allowlisted region.</returns>
    public bool TryResolveRegion(string? regionId, [NotNullWhen(true)] out KindleRegion? region)
    {
        region = null;

        if (string.IsNullOrWhiteSpace(regionId))
            return false;

        if (regionId.Contains("://", StringComparison.Ordinal)
            || regionId.Contains('.', StringComparison.Ordinal)
            || regionId.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        if (!_byId.TryGetValue(regionId, out var resolved))
            return false;

        region = resolved;
        return true;
    }
}
