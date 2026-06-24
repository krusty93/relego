# Feature Specification: 007 Evolve CLI to TUI

**Feature Branch**: `007-evolve-cli-to-tui`  
**Created**: 2026-05-09  
**Status**: Draft  
**Input**: User request: "Evolve the current relego CLI (Spectre.Console.Cli) into a complete Terminal User Interface (TUI) to provide a richer, more guided, end-to-end user experience. The existing CLI command interface must be preserved — the TUI is an additional mode that activates when no command is given."

## User Scenarios & Testing

### User Story 1 — Dual-Mode Launch: TUI vs CLI (Priority: P1)

When the user launches `relego` without any command or arguments, the application enters TUI mode — a persistent, interactive terminal interface. When the user provides a command (e.g., `relego sync`, `relego status`), the existing CLI behavior executes exactly as before, without entering TUI mode. This ensures full backward compatibility while adding the interactive experience.

**Why this priority**: This is the foundational behavioral split. Every other TUI feature depends on this mode detection working correctly. Breaking existing CLI commands would be a regression.

**Independent Test**: Launch `relego` with no arguments and verify the TUI screen appears. Launch `relego sync /path/to/file` and verify the CLI executes the sync command without any TUI rendering.

**Acceptance Scenarios**:

1. **Given** the user runs `relego` with no arguments, **When** the application starts, **Then** the TUI mode activates and the main screen (book list) is displayed.
2. **Given** the user runs `relego sync /path/to/file`, **When** the application starts, **Then** the existing Spectre.Console.Cli CommandApp processes the command and exits — no TUI is rendered.
3. **Given** the user runs `relego config show`, **When** the application starts, **Then** the config show command executes and outputs to stdout — no TUI is rendered.
4. **Given** the user runs `relego --version`, **When** the application starts, **Then** the version is printed and the process exits — no TUI is rendered.
5. **Given** the user runs `relego --help`, **When** the application starts, **Then** the help text is printed and the process exits — no TUI is rendered.

---

### User Story 2 — Startup Splash and Always-Visible Chrome (Priority: P1)

When the TUI launches, the user sees a branded startup experience: a "Relego" Figlet-style ASCII art banner, the CLI version, the server connection status, and — if the Kindle email is not configured — a persistent yellow warning. These elements remain visible across all TUI screens.

**Why this priority**: The splash and status chrome establish trust and orientation. Server connection status and the Kindle email warning prevent the user from performing actions on an unreachable server or forgetting to configure email — reducing wasted effort and confusion.

**Independent Test**: Launch `relego` in TUI mode against a running server and verify the Figlet banner, version, connection status, and (if applicable) Kindle email warning are all visible. Navigate to the settings page and verify they remain visible.

**Acceptance Scenarios**:

1. **Given** TUI mode starts, **When** the main screen renders, **Then** the "Relego" text is displayed in a Figlet-style ASCII font at the top of the screen.
2. **Given** TUI mode starts, **When** the main screen renders, **Then** the CLI version (e.g., `v1.2.0`) is displayed in a fixed location.
3. **Given** a reachable server (determined by a successful `GET /` returning HTTP 200), **When** the TUI main screen renders, **Then** a green connection status indicator is shown (e.g., `● Connected to http://localhost:8080`).
4. **Given** an unreachable server (determined by a failed `GET /`), **When** the TUI main screen renders, **Then** a red connection status indicator is shown (e.g., `● Disconnected — cannot reach http://localhost:8080`).
5. **Given** neither the Kindle email nor the inbox email is configured on the server, **When** any TUI screen renders, **Then** a persistent yellow warning line is displayed beneath the banner (e.g., `⚠ No recap delivery destination configured`).
6. **Given** at least one delivery email (Kindle or inbox) is configured, **When** any TUI screen renders, **Then** no delivery destination warning is shown.
7. **Given** the user navigates from the main screen to the settings page, **When** the settings page renders, **Then** the Figlet banner, version, connection status, and Kindle email warning (if applicable) remain visible.

