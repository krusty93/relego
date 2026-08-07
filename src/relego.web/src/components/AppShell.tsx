import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useRef, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router";
import { api } from "../lib/api";
import { capitalise, formatCount } from "../lib/format";
import { useHotkeys } from "../lib/hotkeys";
import { useSearch } from "../lib/search";
import { useTheme } from "../lib/theme";
import { useToasts } from "../lib/toasts";
import { CommandPalette, type Command } from "./CommandPalette";
import { ShortcutsSheet } from "./ShortcutsSheet";
import { ThemeSwitch } from "./ThemeSwitch";
import { ToastHost } from "./ToastHost";
import {
  HighlightsIcon,
  ImportIcon,
  KeyboardIcon,
  LibraryIcon,
  RecapsIcon,
  SearchIcon,
  SettingsIcon,
} from "./icons";

const NAV = [
  { to: "/app", label: "Library", Icon: LibraryIcon, chord: "l" },
  { to: "/app/highlights", label: "Highlights", Icon: HighlightsIcon, chord: "h" },
  { to: "/app/recaps", label: "Recaps", Icon: RecapsIcon, chord: "r" },
  { to: "/app/import", label: "Import", Icon: ImportIcon, chord: "i" },
  { to: "/app/settings", label: "Settings", Icon: SettingsIcon, chord: "s" },
] as const;

