export const stickerPack: { id: string; title: string; fill: string }[] = [
  { id: "hi", title: "Привет", fill: "#f6c945" },
  { id: "ok", title: "Хорошо", fill: "#7dcea0" },
  { id: "love", title: "Люблю", fill: "#e07a8a" },
  { id: "laugh", title: "Смешно", fill: "#f5d76e" },
  { id: "think", title: "Думаю", fill: "#8eb4e8" },
  { id: "sleep", title: "Сплю", fill: "#6d7d8d" },
  { id: "coffee", title: "Кофе", fill: "#c4784a" },
  { id: "book", title: "Учёба", fill: "#e7d3a1" },
  { id: "late", title: "Опаздываю", fill: "#e0a15a" },
  { id: "done", title: "Готово", fill: "#8fd0a8" },
  { id: "fire", title: "Огонь", fill: "#e07a3d" },
  { id: "sad", title: "Грустно", fill: "#7aa2d4" },
  { id: "party", title: "Ура", fill: "#d98adf" },
  { id: "question", title: "Вопрос", fill: "#3d4c63" },
  { id: "thanks", title: "Спасибо", fill: "#f2b5c5" },
  { id: "cool", title: "Круто", fill: "#2a2a2a" }
];

export function stickerTitle(id: string | null) {
  return stickerPack.find(item => item.id === id)?.title || "Стикер";
}

export function Sticker({ id }: { id: string | null }) {
  const item = stickerPack.find(entry => entry.id === id) || { id: "", title: "Стикер", fill: "#3a3a3a" };
  return (
    <svg className="sticker" viewBox="0 0 120 120" role="img" aria-label={item.title}>
      <rect width="120" height="120" rx="28" fill={item.fill} />
      <Scene id={item.id} />
    </svg>
  );
}

function Scene({ id }: { id: string }) {
  const ink = id === "question" || id === "cool" ? "#f4f4f4" : "#1b1b1b";
  if (id === "coffee") return <><path d="M34 48h40v28a16 16 0 0 1-16 16H50a16 16 0 0 1-16-16V48z" fill="#fff" /><path d="M74 54h8a10 10 0 0 1 0 20h-8" fill="none" stroke="#fff" strokeWidth="6" /><path d="M46 28c0 8-8 8-8 16M60 24c0 8-8 8-8 16M74 28c0 8-8 8-8 16" fill="none" stroke={ink} strokeWidth="4" strokeLinecap="round" /></>;
  if (id === "book") return <><path d="M28 36h28c6 8 6 28 0 48H28V36zM92 36H64c-6 8-6 28 0 48h28V36z" fill="#fff" /><path d="M60 40v44" stroke={ink} strokeWidth="4" /></>;
  if (id === "late") return <><circle cx="60" cy="64" r="28" fill="#fff" /><path d="M60 48v18l12 8" fill="none" stroke={ink} strokeWidth="6" strokeLinecap="round" /><path d="M46 28h28" stroke={ink} strokeWidth="6" strokeLinecap="round" /></>;
  if (id === "done") return <><rect x="34" y="28" width="52" height="64" rx="8" fill="#fff" /><path d="M46 62l10 10 20-24" fill="none" stroke="#1f8a4c" strokeWidth="8" strokeLinecap="round" strokeLinejoin="round" /></>;
  if (id === "fire") return <path d="M60 18c8 16 2 22 2 22s12-4 16 10 0 36-18 48-34-8-32-26 14-22 14-22-2-12 18-32z" fill="#fff3d0" />;
  if (id === "party") return <><circle cx="60" cy="66" r="22" fill="#fff" /><path d="M28 30l8 14M92 28l-10 14M60 22v12M34 96l8-10M86 96l-8-10" stroke="#fff" strokeWidth="6" strokeLinecap="round" /></>;
  if (id === "question") return <text x="60" y="82" textAnchor="middle" fontSize="72" fontFamily="Inter, sans-serif" fill="#f4f4f4">?</text>;
  const eyes = id === "sleep" ? null : <><circle cx="48" cy="58" r="4" fill={ink} /><circle cx="72" cy="58" r="4" fill={ink} /></>;
  const mouth = id === "sad" ? "M46 82c6-8 22-8 28 0" : id === "think" ? "M50 80h20" : id === "cool" ? "M46 78c8 8 20 8 28 0" : "M44 74c8 12 24 12 32 0";
  return (
    <>
      <circle cx="60" cy="64" r="32" fill={id === "love" ? "#fff" : "#fff"} />
      {id === "love" ? <path d="M60 86c-16-10-26-22-18-34 6-8 16-4 18 4 2-8 12-12 18-4 8 12-2 24-18 34z" fill="#e04b64" /> : (
        <>
          {id === "cool" ? <rect x="36" y="50" width="48" height="14" rx="6" fill="#1b1b1b" /> : eyes}
          {id === "sleep" && <path d="M40 56h16M64 56h16" stroke={ink} strokeWidth="4" strokeLinecap="round" />}
          <path d={mouth} fill="none" stroke={ink} strokeWidth="4" strokeLinecap="round" />
          {id === "hi" && <path d="M86 40c8-14 18-8 14 6" fill="none" stroke={ink} strokeWidth="5" strokeLinecap="round" />}
          {id === "thanks" && <path d="M44 42c0 10 16 10 16 0M60 42c0 10 16 10 16 0" fill="none" stroke={ink} strokeWidth="4" />}
          {id === "ok" && <path d="M46 66l8 8 18-20" fill="none" stroke="#1f8a4c" strokeWidth="7" strokeLinecap="round" />}
        </>
      )}
    </>
  );
}