---

### User Story 3 — Book List Main Screen (Priority: P1)

On startup, the TUI displays a table of all imported books retrieved from the server. The table shows Title, Author, and Highlight Count. The user can navigate rows using keyboard arrow keys.

**Why this priority**: The book list is the primary landing screen and the entry point to all highlight-level interactions. Without it, the TUI has no content to display.

**Independent Test**: Launch `relego` in TUI mode with highlights synced to the server. Verify a table appears with books, and that arrow keys move the selection cursor between rows.

**Acceptance Scenarios**:

1. **Given** the server has imported highlights for multiple books, **When** the TUI main screen renders, **Then** a table is displayed with columns: Title, Author, Highlight Count.
2. **Given** the book list table is displayed, **When** the user presses the up/down arrow keys, **Then** the selected row changes visually.
3. **Given** the server has no imported highlights, **When** the TUI main screen renders, **Then** an empty state message is displayed with a visible in-TUI sync action (e.g., "No highlights found. Press `I` to sync from My Clippings.txt.").
4. **Given** the server returns many books, **When** the table exceeds the terminal height, **Then** the list scrolls to keep the selected row visible.

---

### User Story 9 — In-TUI Sync / Import Workflow (Priority: P1)

When the user is on the TUI main screen, they can run the equivalent of `sunny sync` without leaving TUI mode. The TUI reuses the same sync behavior as the CLI: detect `My Clippings.txt` when possible, allow manual path entry when needed, parse highlights, upload them to the server, show the sync summary, and refresh the book list in place.

**Why this priority**: The TUI is intended to provide a guided, end-to-end experience. Without an in-TUI sync flow, first-time and empty-state users still have to drop back to the CLI, which breaks the main promise of feature 007.

**Independent Test**: Launch `sunny` in TUI mode against an empty server. Trigger the sync action from the main screen, provide a valid clippings path, and verify the TUI shows the sync outcome and refreshes the book list without exiting.

**Acceptance Scenarios**:

1. **Given** the user is on the book list screen, **When** the user triggers the sync action, **Then** the TUI starts the sync/import flow without exiting to the CLI.
2. **Given** a default `My Clippings.txt` path is detected, **When** the sync flow starts, **Then** the TUI offers that path for use or confirmation.
3. **Given** no default clippings path is detected, **When** the sync flow starts, **Then** the TUI prompts for a custom file path and allows the user to cancel safely.
4. **Given** the user provides a missing or invalid file path, **When** the sync flow validates the path, **Then** the TUI shows an actionable validation error and remains usable.
5. **Given** the clippings file parses successfully but contains no highlights, **When** the sync flow completes, **Then** the TUI shows the empty-result outcome and keeps the user on the current screen.
6. **Given** parsing fails, **When** the sync flow processes the file, **Then** the TUI shows the parser error without crashing or dropping to raw CLI output.
7. **Given** the sync upload succeeds, **When** the server returns the import summary, **Then** the TUI shows the same outcome data as the CLI sync flow (new highlights, duplicates, new books, new authors) and refreshes the visible book list.
8. **Given** the server is unreachable during sync, **When** the upload fails, **Then** the TUI shows an inline or dialog error, updates connection state, and remains interactive.

---

### User Story 4 — Highlight Detail Menu (Priority: P2)

When the user selects a book from the book list and presses Enter, a detail view opens showing that book's highlights with an action menu. The user can modify highlight weight, toggle exclusion by highlight/book/author, or delete a highlight.

**Why this priority**: This is the core interactive management flow — the reason users enter TUI mode instead of using individual CLI commands. However, all these operations are already available via CLI commands, so the TUI adds convenience, not new capability.

**Independent Test**: Navigate to a book in the book list, press Enter, and verify the highlight detail view appears with action options. Execute each action and verify the corresponding API call succeeds.

