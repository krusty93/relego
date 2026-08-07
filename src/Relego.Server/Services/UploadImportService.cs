using Relego.Core.Contracts;
using Relego.Core.Parsing;
using Relego.Core.Sources;

namespace Relego.Server.Services;

/// <summary>
/// Parses an uploaded highlight export and turns it into a <see cref="SyncRequest"/>.
/// </summary>
/// <remarks>
/// The upload is untrusted: it is written to a temp file, routed by content sniffing
/// rather than by the client-supplied file name, and the temp file is always deleted.
/// The parsers themselves are the same ones the CLI uses (<see cref="Relego.Core.Sources"/>),
/// so an upload and a device import produce identical results.
/// </remarks>
public sealed class UploadImportService(ILogger<UploadImportService> logger)
{
    /// <summary>Largest upload accepted, in bytes.</summary>
    public const long MaxUploadBytes = 64L * 1024 * 1024;

    // "SQLite format 3\0" — the 16-byte magic header every SQLite database starts with.
    private static readonly byte[] SqliteHeader = "SQLite format 3\u0000"u8.ToArray();

    /// <summary>
    /// Reads <paramref name="content"/> into a temp file, detects its format, and parses it.
    /// </summary>
    /// <exception cref="UploadImportException">The upload is empty, too large, or not a supported format.</exception>
    public async Task<UploadImportResult> ParseAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            "relego-upload-" + Guid.NewGuid().ToString("N"));

        try
        {
            var written = await WriteToTempAsync(content, tempPath, cancellationToken).ConfigureAwait(false);

            if (written == 0)
            {
                throw new UploadImportException("The uploaded file is empty.");
            }

            var descriptor = DetectSource(tempPath);
            IHighlightSource source = descriptor.Id == "kobo"
                ? new KoboReaderSource()
                : new KindleClippingsSource();

            ParseResult parseResult;
            try
            {
                parseResult = descriptor.Id == "kobo"
                    ? await source.ReadAsync(tempPath, logger, cancellationToken).ConfigureAwait(false)
                    : await ClippingsParser.ParseAsync(tempPath, logger).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException)
            {
                throw new UploadImportException(
                    $"'{fileName}' could not be read as a {descriptor.DisplayName} export: {ex.Message}",
                    ex);
            }

            return new UploadImportResult(descriptor, parseResult);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>Converts a parse result into the shared bulk-import request shape.</summary>
    public static SyncRequest ToSyncRequest(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new SyncRequest
        {
            Books = [.. result.Books.Select(book => new SyncBookRequest
            {
                Title = book.Title,
                Author = book.Author,
                Highlights = [.. book.Highlights.Select(highlight => new SyncHighlightRequest
                {
                    Text = highlight.Text,
                    AddedOn = highlight.AddedOn,
                })],
            })],
        };
    }

    private static async Task<long> WriteToTempAsync(Stream content, string tempPath, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long written = 0;

        await using var file = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: buffer.Length,
            useAsync: true);

        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            written += read;

            if (written > MaxUploadBytes)
            {
                throw new UploadImportException(
                    $"The uploaded file is larger than {MaxUploadBytes / (1024 * 1024)} MB.");
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return written;
    }

    private static SourceDescriptor DetectSource(string path)
    {
        if (HasSqliteHeader(path))
        {
            return new SourceDescriptor("kobo", "Kobo");
        }

        if (LooksLikeText(path))
        {
            return new SourceDescriptor("kindle", "Kindle");
        }

        throw new UploadImportException(
            "Unrecognised file. Upload a Kindle 'My Clippings.txt' or a Kobo 'KoboReader.sqlite'.");
    }

    private static bool HasSqliteHeader(string path)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = File.OpenRead(path);
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        return read >= header.Length && header.SequenceEqual(SqliteHeader);
    }

    private static bool LooksLikeText(string path)
    {
        Span<byte> sample = stackalloc byte[512];
        using var stream = File.OpenRead(path);
        var read = stream.ReadAtLeast(sample, sample.Length, throwOnEndOfStream: false);

        // A NUL byte in the first block means binary; Kindle exports are UTF-8 text.
        return !sample[..read].Contains((byte)0);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temp file is harmless.
        }
    }
}

/// <summary>A parsed upload together with the source it was recognised as.</summary>
public sealed record UploadImportResult(SourceDescriptor Source, ParseResult ParseResult);

/// <summary>An upload that cannot be accepted. The message is safe to show to the user.</summary>
public sealed class UploadImportException : Exception
{
    public UploadImportException(string message) : base(message)
    {
    }

    public UploadImportException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public UploadImportException()
    {
    }
}
