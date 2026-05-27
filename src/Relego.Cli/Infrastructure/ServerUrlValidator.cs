namespace Relego.Cli.Infrastructure;

/// <summary>
/// Validates the configured Relego server URL value.
/// </summary>
public static class ServerUrlValidator
{
    public const string DefaultServerUrl = "http://localhost:8080";

    public enum ValidationResult
    {
        Valid,
        Missing,
        Malformed,
    }

    /// <summary>
    /// Returns the configured server URL, or the documented localhost default when no value is configured.
    /// </summary>
    public static string GetConfiguredOrDefault(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DefaultServerUrl
            : value.Trim();
    }

    /// <summary>
    /// Resolves the configured server URL to a usable value and validates it.
    /// </summary>
    public static ValidationResult Resolve(string? value, out Uri? uri, out string resolvedValue)
    {
        resolvedValue = GetConfiguredOrDefault(value);
        return Validate(resolvedValue, out uri);
    }

    /// <summary>
    /// Validates <paramref name="value"/> and, on success, outputs the parsed <see cref="Uri"/>.
    /// </summary>
    public static ValidationResult Validate(string? value, out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Missing;

        if (!Uri.TryCreate(value, UriKind.Absolute, out uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            uri = null;
            return ValidationResult.Malformed;
        }

        return ValidationResult.Valid;
    }
}
