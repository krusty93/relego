# Data Model: Client CLI

**Feature**: 006-client-cli
**Phase**: 1 — Design
**Date**: 2026-05-02

---

## Overview

The CLI is stateless — it stores no data locally. All domain data lives on the server (SQLite). The CLI's "data model" consists of:
1. Command tree structure and settings classes (Spectre.Console.Cli)
2. The typed HTTP client wrapping server API contracts (already defined in `Relego.Core/Contracts/`)
3. Infrastructure components (Kindle detection, DI bridge, retry pipeline)

---

## Command Tree Model

### Command Hierarchy

```text
relego (root)
├── sync [path]                    → SyncCommand
├── status                         → StatusCommand
├── config (branch)
│   ├── show                       → ConfigShowCommand
│   ├── schedule <cadence> <time>  → ConfigScheduleCommand
│   ├── count <n|show>             → ConfigCountCommand
│   └── email (branch)
│       ├── kindle <address>       → ConfigEmailKindleCommand
│       └── inbox <address>        → ConfigEmailInboxCommand
├── exclude (branch)
│   ├── <type> <id>                → ExcludeAddCommand (default)
│   ├── remove <type> <id>         → ExcludeRemoveCommand
│   └── list                       → ExcludeListCommand
└── weight (branch)
    ├── set <id> <value>           → WeightSetCommand
    └── list                       → WeightListCommand
```

### Command Settings Classes

Each command that accepts arguments defines an inner `Settings` class with Spectre attributes:

```csharp
// SyncCommand.Settings
public class Settings : CommandSettings
{
    [CommandArgument(0, "[path]")]
    [Description("Path to My Clippings.txt. Auto-detected if omitted.")]
    public string? Path { get; set; }
}

// ConfigScheduleCommand.Settings
public class Settings : CommandSettings
{
    [CommandArgument(0, "<cadence>")]
    [Description("Schedule cadence: 'daily' or 'weekly'")]
    public string Cadence { get; set; } = string.Empty;

    [CommandArgument(1, "<time>")]
    [Description("Delivery time in HH:mm format (e.g., 08:00)")]
    public string Time { get; set; } = string.Empty;
}

// ConfigCountCommand.Settings
public class Settings : CommandSettings
{
    [CommandArgument(0, "<count>")]
    [Description("Number of highlights per recap (1-15), or 'show' to display current")]
    public string CountArg { get; set; } = string.Empty;
}

// ExcludeAddCommand.Settings
public class Settings : CommandSettings
{
    [CommandArgument(0, "<type>")]
    [Description("Entity type: 'highlight', 'book', or 'author'")]
    public string Type { get; set; } = string.Empty;

    [CommandArgument(1, "<id>")]
    [Description("Entity identifier (numeric ID)")]
    public int Id { get; set; }
}

// ExcludeRemoveCommand.Settings — same as ExcludeAddCommand.Settings

// WeightSetCommand.Settings
public class Settings : CommandSettings
{
    [CommandArgument(0, "<id>")]
    [Description("Highlight ID")]
    public int Id { get; set; }

    [CommandArgument(1, "<weight>")]
    [Description("Weight value (1-5)")]
    public int Weight { get; set; }
}
```

---

## Infrastructure Components

### SunnyHttpClient

Typed HTTP client wrapping all server REST API calls. Registered via `IHttpClientFactory` with base address from `RELEGO_SERVER`.

```csharp
public class SunnyHttpClient
{
    private readonly HttpClient _http;

    public SunnyHttpClient(HttpClient http) => _http = http;

    // Sync
    public Task<SyncResponse> PostSyncAsync(SyncRequest request, CancellationToken ct);

    // Settings
    public Task<SettingsResponse> GetSettingsAsync(CancellationToken ct);
    public Task<SettingsResponse> PutSettingsAsync(UpdateSettingsRequest request, CancellationToken ct);

    // Status
    public Task<StatusResponse> GetStatusAsync(CancellationToken ct);

    // Exclusions
    public Task PostExcludeAsync(string type, int id, CancellationToken ct);
    public Task DeleteExcludeAsync(string type, int id, CancellationToken ct);
    public Task<ExclusionsResponse> GetExclusionsAsync(CancellationToken ct);

    // Weights
    public Task PutWeightAsync(int highlightId, SetWeightRequest request, CancellationToken ct);
    public Task<List<WeightedHighlightDto>> GetWeightsAsync(CancellationToken ct);
}
```

**Endpoint mapping**:

| Method | Path | SunnyHttpClient Method |
|--------|------|----------------------|
| POST | `/sync` | `PostSyncAsync` |
| GET | `/settings` | `GetSettingsAsync` |
| PUT | `/settings` | `PutSettingsAsync` |
| GET | `/status` | `GetStatusAsync` |
| POST | `/highlights/{id}/exclude` | `PostExcludeAsync("highlight", id)` |
| POST | `/books/{id}/exclude` | `PostExcludeAsync("book", id)` |
| POST | `/authors/{id}/exclude` | `PostExcludeAsync("author", id)` |
| DELETE | `/highlights/{id}/exclude` | `DeleteExcludeAsync("highlight", id)` |
| DELETE | `/books/{id}/exclude` | `DeleteExcludeAsync("book", id)` |
| DELETE | `/authors/{id}/exclude` | `DeleteExcludeAsync("author", id)` |
| GET | `/exclusions` | `GetExclusionsAsync` |
| PUT | `/highlights/{id}/weight` | `PutWeightAsync` |
| GET | `/highlights/weights` | `GetWeightsAsync` |