**Acceptance Scenarios**:

1. **Given** the user has selected a book in the book list, **When** the user presses Enter, **Then** a detail view opens showing all highlights for that book.
2. **Given** the highlight detail view is open, **When** the user selects "Modify weight" on a highlight, **Then** a prompt appears to enter a new weight value (1–5) and the change is sent to the server.
3. **Given** the highlight detail view is open, **When** the user selects "Exclude highlight from recap," **Then** the highlight is excluded via the server API and the UI reflects the change.
4. **Given** the highlight detail view is open, **When** the user selects "Include highlight in recap" on an excluded highlight, **Then** the exclusion is removed via the server API and the UI reflects the change.
5. **Given** the highlight detail view is open, **When** the user selects "Exclude book from recap," **Then** all highlights from that book are excluded via the server API (book-level exclusion).
6. **Given** the highlight detail view is open, **When** the user selects "Exclude author from recap," **Then** all highlights from that author are excluded via the server API (author-level exclusion).
7. **Given** the highlight detail view is open, **When** the user selects "Delete highlight," **Then** a confirmation prompt appears; on confirm, the highlight is deleted via the server API and removed from the list.
8. **Given** the highlight detail view is open, **When** the user presses Escape or a back key, **Then** the view returns to the book list.

---

### User Story 5 — Test Email Verification Endpoint (Priority: P1)

The server exposes a dedicated endpoint to send a simple plain-text test email to the configured Kindle address, so the TUI can verify SMTP configuration without generating or delivering a recap.

**Why this priority**: The settings screen depends on this endpoint for a safe SMTP verification flow. Without it, the TUI would have to reuse the development-only recap trigger or omit test-email verification entirely.

**Independent Test**: Call `POST /settings/test-email` against a configured server and verify a plain-text email is sent without recap attachment or recap generation side effects.

**Acceptance Scenarios**:

1. **Given** SMTP settings are valid and Kindle email is configured, **When** the client calls `POST /settings/test-email`, **Then** the server sends a simple plain-text test email and returns a success response.
2. **Given** Kindle email is not configured, **When** the client calls `POST /settings/test-email`, **Then** the server returns an actionable validation error and no email is sent.
3. **Given** SMTP delivery fails, **When** the client calls `POST /settings/test-email`, **Then** the server returns an actionable error response describing the failure.
4. **Given** the client calls `POST /settings/test-email`, **When** the server sends the message, **Then** the message does not include recap content or recap attachments.

---

### User Story 6 — Settings Page (Priority: P2)

The user can navigate to a dedicated settings page from the main screen. The settings page displays all configurable fields: Kindle email, recap schedule (cadence + time), highlight count per recap. The user can edit each setting and send a test email.

**Why this priority**: Configuration is essential but typically a one-time activity. Users who have already configured via CLI commands may never visit this page, but new users benefit from a guided settings experience.

**Independent Test**: Navigate to the settings page from the main screen. Verify all current settings are displayed. Edit a setting and verify the change is persisted on the server.

**Acceptance Scenarios**:

1. **Given** the TUI main screen is displayed, **When** the user presses `S`, **Then** the TUI navigates to the settings page.
2. **Given** the settings page is displayed, **When** the user views it, **Then** all current settings are shown: Kindle email, recap schedule (cadence + time), highlight count per recap.
3. **Given** the settings page is displayed, **When** the user selects "Kindle email" and enters a new email address, **Then** the new email is sent to the server and the settings page refreshes to show the updated value.
4. **Given** the settings page is displayed, **When** the user selects "Recap schedule" and enters a new cadence and time, **Then** the new schedule is sent to the server with the detected timezone.
5. **Given** the settings page is displayed, **When** the user selects "Highlight count" and enters a new value, **Then** the value is validated locally (1–15) and sent to the server.
6. **Given** the settings page is displayed and the .NET environment is `Development`, **When** the user selects "Trigger recap (dev)," **Then** a recap is triggered via `POST /dev/recap/trigger` and the result is displayed. This option is hidden when the environment is not `Development`.
7. **Given** the settings page is displayed, **When** the user selects "Send test email," **Then** a simple plain-text test email (not a recap) is sent via the server API (`POST /settings/test-email`) to verify SMTP configuration, and the result (success or failure) is displayed.
8. **Given** the settings page is displayed, **When** the user presses Escape, **Then** the TUI returns to the main screen (book list).
9. **Given** the settings page is displayed, **When** the user views the page, **Then** visible key hints show available actions (e.g., `[Esc] Back · [↑↓] Navigate · [Enter] Edit`).

