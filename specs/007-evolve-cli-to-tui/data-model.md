# Data Model: Evolve CLI to TUI

**Feature**: 007-evolve-cli-to-tui
**Phase**: 1 — Design
**Date**: 2026-05-09

---

## Overview

The TUI is a client-side layer with no server-side storage changes. All data is fetched from the existing REST API and held in memory for the duration of the TUI session. The "data model" consists of:
1. TUI view models (client-side projections of API responses)
2. Screen abstractions and navigation model
3. Render loop infrastructure types

No new database tables, migrations, or API contracts are introduced.

---

## View Models

### BookViewModel

Client-side aggregation of highlights grouped by book. Produced by grouping `HighlightItemDto` records.

```csharp
record BookViewModel
{
    string Title { get; init; }
    string Author { get; init; }
    int HighlightCount { get; init; }
    IReadOnlyList<HighlightViewModel> Highlights { get; init; }
}
```

**Source**: Grouped from `HighlightsResponse.Items` by `(BookTitle, AuthorName)`.

### HighlightViewModel

A single highlight within a book, enriched with exclusion/weight status.

```csharp
record HighlightViewModel
{
    int Id { get; init; }
    string Text { get; init; }
    bool IsExcluded { get; init; }
    int? Weight { get; init; }  // null = default weight
}
```

**Source**: Mapped from `HighlightItemDto`. Exclusion and weight status enriched from `ExclusionsResponse` and `GetWeightsAsync()` data.

### ServerStatus

Snapshot of server connection and configuration state, used by `StatusChrome`.

```csharp
record ServerStatus
{
    bool IsConnected { get; init; }
    string ServerUrl { get; init; }
    bool KindleEmailConfigured { get; init; }
    bool DeliveryEmailConfigured { get; init; }
    string? Version { get; init; }
}
```

**Source**: Derived from `PingAsync` (`GET /` returning HTTP 200 = connected) + `GetStatusAsync` (`KindleEmailConfigured` and `DeliveryEmailConfigured` fields) and assembly version. The delivery warning fires when **neither** flag is `true`.

---

## Screen Model

### IScreen Interface

Each TUI page implements this interface. The `TuiApp` orchestrator calls these methods in the render loop.

```csharp
interface IScreen
{
    /// Returns the renderable content for the screen's content area.
    IRenderable Render();

    /// Handles a key press. Returns a result indicating the next action.
    Task<ScreenResult> HandleKeyAsync(ConsoleKeyInfo key, CancellationToken ct);

    /// Called when the screen becomes active (pushed onto stack).
    Task InitializeAsync(CancellationToken ct);

    /// Returns key hint text for the bottom bar (e.g., "[↑↓] Navigate · [Enter] Select · [Q] Quit").
    string KeyHints { get; }
}
```

### ScreenResult

Returned by `HandleKeyAsync` to signal the orchestrator what to do next.

```csharp
enum ScreenAction { None, Push, Pop, Quit }

record ScreenResult(ScreenAction Action, IScreen? Next = null);
```

- `None` — key handled, re-render current screen.
- `Push` — navigate to `Next` screen (push on stack).
- `Pop` — go back to previous screen (pop stack).
- `Quit` — exit the TUI.

### Screen Stack

The `TuiApp` maintains a `Stack<IScreen>`. Initial stack: `[BookListScreen]`. Navigation:
- Enter on book → Push `HighlightDetailScreen(book)`
- Press `S` → Push `SettingsScreen`
- Press `Esc` → Pop current screen
- Press `Q` / `Ctrl+C` → Quit

---

## Screen Inventory

### BookListScreen

| Field | Type | Source |
|-------|------|--------|
| Books | `List<BookViewModel>` | Grouped from `GET /highlights` |
| SelectedIndex | `int` | Keyboard navigation state |
| SearchQuery | `string?` | Active search filter text |
| IsSearchActive | `bool` | Whether search input is accepting keystrokes |

**Key bindings**: `↑/↓` = navigate, `Enter` = open book, `S` = settings, `/` = search, `R` = refresh, `Q` = quit

### HighlightDetailScreen

| Field | Type | Source |
|-------|------|--------|
| Book | `BookViewModel` | Passed from BookListScreen |
| SelectedIndex | `int` | Keyboard navigation state |
| ActionMenuOpen | `bool` | Whether action menu is showing |

**Key bindings**: `↑/↓` = navigate highlights, `Enter` = open action menu, `R` = refresh, `Esc` = back to book list

