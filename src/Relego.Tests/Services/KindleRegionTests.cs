using System.Text.RegularExpressions;
using Relego.Server.Sync;

namespace Relego.Tests.Services;

/// <summary>
/// Covers the region allowlist shared by the server and the extension: the eight validated hosts,
/// the ids that must stay unavailable, and the parity between
/// <see cref="ParseProfile.KindleRegions"/> and the extension copy in
/// <c>src/relego.extension/src/profile.ts</c>.
///
/// The parity is asserted by reading the extension source rather than trusted: a region the
/// extension offers but the server rejects (or the reverse) breaks the connect flow for the user
/// with no server-side error to point at.
/// </summary>
public sealed class KindleRegionTests
{
    private static readonly (string Id, string DisplayName, string Host, string SignInHost)[] Expected =
    [
        ("us", "United States", "read.amazon.com", "www.amazon.com"),
        ("ca", "Canada", "read.amazon.ca", "www.amazon.ca"),
        ("uk", "United Kingdom", "read.amazon.co.uk", "www.amazon.co.uk"),
        ("de", "Germany", "lesen.amazon.de", "www.amazon.de"),
        ("fr", "France", "lire.amazon.fr", "www.amazon.fr"),
        ("it", "Italy", "leggi.amazon.it", "www.amazon.it"),
        ("es", "Spain", "leer.amazon.es", "www.amazon.es"),
        ("br", "Brazil", "ler.amazon.com.br", "www.amazon.com.br"),
    ];

    [Fact]
    public void Allowlist_HoldsExactlyTheEightValidatedRegions()
    {
        Assert.Equal(Expected.Length, ParseProfile.KindleRegions.Count);

        for (var index = 0; index < Expected.Length; index++)
        {
            var (id, displayName, host, signInHost) = Expected[index];
            var region = ParseProfile.KindleRegions[index];

            Assert.Equal(id, region.Id);
            Assert.Equal(displayName, region.DisplayName);
            Assert.Equal(host, region.NotebookHost);
            Assert.Equal<IEnumerable<string>>([signInHost], region.SignInHostPatterns);
        }
    }

    [Fact]
    public void Allowlist_IsIdenticalToTheExtensionCopy()
    {
        var extension = ReadExtensionRegions();

        Assert.Equal(ParseProfile.KindleRegions.Count, extension.Count);

        for (var index = 0; index < ParseProfile.KindleRegions.Count; index++)
        {
            var server = ParseProfile.KindleRegions[index];
            var client = extension[index];

            Assert.Equal(server.Id, client.Id);
            Assert.Equal(server.DisplayName, client.DisplayName);
            Assert.Equal(server.NotebookHost, client.NotebookHost);
            Assert.Equal<IEnumerable<string>>(server.SignInHostPatterns, client.SignInHostPatterns);
        }
    }

    [Fact]
    public void ProfileVersion_IsIdenticalToTheExtensionCopy()
    {
        var match = Regex.Match(File.ReadAllText(ExtensionProfilePath()), """PROFILE_VERSION = "(?<version>[^"]+)";""");

        Assert.True(match.Success, "PROFILE_VERSION was not found in the extension profile.");
        Assert.Equal(KindleCloudProvider.ProfileVersion, match.Groups["version"].Value);
    }

    [Theory]
    [InlineData("us", "read.amazon.com")]
    [InlineData("ca", "read.amazon.ca")]
    [InlineData("uk", "read.amazon.co.uk")]
    [InlineData("de", "lesen.amazon.de")]
    [InlineData("fr", "lire.amazon.fr")]
    [InlineData("it", "leggi.amazon.it")]
    [InlineData("es", "leer.amazon.es")]
    [InlineData("br", "ler.amazon.com.br")]
    public void TryResolveRegion_ResolvesAnAllowlistedIdToItsOwnHost(string regionId, string expectedHost)
    {
        Assert.True(new ParseProfile("test", ParseProfile.KindleRegions).TryResolveRegion(regionId, out var region));
        Assert.Equal(expectedHost, region!.NotebookHost);
    }

    [Theory]
    [InlineData("jp")]
    [InlineData("cn")]
    [InlineData("dk")]
    [InlineData("ie")]
    [InlineData("pl")]
    [InlineData("com")]
    [InlineData("US")]
    [InlineData("de ")]
    [InlineData("https://read.amazon.com")]
    [InlineData("read.amazon.com")]
    [InlineData("www.amazon.de")]
    [InlineData("us/../de")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveRegion_RejectsUnavailableAndHostShapedInput(string regionId)
    {
        Assert.False(new ParseProfile("test", ParseProfile.KindleRegions).TryResolveRegion(regionId, out var region));
        Assert.Null(region);
    }

    private sealed record ExtensionRegion(
        string Id,
        string DisplayName,
        string NotebookHost,
        IReadOnlyList<string> SignInHostPatterns);

    private static IReadOnlyList<ExtensionRegion> ReadExtensionRegions()
    {
        var source = File.ReadAllText(ExtensionProfilePath());
        var pattern = new Regex(
            """id:\s*"(?<id>[^"]+)",\s*displayName:\s*"(?<name>[^"]+)",\s*notebookHost:\s*"(?<host>[^"]+)",\s*signInHostPatterns:\s*\[(?<signIn>[^\]]*)\]""",
            RegexOptions.Singleline);

        var regions = new List<ExtensionRegion>();

        foreach (Match match in pattern.Matches(source))
        {
            var signInHosts = Regex.Matches(match.Groups["signIn"].Value, "\"([^\"]+)\"")
                .Select(quoted => quoted.Groups[1].Value)
                .ToList();

            regions.Add(new ExtensionRegion(
                match.Groups["id"].Value,
                match.Groups["name"].Value,
                match.Groups["host"].Value,
                signInHosts));
        }

        return regions;
    }

    private static string ExtensionProfilePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "relego.extension", "src", "profile.ts");
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate the extension parse profile by walking up from {AppContext.BaseDirectory}");
    }
}
