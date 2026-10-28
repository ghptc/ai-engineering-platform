import { create } from "zustand";
export const usePreferences = create<{ dark: boolean; toggle: () => void }>(
  (set) => ({
    dark: localStorage.getItem("dark") !== "false",
    toggle: () =>
      set((s) => {
        localStorage.setItem("dark", String(!s.dark));
        return { dark: !s.dark };
      }),
  }),
);
