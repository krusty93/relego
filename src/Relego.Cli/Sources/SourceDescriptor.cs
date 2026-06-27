namespace Relego.Cli.Sources;

/// <summary>
/// A source's self-owned identity. Used only as a label for reporting/logging,
/// never branched on. Replaces a central source-kind enum so the source registry
/// stays open for extension (ADR-008 §5).
/// </summary>
/// <param name="Id">Stable machine id, e.g. <c>"kindle"</c>, <c>"kobo"</c>.</param>
/// <param name="DisplayName">Human label, e.g. <c>"Kindle"</c>, <c>"Kobo"</c>.</param>
public sealed record SourceDescriptor(string Id, string DisplayName);