---

### User Story 7 — Client-Side Search (Priority: P3)

A search bar allows the user to filter the book list table by typing a query. The search matches against highlight text, author name, and book title — all filtering happens client-side on already-fetched data.

**Why this priority**: Search improves usability for users with large highlight libraries, but the book list is navigable without it. This is a convenience enhancement.

**Independent Test**: Open the TUI with a populated book list. Activate the search bar, type a query, and verify the table filters to show only matching rows. Clear the query and verify all rows reappear.

**Acceptance Scenarios**:

1. **Given** the book list is displayed, **When** the user presses `/`, **Then** a search input field appears.
2. **Given** the search input is active, **When** the user types a query, **Then** the book list filters in real-time to show only books where the title, author, or any highlight text contains the query (case-insensitive).
3. **Given** a filtered book list, **When** the user clears the search query, **Then** the full book list is restored.
4. **Given** the search input is active, **When** the user presses Escape, **Then** the search input closes and the full book list is restored.
5. **Given** a search query that matches no books, **When** the user types the query, **Then** an empty state message is displayed (e.g., "No results matching '[query]'").

---

### User Story 8 — TUI Rendering Approach with Terminal.Gui (Priority: P1)

The TUI is built using Terminal.Gui v2 — a dedicated TUI framework with a native event loop, composable views, keyboard input dispatch, and layout engine. The application uses Terminal.Gui's `Application`, `Window`, `FrameView`, and `View` types to compose screens. Spectre.Console.Cli is still used exclusively for CLI command routing; Terminal.Gui handles only TUI rendering.

**Why this priority**: The rendering approach is foundational — every TUI user story depends on a viable rendering strategy. Terminal.Gui provides a built-in event loop, proper screen management, and widget composition that avoids the complexity and limitations of a custom render loop.

**Independent Test**: Launch the TUI and verify that the screen renders correctly with panels and layout. Press keys and verify the screen updates without flickering or corruption.

**Acceptance Scenarios**:

