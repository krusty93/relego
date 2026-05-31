using System.Text.Json.Serialization;
using Relego.Core.Contracts;

namespace Relego.Cli.Infrastructure;

/// <summary>
/// Source-generated JSON serializer context for all contract types used by the CLI.
/// Required for trimmed/NativeAOT publish.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SyncRequest))]
[JsonSerializable(typeof(SyncResponse))]
[JsonSerializable(typeof(SettingsResponse))]
[JsonSerializable(typeof(UpdateSettingsRequest))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(ExclusionsResponse))]
[JsonSerializable(typeof(HighlightsResponse))]
[JsonSerializable(typeof(HighlightItemDto))]
[JsonSerializable(typeof(SetWeightRequest))]
[JsonSerializable(typeof(List<WeightedHighlightDto>))]
[JsonSerializable(typeof(RenameBookRequest))]
[JsonSerializable(typeof(RecapTriggerResponse))]
internal partial class RelegoJsonContext : JsonSerializerContext;
