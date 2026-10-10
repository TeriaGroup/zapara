// Витрина компонентов для проверки глазами и скриншотов (#11). Не входит в сборку приложения.
import { useState } from "react";
import { createRoot } from "react-dom/client";
import "../tokens.css";
import "../styles.css";
import "./ui.css";
import { Button, Card, Chip, ConfirmDialog, EmptyState, Input, LessonRow, SegmentedControl } from "./components.tsx";

function Gallery() {
  const theme = new URLSearchParams(location.search).get("theme") === "light" ? "light" : "dark";
  document.documentElement.dataset.theme = theme;
  const [view, setView] = useState<"day" | "week">("day");
  const [open, setOpen] = useState(new URLSearchParams(location.search).has("dialog"));
  return (
    <main style={{ padding: "var(--zp-space-5)", display: "grid", gap: "var(--zp-space-4)", maxWidth: 880 }}>
      <Card title="Button">
        <div style={{ display: "flex", gap: "var(--zp-space-2)", flexWrap: "wrap" }}>
          <Button variant="primary">Сохранить</Button><Button>Отмена</Button><Button variant="ghost">Подробнее</Button>
          <Button variant="danger">Удалить</Button><Button variant="primary" disabled>Недоступно</Button>
        </div>
      </Card>
      <Card title="Input">
        <div style={{ display: "grid", gap: "var(--zp-space-3)", gridTemplateColumns: "1fr 1fr" }}>
          <Input label="Логин" hint="Латиница, цифры и точка" placeholder="ivanov.a" />
          <Input label="Пароль" type="password" defaultValue="short" error="Не меньше 12 символов" />
        </div>
      </Card>
      <Card title="SegmentedControl · Chip">
        <div style={{ display: "flex", gap: "var(--zp-space-3)", flexWrap: "wrap", alignItems: "center" }}>
          <SegmentedControl label="Вид" value={view} onChange={setView} options={[{ value: "day", label: "День" }, { value: "week", label: "Неделя" }]} />
          {["лек", "пр", "лаб", "конс", "зач", "экз", "курс", "Семинар"].map(type => <Chip key={type} lesson={type} />)}
          <Chip tone="success">Сдано</Chip><Chip tone="warning">Скоро срок</Chip><Chip tone="danger">Просрочено</Chip><Chip>Без срока</Chip>
        </div>
      </Card>
      <Card title="LessonRow">
        <div style={{ display: "grid", gap: "var(--zp-space-2)" }}>
          <LessonRow start="09:00" end="10:30" subject="Математический анализ" type="лек" room="413" teacher="Иванов И. И." state="past" />
          <LessonRow start="10:50" end="12:20" subject="Физика" type="пр" room="323" teacher="Петрова А. С." state="now" />
          <LessonRow start="12:40" end="14:10" subject="Информатика" type="лаб" room="216" state="next" />
        </div>
      </Card>
      <Card title="EmptyState"><EmptyState title="Заданий пока нет" hint="Добавьте задание сами или дождитесь задания от старосты." action={<Button variant="primary" onClick={() => setOpen(true)}>Добавить задание</Button>} /></Card>
      <ConfirmDialog open={open} danger title="Удалить задание?" body="«Физика: задачи 1–5». Его нельзя будет восстановить." confirmLabel="Удалить" onConfirm={() => setOpen(false)} onCancel={() => setOpen(false)} />
    </main>
  );
}
createRoot(document.getElementById("root")!).render(<Gallery />);