1. **Given** Terminal.Gui is the TUI framework, **When** the TUI starts, **Then** the screen is composed using Terminal.Gui's `Window` and `FrameView` types with distinct regions (banner, status bar, content area).
2. **Given** the TUI is running, **When** the user presses a key, **Then** the content area updates without full-screen flicker (using Terminal.Gui's native event loop for controlled updates).
3. **Given** the TUI is running, **When** the terminal is resized, **Then** the layout adjusts gracefully to the new terminal dimensions.
4. **Given** the TUI is running, **When** the user presses `Q` or `Ctrl+C`, **Then** the TUI exits cleanly, restoring the terminal to its original state.

---

### Edge Cases

- **Server unreachable at TUI start**: TUI launches with a red "Disconnected" status; book list shows empty state with message "Cannot reach server." The user can still navigate to Settings to verify the server URL.
- **Server becomes unreachable during TUI session**: When an action fails due to connectivity, the status indicator updates to red and the error is shown inline — the TUI does not crash.
- **Very large number of books (100+)**: The book list must remain navigable through scrolling; rendering performance must not degrade noticeably.
- **Terminal too small**: If the terminal dimensions are below a minimum usable size (e.g., less than 80×24), the TUI displays a message asking the user to resize.
- **Non-interactive terminal (piped input)**: If stdin is not a TTY, the TUI does not start; the help text is displayed instead (same as `sunny --help`).
- **Concurrent modification**: If another CLI instance modifies data while the TUI is open, the TUI shows stale data until the user presses `R` to refresh the current screen.
- **Interrupted API call**: If the user navigates away during an in-flight API call, the TUI cancels the request and returns to the previous screen without error.
- **Figlet rendering in narrow terminals**: If the terminal is too narrow for the full Figlet text, fall back to a plain-text "Relego" heading.

## Requirements

### Functional Requirements

- **FR-007-01**: Application MUST detect whether command-line arguments contain a known command; if no command is provided, TUI mode activates; if a command is provided, the existing CLI CommandApp processes it.
- **FR-007-02**: Application MUST display a "Relego" Figlet-style ASCII art banner at the top of the TUI on every screen.
- **FR-007-03**: Application MUST display the CLI version on every TUI screen.
- **FR-007-04**: Application MUST display server connection status (connected/disconnected) on every TUI screen, determined by calling `GET /` on the server and checking for HTTP 200. Updated on initial load, after each API call failure, and on manual refresh (`R`).
- **FR-007-05**: Application MUST display a persistent yellow warning on every TUI screen when the Kindle email is not configured on the server.
- **FR-007-06**: TUI MUST display a book list table on the main screen with columns: Title, Author, Highlight Count.
- **FR-007-07**: TUI MUST support keyboard navigation (up/down arrow keys) for selecting rows in the book list.
- **FR-007-08**: TUI MUST support pressing Enter on a selected book to open the highlight detail view for that book.
- **FR-007-09**: TUI MUST provide actions in the highlight detail view: modify weight, exclude/include by highlight, exclude/include by book, exclude/include by author, delete highlight.
- **FR-007-09a**: TUI main screen MUST provide a sync/import action that is accessible from both populated and empty states.
- **FR-007-09b**: TUI MUST allow the user to run the highlight sync workflow entirely inside TUI mode without exiting to the CLI.
- **FR-007-09c**: TUI sync MUST auto-detect `My Clippings.txt` using the same detection logic as the CLI and MUST allow manual path entry or cancellation when auto-detection is unavailable or unsuitable.
- **FR-007-09d**: TUI sync MUST reuse the same parsing, request mapping, and `POST /sync` upload behavior as CLI `sunny sync`.
- **FR-007-09e**: After a successful sync, TUI MUST show the sync outcome summary and refresh the current book list data.
- **FR-007-10**: TUI MUST provide a settings page accessible from the main screen via the `S` key.
- **FR-007-11**: Settings page MUST allow editing: Kindle email, recap schedule (cadence + time), highlight count per recap.
- **FR-007-12**: Server MUST expose `POST /settings/test-email` to send a simple plain-text test email for SMTP verification without generating a recap.
- **FR-007-12a**: Settings page MUST support sending a simple plain-text test email via `POST /settings/test-email` to verify SMTP configuration.
- **FR-007-12b**: Settings page MUST support triggering a recap via `POST /dev/recap/trigger`, but ONLY when the .NET environment is `Development`. This option MUST be hidden in non-Development environments.
- **FR-007-13**: TUI MUST provide a client-side search bar (activated by `/`) that filters the book list by title, author, and highlight text (case-insensitive).
- **FR-007-14**: TUI MUST use Terminal.Gui v2 as the TUI rendering framework. Spectre.Console.Cli continues to be used for CLI command routing when arguments are provided.
- **FR-007-15**: TUI MUST use Terminal.Gui's `Application`, `Window`, `FrameView`, and `View` APIs for screen composition and event-driven rendering.
- **FR-007-16**: TUI MUST provide visible key hints on each screen showing available actions and navigation shortcuts.
- **FR-007-16a**: TUI MUST support a refresh action (`R` key) on every screen that re-fetches data from the server and re-renders the current screen without navigating away.
- **FR-007-17**: TUI MUST exit cleanly on `Q` or `Ctrl+C`, restoring the terminal state.
- **FR-007-18**: TUI MUST NOT modify any existing REST API endpoints; all interactions use the current API surface.
- **FR-007-19**: TUI MUST show an appropriate empty state when no highlights have been imported, including a visible sync/import action.
- **FR-007-20**: TUI MUST detect non-interactive terminals (stdin is not a TTY) and fall back to displaying help text instead of starting TUI mode.
- **FR-007-21**: All existing CLI commands MUST continue to function identically when invoked with arguments — zero behavioral regression.

### Key Entities

- **TuiApp**: Top-level TUI orchestrator managing the render loop, keyboard input dispatch, and screen transitions.
- **Screen**: An abstraction representing a distinct TUI page (BookListScreen, HighlightDetailScreen, SettingsScreen) with its own layout, data, and input handling.
- **StatusChrome**: The persistent header region containing the Figlet banner, version, connection status, and Kindle email warning — shared across all screens.
- **SearchFilter**: Client-side filter component that narrows the book list based on a text query matched against title, author, and highlight text.
- **SyncWorkflow**: Shared application workflow used by both CLI and TUI to resolve the clippings path, parse highlights, upload the sync request, and return a structured outcome summary.

## Success Criteria

### Measurable Outcomes

- **SC-007-01**: Users can launch the TUI by running `sunny` with no arguments and see the book list within 3 seconds (under normal network conditions).
- **SC-007-02**: All existing CLI commands (`sunny sync`, `sunny status`, `sunny config *`, `sunny exclude *`, `sunny weight *`) continue to work identically — zero regressions.
- **SC-007-03**: Users can navigate the book list, view highlight details, and modify settings without leaving the TUI — completing a full management workflow in a single session.
- **SC-007-04**: Users without Kindle email configured are informed by a persistent warning visible on every screen, reducing failed recap deliveries.
- **SC-007-05**: Users with 100+ books can scroll and search the book list without perceptible rendering lag.
- **SC-007-06**: The TUI renders correctly in terminals with at least 80 columns and 24 rows.
- **SC-007-07**: Search filters the book list as the user types, showing results within 200ms of each keystroke.
- **SC-007-08**: Users can verify SMTP delivery with a plain-text test email without sending a real recap.
- **SC-007-09**: A first-time user with no imported books can complete a sync from within the TUI and see the refreshed book list in the same session.

## Assumptions

- Terminal.Gui v2 (added as a new NuGet dependency) provides the event loop, input dispatch, widget composition, and layout engine needed for TUI rendering.
- Spectre.Console.Cli continues to handle CLI command routing unchanged; Terminal.Gui is used exclusively for the TUI mode.
- The REST API surface from features 004 and 005 is mostly sufficient for TUI functionality. One new endpoint is required: `POST /settings/test-email` to send a simple plain-text test email for SMTP verification. This missing API is documented as a comment on issue #188.
- The existing `sunny sync` flow can be extracted into a shared workflow consumed by both the CLI command and the TUI without changing the current CLI contract.
- All data displayed in the TUI (books, highlights, settings) is fetched via existing REST endpoints and cached client-side for the duration of the TUI session.
- Single-user architecture — no concurrent TUI sessions modifying the same server.
- The TUI is a convenience layer; all operations remain available via CLI commands for scripting and automation.
- If API optimizations or new endpoints are identified during implementation, they will be documented as comments on issue #188 rather than included in this feature's scope.
- Keyboard shortcuts: `S` = Settings, `/` = Search, `R` = Refresh, `Q` / `Ctrl+C` = Exit, `Esc` = Back. These are initial defaults subject to refinement during implementation.
