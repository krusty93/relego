using System.Security.Cryptography;
using System.Text;
using Relego.Server.Data;
using Relego.Server.Models;

namespace Relego.Server.Sync;

/// <summary>
/// Issues, hashes and verifies the one-time pairing token used by the extension channel. Only the
/// SHA-256 hash of a token is ever stored or compared; the token itself is never logged.
/// </summary>
public sealed class SyncTokenService(SyncConnectionRepository connections)
{
    private const int TokenByteLength = 32;

    /// <summary>Generates a new random pairing token.</summary>
    /// <returns>A URL-safe, lowercase hexadecimal token.</returns>
    public static string GenerateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenByteLength)).ToLowerInvariant();

    /// <summary>Computes the lowercase hexadecimal SHA-256 hash of a token.</summary>
    /// <param name="token">The token to hash.</param>
    /// <returns>The lowercase hexadecimal hash.</returns>
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Extracts the token from an <c>Authorization: Bearer &lt;token&gt;</c> header value.</summary>
    /// <param name="authorizationHeader">The raw header value, or <c>null</c>.</param>
    /// <returns>The token, or <c>null</c> when the header is missing or malformed.</returns>
    public static string? ExtractBearerToken(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
            return null;

        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authorizationHeader[prefix.Length..].Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }

    /// <summary>
    /// Verifies a bearer token against the live connection's stored hash in constant time.
    /// </summary>
    /// <param name="userId">Owning user id.</param>
    /// <param name="providerId">Provider id.</param>
    /// <param name="token">The token supplied by the extension, or <c>null</c>.</param>
    /// <returns>The authorized connection, or <c>null</c> when the token is missing or invalid.</returns>
    public async Task<SyncConnection?> VerifyAsync(int userId, string providerId, string? token)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        var connection = await connections.GetLiveAsync(userId, providerId);
        if (connection is null || string.IsNullOrEmpty(connection.TokenHash))
            return null;

        var candidate = Encoding.UTF8.GetBytes(HashToken(token));
        var stored = Encoding.UTF8.GetBytes(connection.TokenHash);

        return CryptographicOperations.FixedTimeEquals(candidate, stored) ? connection : null;
    }

    /// <summary>Revokes a connection's token by disconnecting it; highlights are left untouched.</summary>
    /// <param name="connectionId">Connection to revoke.</param>
    /// <returns>A task that completes when the token has been revoked.</returns>
    public Task RevokeAsync(long connectionId) => connections.DisconnectAsync(connectionId);
}
