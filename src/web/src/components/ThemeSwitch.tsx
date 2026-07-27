import { useTheme, type ThemePreference } from "../lib/theme";
import { MonitorIcon, MoonIcon, SunIcon } from "./icons";

const OPTIONS: { value: ThemePreference; label: string; Icon: typeof SunIcon }[] = [
  { value: "light", label: "Light", Icon: SunIcon },
  { value: "dark", label: "Dark", Icon: MoonIcon },
  { value: "system", label: "Auto", Icon: MonitorIcon },
];

export function ThemeSwitch({ wide = false, labelledBy }: { wide?: boolean; labelledBy?: string }) {
  const { preference, setPreference } = useTheme();

  return (
    <div
      className={wide ? "theme-switch theme-switch--wide" : "theme-switch"}
      role="group"
      {...(labelledBy ? { "aria-labelledby": labelledBy } : { "aria-label": "Color theme" })}
    >
      {OPTIONS.map(({ value, label, Icon }) => (
        <button
          key={value}
          type="button"
          aria-pressed={preference === value}
          onClick={() => setPreference(value)}
        >
          <Icon />
          {label}
        </button>
      ))}
    </div>
  );
}
