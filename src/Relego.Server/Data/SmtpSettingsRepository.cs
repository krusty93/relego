using System.Data;
using Dapper;
using Relego.Server.Infrastructure.Smtp;

namespace Relego.Server.Data;

/// <summary>
/// Single-row store for the outgoing mail server configuration.
/// </summary>
/// <remarks>
/// Environment variables seed this table on first boot; once a client saves settings the
/// stored row wins, so the web UI is authoritative without losing an existing env-only setup.
/// </remarks>
public sealed class SmtpSettingsRepository(IDbConnection connection)
{
    public async Task<StoredSmtpSettings?> GetAsync()
    {
        var row = await connection.QuerySingleOrDefaultAsync<Row>(
            """
            SELECT host AS Host, port AS Port, from_address AS FromAddress,
                   username AS Username, password AS Password,
                   skip_cert_verify AS SkipCertVerify,
                   updated_at AS UpdatedAtText
            FROM smtp_settings WHERE id = 1
            """).ConfigureAwait(false);

        return row is null
            ? null
            : new StoredSmtpSettings(
                row.Host,
                (int)row.Port,
                row.FromAddress,
                row.Username,
                row.Password,
                row.SkipCertVerify != 0,
                DateTimeOffset.Parse(row.UpdatedAtText, System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<StoredSmtpSettings> UpsertAsync(SmtpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var updatedAt = DateTimeOffset.UtcNow;

        await connection.ExecuteAsync(
            """
            INSERT INTO smtp_settings (id, host, port, from_address, username, password, skip_cert_verify, updated_at)
            VALUES (1, @Host, @Port, @FromAddress, @Username, @Password, @SkipCertVerify, @UpdatedAt)
            ON CONFLICT(id) DO UPDATE SET
                host = excluded.host,
                port = excluded.port,
                from_address = excluded.from_address,
                username = excluded.username,
                password = excluded.password,
                skip_cert_verify = excluded.skip_cert_verify,
                updated_at = excluded.updated_at
            """,
            new
            {
                settings.Host,
                settings.Port,
                settings.FromAddress,
                settings.Username,
                settings.Password,
                SkipCertVerify = settings.SkipCertificateVerification ? 1 : 0,
                UpdatedAt = updatedAt.UtcDateTime.ToString("O"),
            }).ConfigureAwait(false);

        return new StoredSmtpSettings(
            settings.Host,
            settings.Port,
            settings.FromAddress,
            settings.Username,
            settings.Password,
            settings.SkipCertificateVerification,
            updatedAt);
    }

    private sealed class Row
    {
        public string Host { get; set; } = string.Empty;
        public long Port { get; set; }
        public string FromAddress { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public long SkipCertVerify { get; set; }
        public string UpdatedAtText { get; set; } = string.Empty;
    }
}

/// <summary>The persisted mail server configuration and when it was saved.</summary>
public sealed record StoredSmtpSettings(
    string Host,
    int Port,
    string FromAddress,
    string Username,
    string Password,
    bool SkipCertificateVerification,
    DateTimeOffset UpdatedAt);
