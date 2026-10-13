namespace Relego.Server.Sync;

/// <summary>
/// Signals a sync operation failure that maps to a specific HTTP status code.
/// </summary>
public sealed class SyncOperationException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The actionable message.</param>
    /// <param name="statusCode">The HTTP status code the endpoint should return.</param>
    /// <param name="recovery">Machine-readable recovery hint, or <c>null</c>.</param>
    public SyncOperationException(string message, int statusCode, string? recovery = null)
        : base(message)
    {
        StatusCode = statusCode;
        Recovery = recovery;
    }

    /// <summary>HTTP status code the endpoint should return.</summary>
    public int StatusCode { get; }

    /// <summary>Machine-readable recovery hint surfaced in the problem response, or <c>null</c>.</summary>
    public string? Recovery { get; }
}
