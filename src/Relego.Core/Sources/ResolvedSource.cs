namespace Relego.Core.Sources;

/// <summary>
/// A source that detection resolved to a concrete file, ready to be read by the
/// import workflow.
/// </summary>
/// <param name="Source">The reader to invoke. Its <see cref="IHighlightSource.Descriptor"/> carries the source identity for the per-source summary/report.</param>
/// <param name="ResolvedPath">The concrete file path to read.</param>
public sealed record ResolvedSource(IHighlightSource Source, string ResolvedPath);
