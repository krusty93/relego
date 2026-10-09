namespace Relego.Server.Sync;

/// <summary>
/// Signals a sync operation failure that maps to a specific HTTP status code.
/// </summary>
public sealed class SyncOperationException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The actionable message.</param>
    /// <param name="statusCode">The HTTP status code the endpoint should return.</param>
    public SyncOperationException(string message, int statusCode)
        : base(message) => StatusCode = statusCode;

    /// <summary>HTTP status code the endpoint should return.</summary>
    public int StatusCode { get; }
}
