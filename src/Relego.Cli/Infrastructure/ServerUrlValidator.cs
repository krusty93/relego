namespace Relego.Cli.Infrastructure;

/// <summary>
/// Validates the configured Relego server URL value.
/// </summary>
public static class ServerUrlValidator
{
    public const string ConfigKey = "Server:Url";
    public const string DefaultServerUrl = "http://localhost:8080";
    public const string EnvironmentVariableName = "SERVER_URL";

    public enum ValidationResult
    {
        Valid,
        Missing,
        Malformed,
    }

    /// <summary>
    /// Returns the configured server URL, preferring the environment override and falling back to the documented localhost default.
    /// </summary>
    public static string GetConfiguredOrDefault(string? configuredValue, string? environmentValue = null)
    {
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return environmentValue.Trim();

        return string.IsNullOrWhiteSpace(configuredValue)
            ? DefaultServerUrl
            : configuredValue.Trim();
    }

    /// <summary>
    /// Resolves the configured server URL to a usable value and validates it.
    /// </summary>
    public static ValidationResult Resolve(string? configuredValue, string? environmentValue, out Uri? uri, out string resolvedValue)
    {
        resolvedValue = GetConfiguredOrDefault(configuredValue, environmentValue);
        return Validate(resolvedValue, out uri);
    }

    /// <summary>
    /// Resolves the configured server URL to a usable value and validates it.
    /// </summary>
    public static ValidationResult Resolve(string? configuredValue, out Uri? uri, out string resolvedValue)
    {
        resolvedValue = GetConfiguredOrDefault(configuredValue);
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
