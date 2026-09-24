const paths = {
  calendar: "M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M16 2v4 M8 2v4 M3 10h18",
  week: "M3 3h7v7H3z M14 3h7v7h-7z M14 14h7v7h-7z M3 14h7v7H3z",
  summary: "M3 3v18h18 M18 17V9 M13 17V5 M8 17v-3",
  teachers: "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z M22 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75",
  map: "M3 6l6-3 6 3 6-3v15l-6 3-6-3-6 3z M9 3v15 M15 6v15",
  friends: "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M12 18a6 6 0 1 0 0-12 6 6 0 0 0 0 12z M12 14a2 2 0 1 0 0-4 2 2 0 0 0 0 4z",
  homework: "M4 4h12l4 4v12H4z M8 12h8 M8 16h5",
  community: "M12 12a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7z M8 21v-2a4 4 0 0 1 4-4h0a4 4 0 0 1 4 4v2 M4.5 10a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z M1 20.5v-1A3 3 0 0 1 4 16.5h1.2 M19.5 10a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z M23 20.5v-1a3 3 0 0 0-3-3.5h-1.2",
  users: "M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z M23 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75",
  settings: "M21 4h-9 M8 4H3 M21 12h-5 M12 12H3 M21 20h-7 M10 20H3 M12 2v4 M16 10v4 M10 18v4",
  chat: "M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z",
  left: "M15 18l-6-6 6-6",
  right: "M9 18l6-6-6-6",
  down: "M6 9l6 6 6-6",
  up: "M18 15l-6-6-6 6",
  sun: "M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10z M12 1v2 M12 21v2 M4.22 4.22l1.42 1.42 M18.36 18.36l1.42 1.42 M1 12h2 M21 12h2 M4.22 19.78l1.42-1.42 M18.36 5.64l1.42-1.42",
  moon: "M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z",
  refresh: "M21 12a9 9 0 1 1-3-6.7 M21 3v6h-6",
  plus: "M12 5v14 M5 12h14",
  more: "M5 12h.01 M12 12h.01 M19 12h.01",
  smile: "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M8 14s1.5 2 4 2 4-2 4-2 M9 9h.01 M15 9h.01",
  circle: "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z",
  mic: "M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z M19 10v2a7 7 0 0 1-14 0v-2 M12 19v4 M8 23h8",
  menu: "M4 6h16 M4 12h16 M4 18h16",
  file: "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M8 13h8 M8 17h6",
  shield: "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"
} as const;

export type IconName = keyof typeof paths;

export function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  return (
    <svg className="ico" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d={paths[name]} />
    </svg>
  );
}
