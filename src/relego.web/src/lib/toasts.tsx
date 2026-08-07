import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";

export interface Toast {
  id: number;
  message: string;
  tone: "ok" | "bad";
  action?: { label: string; run: () => void };
}

interface ToastContextValue {
  toasts: Toast[];
  push: (message: string, options?: { tone?: Toast["tone"]; action?: Toast["action"]; ms?: number }) => void;
  dismiss: (id: number) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(1);

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((toast) => toast.id !== id));
  }, []);

  const push = useCallback<ToastContextValue["push"]>(
    (message, options) => {
      const id = nextId.current++;
      const toast: Toast = { id, message, tone: options?.tone ?? "ok" };
      if (options?.action) toast.action = options.action;

      setToasts((current) => [...current, toast]);

      // Undoable toasts linger; a plain confirmation clears itself quickly.
      window.setTimeout(() => dismiss(id), options?.ms ?? (options?.action ? 8000 : 4000));
    },
    [dismiss],
  );

  const value = useMemo(() => ({ toasts, push, dismiss }), [toasts, push, dismiss]);

  return <ToastContext.Provider value={value}>{children}</ToastContext.Provider>;
}

export function useToasts(): ToastContextValue {
  const context = useContext(ToastContext);
  if (!context) throw new Error("useToasts must be used inside ToastProvider");
  return context;
}