### KindleDetector

Static utility class for cross-platform Kindle mount detection.

```csharp
public static class KindleDetector
{
    /// <summary>
    /// Attempts to locate 'My Clippings.txt' on a connected Kindle device.
    /// Returns the full file path if found, null otherwise.
    /// </summary>
    public static string? DetectClippingsPath();
}
```

**Detection paths by platform**:

| Platform | Candidate Paths |
|----------|----------------|
| macOS | `/Volumes/Kindle/documents/My Clippings.txt` |
| Linux | `/media/*/Kindle/documents/My Clippings.txt`, `/run/media/*/Kindle/documents/My Clippings.txt` |
| Windows | `D:\documents\My Clippings.txt` through `G:\documents\My Clippings.txt` |

### TypeRegistrar (DI Bridge)

Bridges Spectre.Console.Cli's `ITypeRegistrar` to `Microsoft.Extensions.DependencyInjection`:

```csharp
public sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services;
    public TypeRegistrar(IServiceCollection services) => _services = services;

    public ITypeResolver Build() => new TypeResolver(_services.BuildServiceProvider());
    public void Register(Type service, Type implementation) => _services.AddSingleton(service, implementation);
    public void RegisterInstance(Type service, object implementation) => _services.AddSingleton(service, implementation);
    public void RegisterLazy(Type service, Func<object> factory) => _services.AddSingleton(service, _ => factory());
}

public sealed class TypeResolver : ITypeResolver
{
    private readonly IServiceProvider _provider;
    public TypeResolver(IServiceProvider provider) => _provider = provider;
    public object? Resolve(Type? type) => type is null ? null : _provider.GetService(type);
}
```

### Polly Retry Pipeline

Custom `DelegatingHandler` wrapping Polly's resilience pipeline:

```csharp
public static class HttpClientResilienceExtensions
{
    public static IHttpClientBuilder AddSunnyResilience(this IHttpClientBuilder builder)
    {
        builder.AddHttpMessageHandler(() => new RetryHandler());
        return builder;
    }
}
```

**Retry policy configuration**:

| Parameter | Value |
|-----------|-------|
| Max attempts | 3 (1 initial + 2 retries) |
| Backoff | Exponential: 1s, 2s, 4s |
| Transient codes | 408, 429, 500, 502, 503, 504 |
| Non-retried | All 4xx (except 408, 429), network errors after exhaustion |

---

## Shared Contracts (from Relego.Core)

The CLI uses the following existing DTOs without modification:

| Contract | Usage |
|----------|-------|
| `SyncRequest` / `SyncBookRequest` / `SyncHighlightRequest` | POST /sync body |
| `SyncResponse` | POST /sync response |
| `SettingsResponse` | GET /settings response |
| `UpdateSettingsRequest` | PUT /settings body |
| `StatusResponse` | GET /status response |
| `ExclusionsResponse` / `ExcludedHighlightDto` / `ExcludedBookDto` / `ExcludedAuthorDto` | GET /exclusions response |
| `SetWeightRequest` | PUT /highlights/{id}/weight body |
| `WeightedHighlightDto` | GET /highlights/weights response items |

No changes to `Relego.Core` are required for this feature.

---

## Data Flow: Sync Command

```
User runs: relego sync /path/to/My Clippings.txt
    │
    ▼
┌─────────────────────┐
│ ClippingsParser      │  (existing, in Relego.Cli/Parsing/)
│ ParseAsync(path)     │
└──────────┬──────────┘
           │ ParseResult { Books[], TotalEntries, DuplicatesRemoved }
           ▼
┌─────────────────────┐
│ Map to SyncRequest   │  ParsedBook → SyncBookRequest
│                      │  ParsedHighlight → SyncHighlightRequest
└──────────┬──────────┘
           │ SyncRequest { Books[] }
           ▼
┌─────────────────────┐
│ SunnyHttpClient      │  POST /sync (with Polly retry)
│ PostSyncAsync()      │
└──────────┬──────────┘
           │ SyncResponse { NewHighlights, DuplicateHighlights, NewBooks, NewAuthors }
           ▼
┌─────────────────────┐
│ Spectre Panel        │  Rich formatted summary output
│ Display results      │
└─────────────────────┘
```

---

## Validation Rules (Client-Side)

| Command | Field | Rule | Error Message |
|---------|-------|------|---------------|
| `config schedule` | time | Regex `^([01]\d\|2[0-3]):[0-5]\d$` | "Invalid time format. Use HH:mm (e.g., 08:00)" |
| `config schedule` | cadence | Must be "daily" or "weekly" | "Invalid cadence. Use 'daily' or 'weekly'" |
| `config count` | count | Integer 1–15 | "Count must be between 1 and 15" |
| `weight set` | weight | Integer 1–5 | "Weight must be between 1 and 5" |
| `exclude` | type | Must be "highlight", "book", or "author" | "Invalid type. Use 'highlight', 'book', or 'author'" |
| startup | RELEGO_SERVER | Valid absolute HTTP(S) URI | "RELEGO_SERVER must be set to a valid URL (e.g., http://localhost:5000)" |
