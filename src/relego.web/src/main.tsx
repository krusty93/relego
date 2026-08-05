import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { AppShell } from "./components/AppShell";
import { SearchProvider } from "./lib/search";
import { ThemeProvider } from "./lib/theme";
import { ToastProvider } from "./lib/toasts";
import { HighlightsPage } from "./routes/HighlightsPage";
import { ImportPage } from "./routes/ImportPage";
import { LibraryPage } from "./routes/LibraryPage";
import { RecapsPage } from "./routes/RecapsPage";
import { SettingsPage } from "./routes/SettingsPage";
import "./styles/global.css";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 10_000, refetchOnWindowFocus: false, retry: false },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <ToastProvider>
          <SearchProvider>
            <BrowserRouter>
              <Routes>
                <Route element={<AppShell />}>
                  <Route path="/" element={<LibraryPage />} />
                  <Route path="/highlights" element={<HighlightsPage />} />
                  <Route path="/books/:bookId" element={<HighlightsPage />} />
                  <Route path="/recaps" element={<RecapsPage />} />
                  <Route path="/import" element={<ImportPage />} />
                  <Route path="/settings" element={<SettingsPage />} />
                  <Route path="*" element={<Navigate to="/" replace />} />
                </Route>
              </Routes>
            </BrowserRouter>
          </SearchProvider>
        </ToastProvider>
      </ThemeProvider>
    </QueryClientProvider>
  </StrictMode>,
);
