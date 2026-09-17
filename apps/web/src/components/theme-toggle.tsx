"use client";

import { useSyncExternalStore } from "react";

type Theme = "system" | "light" | "dark";

const themes: Theme[] = ["system", "light", "dark"];
const labels: Record<Theme, string> = {
  system: "Sistema",
  light: "Claro",
  dark: "Escuro",
};
const icons: Record<Theme, string> = {
  system: "◐",
  light: "☼",
  dark: "☾",
};
const themeEvent = "mnemora-theme-change";

function getTheme(): Theme {
  const saved = localStorage.getItem("mnemora-theme");
  return saved === "light" || saved === "dark" ? saved : "system";
}

function getServerTheme(): Theme {
  return "system";
}

function subscribe(onStoreChange: () => void) {
  const sync = () => {
    applyTheme(getTheme());
    onStoreChange();
  };
  window.addEventListener("storage", sync);
  window.addEventListener(themeEvent, sync);
  return () => {
    window.removeEventListener("storage", sync);
    window.removeEventListener(themeEvent, sync);
  };
}

function applyTheme(theme: Theme) {
  if (theme === "system") delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = theme;
}

export function ThemeToggle({ compact = false }: { compact?: boolean }) {
  const theme = useSyncExternalStore(subscribe, getTheme, getServerTheme);

  function cycleTheme() {
    const next = themes[(themes.indexOf(theme) + 1) % themes.length];
    applyTheme(next);
    if (next === "system") localStorage.removeItem("mnemora-theme");
    else localStorage.setItem("mnemora-theme", next);
    window.dispatchEvent(new Event(themeEvent));
  }

  const next = themes[(themes.indexOf(theme) + 1) % themes.length];

  return (
    <button
      className={`theme-toggle${compact ? " theme-toggle-compact" : ""}`}
      type="button"
      onClick={cycleTheme}
      aria-label={`Tema: ${labels[theme]}. Mudar para ${labels[next].toLowerCase()}.`}
      title={`Tema: ${labels[theme]}`}
    >
      <span aria-hidden="true">{icons[theme]}</span>
      {!compact && <span>{labels[theme]}</span>}
    </button>
  );
}