export function AppShell() {
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const { cycle } = useTheme();
  const { push } = useToasts();
  const { query, setQuery } = useSearch();
  const searchRef = useRef<HTMLInputElement>(null);

  const cycleTheme = useCallback(() => {
    const next = cycle();
    push(next === "system" ? "Theme follows your system." : `${capitalise(next)} theme.`);
  }, [cycle, push]);

  const [paletteOpen, setPaletteOpen] = useState(false);
  const [sheetOpen, setSheetOpen] = useState(false);

  const status = useQuery({
    queryKey: ["status"],
    queryFn: api.status,
    refetchInterval: 20_000,
    retry: false,
  });
  const settings = useQuery({ queryKey: ["settings"], queryFn: api.settings, retry: false });

  const go = useCallback((to: string) => navigate(to), [navigate]);
  const hasDestination = Boolean(
    settings.data?.kindleEmail?.trim() || settings.data?.deliveryEmail?.trim(),
  );
  const sendBlocked = settings.isSuccess && !hasDestination;

  const guardSend = useCallback(
    (send: () => Promise<unknown>, failure: string) => async () => {
      if (sendBlocked) {
        go("/app/settings");
        push("Add a reader address first — Relego has nowhere to send it.");
        return;
      }
      try {
        await send();
      } catch (error) {
        push(error instanceof Error ? error.message : failure, { tone: "bad" });
        return;
      }
      return true;
    },
    [go, push, sendBlocked],
  );

  const commands: Command[] = [
    ...NAV.map((item) => ({
      id: item.label.toLowerCase(),
      group: "Go to",
      label: item.label,
      hint: `g ${item.chord}`,
      run: () => go(item.to),
    })),
    {
      id: "send-recap",
      group: "Actions",
      label: "Send recap now",
      run: async () => {
        const sent = await guardSend(api.sendRecapNow, "Could not send the recap.")();
        if (!sent) return;
        void queryClient.invalidateQueries({ queryKey: ["recaps"] });
        void queryClient.invalidateQueries({ queryKey: ["status"] });
        push("Recap queued. It will be delivered shortly.");
      },
    },
    {
      id: "test-kindle",
      group: "Actions",
      label: "Send test email to your reader",
      run: async () => {
        const sent = await guardSend(api.testKindleEmail, "Could not send the test email.")();
        if (sent) push("Test email sent to your reader.");
      },
    },
    { id: "theme", group: "Actions", label: "Cycle theme", hint: "t", run: cycleTheme },
    {
      id: "shortcuts",
      group: "Actions",
      label: "Keyboard shortcuts",
      hint: "?",
      run: () => setSheetOpen(true),
    },
  ];

  useHotkeys({
    meta: { k: () => setPaletteOpen(true) },
    goChords: Object.fromEntries(NAV.map((item) => [item.chord, () => go(item.to)])),
    keys: {
      "/": () => searchRef.current?.focus(),
      "?": () => setSheetOpen(true),
      t: cycleTheme,
    },
  });

  // Esc inside a text field should hand focus back to the page, not stay trapped.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key !== "Escape") return;
      if (document.activeElement === searchRef.current) searchRef.current?.blur();
    }

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  const connected = status.isSuccess;
  const connectionState = status.isSuccess ? "online" : status.isError ? "offline" : "checking";
  const serverOrigin = window.location.origin;
  const serverLabel = window.location.host;

  return (
    <>
      <a className="skip-link" href="#main">
        Skip to content
      </a>

      <div className="app">
        <aside className="rail">
          <div className="wordmark">
            <span className="mark">
              relego<span className="dot">.</span>
            </span>
            {status.data?.serverVersion ? <small>v{status.data.serverVersion}</small> : null}
            <span className="beta-badge" title="The web UI is in beta. You may encounter rough edges.">beta</span>
          </div>

          <nav className="nav" aria-label="Primary">
            {NAV.map(({ to, label, Icon }) => (
              <NavLink key={to} to={to} end={to === "/app"} className="nav-item">
                <Icon className="icon" />
                {label}
                {label === "Library" && status.data ? (
                  <span className="count">{formatCount(status.data.totalBooks)}</span>
                ) : null}
                {label === "Highlights" && status.data ? (
                  <span className="count">{formatCount(status.data.totalHighlights)}</span>
                ) : null}
              </NavLink>
            ))}
          </nav>

          <div className="rail-foot">
            <button className="nav-item" type="button" onClick={() => setSheetOpen(true)}>
              <KeyboardIcon className="icon" />
              Shortcuts <kbd>?</kbd>
            </button>

            <div className="conn" data-state={connectionState}>
              <span className="led" aria-hidden="true" />
              <span className="conn-label">
                {connected ? "Connected" : status.isError ? "Disconnected" : "Connecting…"}
              </span>
              <code title={serverOrigin}>{serverLabel}</code>
            </div>

            <ThemeSwitch />
          </div>
        </aside>

        <div className="main">
          <header className="topbar">
            <span className="mobile-mark" aria-hidden="true">
              relego<span className="dot">.</span>
            </span>

            <label className="search">
              <SearchIcon strokeWidth={1.8} />
              <span className="sr-only">Search books and highlights</span>
              <input
                ref={searchRef}
                type="search"
                placeholder="Search books and highlights"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === "Enter" && location.pathname !== "/app/highlights") {
                    go("/app/highlights");
                  }
                }}
              />
              <kbd aria-hidden="true">/</kbd>
            </label>

            <div className="spacer" />

            <button
              className="btn btn--ghost"
              type="button"
              data-palette
              onClick={() => setPaletteOpen(true)}
              aria-haspopup="dialog"
            >
              <kbd aria-hidden="true">Ctrl K</kbd>
              <span className="cmd-label">Commands</span>
            </button>
          </header>

          <main id="main">
            <Outlet />
          </main>
        </div>

        <nav className="tabbar" aria-label="Primary">
          {NAV.map(({ to, label, Icon }) => (
            <NavLink key={to} to={to} end={to === "/app"}>
              <Icon />
              {label}
            </NavLink>
          ))}
        </nav>
      </div>

      <CommandPalette
        open={paletteOpen}
        onClose={() => setPaletteOpen(false)}
        commands={commands}
      />
      <ShortcutsSheet open={sheetOpen} onClose={() => setSheetOpen(false)} />
      <ToastHost />
    </>
  );
}
