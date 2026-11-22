using System.Globalization;

namespace Relego.Server.Telemetry;

/// <summary>
/// Stores the installation identifier next to the database, so it lives on the same volume and
/// disappears with it. The value is random and carries no information about the host.
/// </summary>
public sealed class InstallationIdStore(string directory)
{
    private const string FileName = "install-id";

    /// <summary>
    /// Returns the identifier for this install, creating it on first use. It is written next to the
    /// database rather than into it, so the identifier survives schema resets.
    /// </summary>
    /// <param name="cancellationToken">Cancels the file access.</param>
    /// <returns>The identifier, or <see langword="null"/> when the volume is not writable and telemetry should be skipped.</returns>
    public async Task<string?> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(directory, FileName);

        if (await TryReadAsync(path, cancellationToken) is { } existing)
            return existing;

        var generated = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        // Two attempts: the second one may replace an identifier file that exists but holds no usable
        // value, which would otherwise leave the install uncounted forever.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(generated.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);

                return generated;
            }
            catch (IOException)
            {
                // Another writer won the race; its identifier is the install's identity from now on.
                if (await TryReadAsync(path, cancellationToken) is { } winner)
                    return winner;

                TryDelete(path);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Nothing left to do; the next heartbeat tries again.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: an unwritable volume simply leaves telemetry off.
        }
    }

    private static async Task<string?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var value = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();

            return value.Length == 0 ? null : value;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
