using Microsoft.Extensions.Options;
using Relego.Core.Contracts;
using Relego.Server.Data;

namespace Relego.Server.Infrastructure.Smtp;

/// <summary>
/// Resolves the mail server configuration that a send should actually use.
/// </summary>
/// <remarks>
/// Precedence is deliberate and documented in the UI: a row saved from a client wins,
/// otherwise the values bound from configuration (which include the <c>SMTP_*</c>
/// environment variables) are used. That makes environment variables a first-boot seed
/// rather than a permanent override.
/// </remarks>
public sealed class SmtpConfigurationService(
    SmtpSettingsRepository repository,
    IOptions<SmtpSettings> configured)
{
    /// <summary>Returns the settings a send should use, plus where they came from.</summary>
    public async Task<EffectiveSmtpSettings> GetEffectiveAsync()
    {
        var stored = await repository.GetAsync().ConfigureAwait(false);

        if (stored is not null)
        {
            return new EffectiveSmtpSettings(
                new SmtpSettings
                {
                    Host = stored.Host,
                    Port = stored.Port,
                    FromAddress = stored.FromAddress,
                    Username = stored.Username,
                    Password = stored.Password,
                    SkipCertificateVerification = stored.SkipCertificateVerification,
                },
                SmtpSettingsOrigin.Database,
                stored.UpdatedAt);
        }

        var fromConfig = configured.Value;
        var origin = string.IsNullOrWhiteSpace(fromConfig.FromAddress)
            ? SmtpSettingsOrigin.Default
            : SmtpSettingsOrigin.Environment;

        return new EffectiveSmtpSettings(
            new SmtpSettings
            {
                Host = fromConfig.Host,
                Port = fromConfig.Port,
                FromAddress = fromConfig.FromAddress,
                Username = fromConfig.Username,
                Password = fromConfig.Password,
            },
            origin,
            UpdatedAt: null);
    }

    /// <summary>
    /// Applies a partial update on top of the current effective settings and persists it.
    /// A <see langword="null"/> password keeps the stored one; an empty password clears it.
    /// </summary>
    public async Task<EffectiveSmtpSettings> SaveAsync(UpdateSmtpSettingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var current = await GetEffectiveAsync().ConfigureAwait(false);

        var merged = new SmtpSettings
        {
            Host = request.Host?.Trim() ?? current.Settings.Host,
            Port = request.Port ?? current.Settings.Port,
            FromAddress = request.FromAddress?.Trim() ?? current.Settings.FromAddress,
            Username = request.Username?.Trim() ?? current.Settings.Username,
            Password = request.Password ?? current.Settings.Password,
            SkipCertificateVerification = request.SkipCertificateVerification ?? current.Settings.SkipCertificateVerification,
        };

        var stored = await repository.UpsertAsync(merged).ConfigureAwait(false);
        return new EffectiveSmtpSettings(merged, SmtpSettingsOrigin.Database, stored.UpdatedAt);
    }

    /// <summary>Projects the effective settings into the API shape, without the password.</summary>
    public static SmtpSettingsResponse ToResponse(EffectiveSmtpSettings effective)
    {
        ArgumentNullException.ThrowIfNull(effective);

        return new SmtpSettingsResponse
        {
            Host = effective.Settings.Host,
            Port = effective.Settings.Port,
            FromAddress = effective.Settings.FromAddress,
            Username = effective.Settings.Username,
            PasswordSet = !string.IsNullOrEmpty(effective.Settings.Password),
            SkipCertificateVerification = effective.Settings.SkipCertificateVerification,
            Source = effective.Origin switch
            {
                SmtpSettingsOrigin.Database => "database",
                SmtpSettingsOrigin.Environment => "environment",
                _ => "default",
            },
            UpdatedAt = effective.UpdatedAt,
        };
    }
}

/// <summary>Where the effective mail server configuration came from.</summary>
public enum SmtpSettingsOrigin
{
    /// <summary>Nothing has been configured yet; built-in defaults are in effect.</summary>
    Default,

    /// <summary>Values come from configuration or the <c>SMTP_*</c> environment variables.</summary>
    Environment,

    /// <summary>Values were saved by a client and are stored in the database.</summary>
    Database,
}

/// <summary>The mail server configuration in effect, and its provenance.</summary>
public sealed record EffectiveSmtpSettings(
    SmtpSettings Settings,
    SmtpSettingsOrigin Origin,
    DateTimeOffset? UpdatedAt);