**Actions**: Modify weight, Exclude/include highlight, Exclude/include book, Exclude/include author, Delete highlight

### SettingsScreen

| Field | Type | Source |
|-------|------|--------|
| Settings | `SettingsResponse` | `GET /settings` |
| SelectedField | `int` | Keyboard navigation state |
| IsEditing | `bool` | Whether an inline editor is active |

**Key bindings**: `↑/↓` = navigate fields, `Enter` = edit field, `T` = send test email, `R` = refresh, `Esc` = back

---

## Layout Structure

```
┌─────────────────────────────────────────────────────┐
│  ███████╗██╗   ██╗███╗   ██╗███╗   ██╗██╗   ██╗   │
│  ██╔════╝██║   ██║████╗  ██║████╗  ██║╚██╗ ██╔╝   │  ← Figlet banner
│  ███████╗██║   ██║██╔██╗ ██║██╔██╗ ██║ ╚████╔╝    │    (StatusChrome)
│  ╚════██║██║   ██║██║╚██╗██║██║╚██╗██║  ╚██╔╝     │
│  ███████║╚██████╔╝██║ ╚████║██║ ╚████║   ██║      │
│  ╚══════╝ ╚═════╝ ╚═╝  ╚═══╝╚═╝  ╚═══╝   ╚═╝    │
│  v0.9.3  ● Connected to http://localhost:8080      │  ← Version + status
│  ⚠ Kindle email not configured                     │  ← Warning (conditional)
├─────────────────────────────────────────────────────┤
│                                                     │
│  Title              │ Author          │ Highlights  │  ← Content area
│  ────────────────── │ ─────────────── │ ─────────── │    (current screen)
│ ▸ Atomic Habits     │ James Clear     │         47  │
│   Deep Work         │ Cal Newport     │         23  │
│   Thinking, Fast... │ Daniel Kahneman │         31  │
│                                                     │
├─────────────────────────────────────────────────────┤
│  [↑↓] Navigate · [Enter] View · [S] Settings ·     │  ← Key hints
│  [/] Search · [R] Refresh · [Q] Quit               │    (from current screen)
└─────────────────────────────────────────────────────┘
```

**Spectre.Console Layout mapping**:
```csharp
var layout = new Layout("Root")
    .SplitRows(
        new Layout("Chrome"),    // StatusChrome: Figlet + version + status + warning
        new Layout("Content"),   // Current screen's Render() output
        new Layout("KeyHints")   // Current screen's KeyHints
    );
```

---

## API Calls Used by TUI

| TUI Action | HTTP Method | Endpoint | Existing in SunnyHttpClient? |
|------------|-------------|----------|------------------------------|
| Load book list | GET | `/highlights?page=N&pageSize=200` | **No** — add `GetHighlightsAsync` |
| Load settings | GET | `/settings` | Yes — `GetSettingsAsync` |
| Update settings | PUT | `/settings` | Yes — `PutSettingsAsync` |
| Check server status | GET | `/status` | Yes — `GetStatusAsync` |
| Exclude highlight | POST | `/highlights/{id}/exclude` | Yes — `PostExcludeAsync` |
| Include highlight | DELETE | `/highlights/{id}/exclude` | Yes — `DeleteExcludeAsync` |
| Exclude book | POST | `/books/{id}/exclude` | Yes — `PostExcludeAsync` |
| Include book | DELETE | `/books/{id}/exclude` | Yes — `DeleteExcludeAsync` |
| Exclude author | POST | `/authors/{id}/exclude` | Yes — `PostExcludeAsync` |
| Include author | DELETE | `/authors/{id}/exclude` | Yes — `DeleteExcludeAsync` |
| Set weight | PUT | `/highlights/{id}/weight` | Yes — `PutWeightAsync` |
| Get exclusions | GET | `/exclusions` | Yes — `GetExclusionsAsync` |
| Get weights | GET | `/highlights/weights` | Yes — `GetWeightsAsync` |
| Delete highlight | DELETE | `/highlights/{id}` | **No** — add `DeleteHighlightAsync` |
| Send test email | POST | `/dev/recap/trigger` | **No** — add `PostTestRecapAsync` |
| Load highlights (search) | GET | `/highlights?q=term` | **No** — same as `GetHighlightsAsync` with query param |

**New methods needed**: `GetHighlightsAsync(int page, int pageSize, string? query)`, `DeleteHighlightAsync(int id)`, `PostTestRecapAsync()`
